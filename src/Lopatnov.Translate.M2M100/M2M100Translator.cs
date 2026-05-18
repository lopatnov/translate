using System.Buffers;
using Lopatnov.Translate.Core.Abstractions;
using Lopatnov.Translate.M2M100.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Lopatnov.Translate.M2M100;

public sealed class M2M100Translator : ITextTranslator, IDisposable
{
    private readonly M2M100Options _options;
    private readonly IM2M100Tokenizer _tokenizer;
    private readonly IOnnxSession _encoderSession;
    private readonly IOnnxSession _decoderSession;
    private readonly bool _ownsTokenizer;
    private readonly bool _ownsEncoder;
    private readonly bool _ownsDecoder;
    private readonly SessionOptions? _sessionOptions;
    private readonly bool _ownsSessionOptions;

    // OnnxRuntime InferenceSession.Run is not safe for concurrent calls on the
    // same session when native state is shared (observed as 0xC0000005 crashes
    // under concurrent live-translation load). Serialize with a semaphore.
    private readonly SemaphoreSlim _inferenceLock = new(1, 1);

    // Server-side inference timeout, read once at startup.
    // Set TRANSLATE_TIMEOUT_MS env var to impose a limit (ms).
    // When unset: inference runs to completion regardless of the gRPC client deadline.
    private static readonly TimeSpan? InferenceTimeout = ReadInferenceTimeout();
    private static TimeSpan? ReadInferenceTimeout()
    {
        var raw = Environment.GetEnvironmentVariable("TRANSLATE_TIMEOUT_MS");
        return int.TryParse(raw, out var ms) && ms > 0
            ? TimeSpan.FromMilliseconds(ms)
            : null;
    }

    public M2M100Translator(IOptions<M2M100Options> options)
        : this(options.Value, null, null, null, null) { }

    public M2M100Translator(M2M100Options options, IM2M100Tokenizer? tokenizer,
        IOnnxSession? encoderSession, IOnnxSession? decoderSession,
        SessionOptions? sessionOptions = null)
    {
        if (options.MaxTokens <= 0)
            throw new ArgumentException($"MaxTokens must be > 0, got {options.MaxTokens}.", nameof(options));

        _options = options;
        _ownsTokenizer = tokenizer is null;
        _tokenizer = tokenizer ?? new M2M100Tokenizer(options.Path, options.TokenizerFile,
            options.TokenizerConfigFile, options.VocabFile);
        _ownsEncoder = encoderSession is null;
        _encoderSession = encoderSession ?? new OnnxSessionAdapter(
            Path.Combine(options.Path, options.EncoderFile), sessionOptions);
        _ownsDecoder = decoderSession is null;
        _decoderSession = decoderSession ?? new OnnxSessionAdapter(
            Path.Combine(options.Path, options.DecoderFile), sessionOptions);
        // Own sessionOptions only when it was provided and used to construct at least one session.
        // After session construction ONNX has copied all relevant config, so we can dispose freely.
        _sessionOptions = sessionOptions;
        _ownsSessionOptions = sessionOptions is not null && (_ownsEncoder || _ownsDecoder);
    }

    public async Task<string> TranslateAsync(string text, string sourceLanguage, string targetLanguage,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await _inferenceLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Use a server-side timeout token (if TRANSLATE_TIMEOUT_MS is set),
            // otherwise CancellationToken.None — so inference always runs to completion
            // regardless of the gRPC client deadline. The outer cancellationToken is still
            // respected at two earlier checkpoints: ThrowIfCancellationRequested() above
            // and WaitAsync(), plus Task.Run won't start if already cancelled.
            using var inferenceCts = InferenceTimeout.HasValue
                ? new CancellationTokenSource(InferenceTimeout.Value)
                : new CancellationTokenSource();
            return await Task.Run(
                () => Translate(text, sourceLanguage, targetLanguage, inferenceCts.Token),
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            // Guard against ObjectDisposedException if Dispose() races with this finally.
            try { _inferenceLock.Release(); }
            catch (ObjectDisposedException) { /* disposal already in progress */ }
        }
    }

