using Lopatnov.Translate.Nllb;
using Lopatnov.Translate.Nllb.Abstractions;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Moq;

namespace Lopatnov.Translate.Nllb.Tests;

public sealed class NllbTranslatorTests
{
    [Fact]
    public async Task TranslateAsync_CallsEncoderWithInputIdsAndAttentionMask()
    {
        var encoderMock = new Mock<IOnnxSession>();
        var decoderMock = new Mock<IOnnxSession>();

        // Encoder returns a minimal hidden state: [1, seqLen, 4]
        encoderMock
            .Setup(s => s.Run(It.IsAny<IReadOnlyCollection<NamedOnnxValue>>()))
            .Returns<IReadOnlyCollection<NamedOnnxValue>>(inputs =>
            {
                var inputIds = inputs.First(i => i.Name == "input_ids").AsTensor<long>();
                var seqLen = inputIds.Dimensions[1];
                var hiddenState = new DenseTensor<float>(new float[1 * seqLen * 4], new[] { 1, seqLen, 4 });
                return new[] { NamedOnnxValue.CreateFromTensor("last_hidden_state", hiddenState) };
            });

        // Decoder returns EOS at the last position to stop the loop immediately.
        // Shape mirrors the actual decoder: [1, seqLen, vocabSize].
        decoderMock
            .Setup(s => s.Run(It.IsAny<IReadOnlyCollection<NamedOnnxValue>>()))
            .Returns<IReadOnlyCollection<NamedOnnxValue>>(inputs =>
            {
                var seqLen = inputs.First(i => i.Name == "input_ids").AsTensor<long>().Dimensions[1];
                var vocabSize = 10;
                var logits = new DenseTensor<float>(new float[seqLen * vocabSize], new[] { 1, seqLen, vocabSize });
                logits[0, seqLen - 1, (int)NllbTokenizer.EosTokenId] = 100f;
                return new[] { NamedOnnxValue.CreateFromTensor("logits", logits) };
            });

        var options = new NllbOptions { MaxTokens = 10 };
        using var translator = new NllbTranslator(options, new FakeNllbTokenizer(), encoderMock.Object, decoderMock.Object);

        await translator.TranslateAsync("hello", "eng_Latn", "ukr_Cyrl");

        encoderMock.Verify(s => s.Run(It.Is<IReadOnlyCollection<NamedOnnxValue>>(
            inputs => inputs.Any(i => i.Name == "input_ids") &&
                      inputs.Any(i => i.Name == "attention_mask"))), Times.Once);
    }
}
