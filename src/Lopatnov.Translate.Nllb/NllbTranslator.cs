using Lopatnov.Translate.Core.Abstractions;
using Lopatnov.Translate.Nllb.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Lopatnov.Translate.Nllb;

public sealed class NllbTranslator : ITextTranslator, IDisposable
{
    private readonly NllbOptions _options;
    private readonly INllbTokenizer _tokenizer;
    private readonly IOnnxSession _encoderSession;
    private readonly IOnnxSession _decoderSession;
    private readonly bool _ownsTokenizer;
    private readonly bool _ownsEncoder;
    private readonly bool _ownsDecoder;

    public NllbTranslator(IOptions<NllbOptions> options)
        : this(options.Value, null, null, null) { }

    public NllbTranslator(NllbOptions options, INllbTokenizer? tokenizer, IOnnxSession? encoderSession, IOnnxSession? decoderSession)
    {
        if (options.BeamSize > 1)
            throw new NotSupportedException($"BeamSize > 1 is not implemented; only greedy decoding (BeamSize = 1) is supported.");

        _options = options;
        _ownsTokenizer = tokenizer is null;
        _tokenizer = tokenizer ?? new NllbTokenizer(options.Path, options.TokenizerFile, options.TokenizerConfigFile);
        _ownsEncoder = encoderSession is null;
        _encoderSession = encoderSession ?? new OnnxSessionAdapter(Path.Combine(options.Path, options.EncoderFile));
        _ownsDecoder = decoderSession is null;
        _decoderSession = decoderSession ?? new OnnxSessionAdapter(Path.Combine(options.Path, options.DecoderFile));
    }

    public Task<string> TranslateAsync(string text, string sourceLanguage, string targetLanguage, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.Run(() => Translate(text, sourceLanguage, targetLanguage, cancellationToken), cancellationToken);
    }

    private string Translate(string text, string sourceLanguage, string targetLanguage, CancellationToken cancellationToken)
    {
        var inputIds = _tokenizer.Encode(text, sourceLanguage);
        var attentionMask = new long[inputIds.Length];
        Array.Fill(attentionMask, 1L);

        var encoderOutputs = _encoderSession.Run([
            NamedOnnxValue.CreateFromTensor("input_ids", CreateLongTensor(inputIds)),
            NamedOnnxValue.CreateFromTensor("attention_mask", CreateLongTensor(attentionMask)),
        ]);

        var encoderHiddenState = encoderOutputs
            .First(o => o.Name == "last_hidden_state")
            .AsTensor<float>();

        var targetLangId = _tokenizer.GetLanguageTokenId(targetLanguage);

        // Pre-allocate full decoder buffer to avoid per-step array allocations.
        // Layout: [EOS, tgt_lang_id, ...generated tokens...]
        var decoderBuf = new long[_options.MaxTokens + 2];
        decoderBuf[0] = NllbTokenizer.EosTokenId;
        decoderBuf[1] = targetLangId;
        var decoderCount = 2;

        for (var step = 0; step < _options.MaxTokens; step++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var decoderOutputs = _decoderSession.Run([
                NamedOnnxValue.CreateFromTensor("input_ids", CreateLongTensor(decoderBuf, decoderCount)),
                NamedOnnxValue.CreateFromTensor("encoder_hidden_states", encoderHiddenState),
                NamedOnnxValue.CreateFromTensor("encoder_attention_mask", CreateLongTensor(attentionMask)),
            ]);

            var logits = decoderOutputs
                .First(o => o.Name == "logits")
                .AsTensor<float>();

            var nextToken = Argmax(logits, decoderCount - 1);

            if (nextToken == NllbTokenizer.EosTokenId)
                break;

            decoderBuf[decoderCount++] = nextToken;
        }

        // Skip decoder_start_token (EOS) and forced target-lang BOS
        return _tokenizer.Decode(new ArraySegment<long>(decoderBuf, 2, decoderCount - 2));
    }

    public void Dispose()
    {
        if (_ownsTokenizer) _tokenizer.Dispose();
        if (_ownsEncoder) _encoderSession.Dispose();
        if (_ownsDecoder) _decoderSession.Dispose();
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