    private string Translate(string text, string sourceLanguage, string targetLanguage,
        CancellationToken cancellationToken)
    {
        var inputIds = _tokenizer.Encode(text, sourceLanguage);
        var attentionMask = new long[inputIds.Length];
        Array.Fill(attentionMask, 1L);

        // Encoder: copy hidden state into managed memory — it must survive across all decode steps.
        DenseTensor<float>? encoderHiddenState = null;
        _encoderSession.Run(
            [
                NamedOnnxValue.CreateFromTensor("input_ids", CreateLongTensor(inputIds)),
                NamedOnnxValue.CreateFromTensor("attention_mask", CreateLongTensor(attentionMask)),
            ],
            ["last_hidden_state"],
            outputs =>
            {
                var t = outputs.First(o => o.Name == "last_hidden_state").AsTensor<float>();
                encoderHiddenState = new DenseTensor<float>(t.ToArray(), t.Dimensions.ToArray());
            });

        var targetLangId = _tokenizer.GetLanguageTokenId(targetLanguage);

        // Rent a decoder buffer from the pool to avoid per-request heap allocation.
        // Layout: [EOS, tgt_lang_id, ...generated tokens...]
        var decoderBuf = ArrayPool<long>.Shared.Rent(_options.MaxTokens + 2);
        try
        {
            decoderBuf[0] = M2M100Tokenizer.EosTokenId;
            decoderBuf[1] = targetLangId;
            var decoderCount = 2;

            // CancellationToken is only checked between decode steps; ONNX calls are not interruptible mid-run.
            for (var step = 0; step < _options.MaxTokens; step++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var nextToken = RunDecoderStep(decoderBuf, decoderCount, encoderHiddenState!, attentionMask);

                if (nextToken == M2M100Tokenizer.EosTokenId)
                    break;

                decoderBuf[decoderCount++] = nextToken;
            }

            // Skip decoder_start_token (EOS) and forced target-lang BOS
            return _tokenizer.Decode(new ArraySegment<long>(decoderBuf, 2, decoderCount - 2));
        }
        finally
        {
            ArrayPool<long>.Shared.Return(decoderBuf);
        }
    }

    public void Dispose()
    {
        // Acquire the inference lock before disposing ONNX sessions to ensure no
        // in-flight translation is executing inside the lock. Without this, a concurrent
        // TranslateAsync finally-block calling Release() after disposal throws
        // ObjectDisposedException.
        // Acquire the lock and keep it (do NOT Release before Dispose).
        // Holding the lock at count=0 ensures:
        //   1. Any in-flight TranslateAsync has already exited the lock.
        //   2. Callers queued on WaitAsync receive ObjectDisposedException when
        //      we call _inferenceLock.Dispose() below, so they cannot enter
        //      inference against already-disposed ONNX sessions.
        _inferenceLock.Wait();
        if (_ownsTokenizer) _tokenizer.Dispose();
        if (_ownsEncoder) _encoderSession.Dispose();
        if (_ownsDecoder) _decoderSession.Dispose();
        if (_ownsSessionOptions) _sessionOptions!.Dispose();
        _inferenceLock.Dispose(); // signals queued WaitAsync callers with ObjectDisposedException
    }

    private long RunDecoderStep(long[] decoderBuf, int decoderCount,
        DenseTensor<float> encoderHiddenState, long[] attentionMask)
    {
        var nextToken = -1L;
        _decoderSession.Run(
            [
                NamedOnnxValue.CreateFromTensor("input_ids", CreateLongTensor(decoderBuf, decoderCount)),
                NamedOnnxValue.CreateFromTensor("encoder_hidden_states", encoderHiddenState),
                NamedOnnxValue.CreateFromTensor("encoder_attention_mask", CreateLongTensor(attentionMask)),
            ],
            ["logits"],
            outputs => nextToken = Argmax(
                outputs.First(o => o.Name == "logits").AsTensor<float>(),
                decoderCount - 1));
        return nextToken;
    }

    private static DenseTensor<long> CreateLongTensor(long[] data)
        => new(data, new[] { 1, data.Length });

    private static DenseTensor<long> CreateLongTensor(long[] data, int length)
        => new(new Memory<long>(data, 0, length), new[] { 1, length });

    private static long Argmax(Tensor<float> logits, int position)
    {
        var vocabSize = logits.Dimensions[2];

        if (logits is DenseTensor<float> dense)
        {
            var span = dense.Buffer.Span.Slice(position * vocabSize, vocabSize);
            var maxIdx = 0;
            for (var v = 1; v < span.Length; v++)
                if (span[v] > span[maxIdx]) maxIdx = v;
            return maxIdx;
        }

        var maxVal = float.NegativeInfinity;
        var maxIdxFallback = 0L;
        for (var v = 0; v < vocabSize; v++)
        {
            var val = logits[0, position, v];
            if (val > maxVal) { maxVal = val; maxIdxFallback = v; }
        }
        return maxIdxFallback;
    }
}
