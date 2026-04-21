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

    public NllbTranslator(IOptions<NllbOptions> options)
        : this(options.Value, null, null, null) { }

    public NllbTranslator(NllbOptions options, INllbTokenizer? tokenizer, IOnnxSession? encoderSession, IOnnxSession? decoderSession)
    {
        _options = options;
        _tokenizer = tokenizer ?? new NllbTokenizer(options.Path, options.TokenizerFile, options.TokenizerConfigFile);
        _encoderSession = encoderSession ?? new OnnxSessionAdapter(Path.Combine(options.Path, options.EncoderFile));
        _decoderSession = decoderSession ?? new OnnxSessionAdapter(Path.Combine(options.Path, options.DecoderFile));
    }

    public Task<string> TranslateAsync(string text, string sourceLanguage, string targetLanguage, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var inputIds = _tokenizer.Encode(text, sourceLanguage);
        var attentionMask = Enumerable.Repeat(1L, inputIds.Length).ToArray();

        var encoderOutputs = _encoderSession.Run(new[]
        {
            NamedOnnxValue.CreateFromTensor("input_ids", CreateLongTensor(inputIds)),
            NamedOnnxValue.CreateFromTensor("attention_mask", CreateLongTensor(attentionMask)),
        });

        var encoderHiddenState = encoderOutputs
            .First(o => o.Name == "last_hidden_state")
            .AsTensor<float>();

        var targetLangId = _tokenizer.GetLanguageTokenId(targetLanguage);
        // NLLB decoder_start_token_id=2 (</s>), followed by forced target-language BOS
        var decoderIds = new List<long> { NllbTokenizer.EosTokenId, targetLangId };

        for (var step = 0; step < _options.MaxTokens; step++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var decoderOutputs = _decoderSession.Run(new[]
            {
                NamedOnnxValue.CreateFromTensor("input_ids", CreateLongTensor(decoderIds.ToArray())),
                NamedOnnxValue.CreateFromTensor("encoder_hidden_states", encoderHiddenState),
                NamedOnnxValue.CreateFromTensor("encoder_attention_mask", CreateLongTensor(attentionMask)),
            });

            var logits = decoderOutputs
                .First(o => o.Name == "logits")
                .AsTensor<float>();

            var nextToken = Argmax(logits, decoderIds.Count - 1);

            if (nextToken == NllbTokenizer.EosTokenId)
                break;

            decoderIds.Add(nextToken);
        }

        // Skip decoder_start_token (2) and forced target-lang BOS
        return Task.FromResult(_tokenizer.Decode(decoderIds.Skip(2)));
    }

    public void Dispose()
    {
        _tokenizer.Dispose();
        _encoderSession.Dispose();
        _decoderSession.Dispose();
    }

    private static DenseTensor<long> CreateLongTensor(long[] data)
        => new(data, new[] { 1, data.Length });

    private static long Argmax(Tensor<float> logits, int position)
    {
        var vocabSize = logits.Dimensions[2];
        var maxVal = float.NegativeInfinity;
        var maxIdx = 0L;
        for (var v = 0; v < vocabSize; v++)
        {
            var val = logits[0, position, v];
            if (val > maxVal)
            {
                maxVal = val;
                maxIdx = v;
            }
        }
        return maxIdx;
    }
}
