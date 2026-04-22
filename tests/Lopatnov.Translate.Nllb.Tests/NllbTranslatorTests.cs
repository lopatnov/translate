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

        // Encoder: provide a minimal hidden state [1, seqLen, 4] via the callback.
        encoderMock
            .Setup(s => s.Run(
                It.IsAny<IReadOnlyCollection<NamedOnnxValue>>(),
                It.IsAny<IReadOnlyCollection<string>?>(),
                It.IsAny<Action<IEnumerable<NamedOnnxValue>>>()))
            .Callback<IReadOnlyCollection<NamedOnnxValue>, IReadOnlyCollection<string>?, Action<IEnumerable<NamedOnnxValue>>>(
                (inputs, _, process) =>
                {
                    var seqLen = inputs.First(i => i.Name == "input_ids").AsTensor<long>().Dimensions[1];
                    var hiddenState = new DenseTensor<float>(new float[seqLen * 4], new[] { 1, seqLen, 4 });
                    process([NamedOnnxValue.CreateFromTensor("last_hidden_state", hiddenState)]);
                });

        // Decoder: return EOS at the last position to stop the loop on the first step.
        decoderMock
            .Setup(s => s.Run(
                It.IsAny<IReadOnlyCollection<NamedOnnxValue>>(),
                It.IsAny<IReadOnlyCollection<string>?>(),
                It.IsAny<Action<IEnumerable<NamedOnnxValue>>>()))
            .Callback<IReadOnlyCollection<NamedOnnxValue>, IReadOnlyCollection<string>?, Action<IEnumerable<NamedOnnxValue>>>(
                (inputs, _, process) =>
                {
                    var seqLen = inputs.First(i => i.Name == "input_ids").AsTensor<long>().Dimensions[1];
                    const int vocabSize = 10;
                    var logits = new DenseTensor<float>(new float[seqLen * vocabSize], new[] { 1, seqLen, vocabSize });
                    logits[0, seqLen - 1, (int)NllbTokenizer.EosTokenId] = 100f;
                    process([NamedOnnxValue.CreateFromTensor("logits", logits)]);
                });

        var options = new NllbOptions { MaxTokens = 10 };
        using var translator = new NllbTranslator(options, new FakeNllbTokenizer(), encoderMock.Object, decoderMock.Object);

        await translator.TranslateAsync("hello", "eng_Latn", "ukr_Cyrl");

        encoderMock.Verify(s => s.Run(
            It.Is<IReadOnlyCollection<NamedOnnxValue>>(
                inputs => inputs.Any(i => i.Name == "input_ids") &&
                          inputs.Any(i => i.Name == "attention_mask")),
            It.IsAny<IReadOnlyCollection<string>?>(),
            It.IsAny<Action<IEnumerable<NamedOnnxValue>>>()), Times.Once);
    }
}
