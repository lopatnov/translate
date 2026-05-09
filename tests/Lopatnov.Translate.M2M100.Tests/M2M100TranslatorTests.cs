using Lopatnov.Translate.M2M100;
using Lopatnov.Translate.M2M100.Abstractions;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Moq;

namespace Lopatnov.Translate.M2M100.Tests;

public sealed class M2M100TranslatorTests
{
    [Fact]
    public async Task TranslateAsync_CallsEncoderWithInputIdsAndAttentionMask()
    {
        var encoderMock = new Mock<IOnnxSession>();
        var decoderMock = new Mock<IOnnxSession>();

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

        // Decoder returns EOS immediately to stop the loop.
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
                    logits[0, seqLen - 1, (int)M2M100Tokenizer.EosTokenId] = 100f;
                    process([NamedOnnxValue.CreateFromTensor("logits", logits)]);
                });

        var options = new M2M100Options { MaxTokens = 10 };
        using var translator = new M2M100Translator(options, new FakeM2M100Tokenizer(),
            encoderMock.Object, decoderMock.Object);

        await translator.TranslateAsync("hello", "eng_Latn", "ukr_Cyrl");

        encoderMock.Verify(s => s.Run(
            It.Is<IReadOnlyCollection<NamedOnnxValue>>(
                inputs => inputs.Any(i => i.Name == "input_ids") &&
                          inputs.Any(i => i.Name == "attention_mask")),
            It.IsAny<IReadOnlyCollection<string>?>(),
            It.IsAny<Action<IEnumerable<NamedOnnxValue>>>()), Times.Once);
    }

    [Fact]
    public async Task TranslateAsync_StopsAtEos()
    {
        var encoderMock = new Mock<IOnnxSession>();
        var decoderMock = new Mock<IOnnxSession>();
        var decoderCallCount = 0;

        encoderMock
            .Setup(s => s.Run(It.IsAny<IReadOnlyCollection<NamedOnnxValue>>(),
                It.IsAny<IReadOnlyCollection<string>?>(),
                It.IsAny<Action<IEnumerable<NamedOnnxValue>>>()))
            .Callback<IReadOnlyCollection<NamedOnnxValue>, IReadOnlyCollection<string>?, Action<IEnumerable<NamedOnnxValue>>>(
                (inputs, _, process) =>
                {
                    var seqLen = inputs.First(i => i.Name == "input_ids").AsTensor<long>().Dimensions[1];
                    process([NamedOnnxValue.CreateFromTensor("last_hidden_state",
                        new DenseTensor<float>(new float[seqLen * 4], new[] { 1, seqLen, 4 }))]);
                });

        // Return a real token on first call, EOS on second.
        decoderMock
            .Setup(s => s.Run(It.IsAny<IReadOnlyCollection<NamedOnnxValue>>(),
                It.IsAny<IReadOnlyCollection<string>?>(),
                It.IsAny<Action<IEnumerable<NamedOnnxValue>>>()))
            .Callback<IReadOnlyCollection<NamedOnnxValue>, IReadOnlyCollection<string>?, Action<IEnumerable<NamedOnnxValue>>>(
                (inputs, _, process) =>
                {
                    decoderCallCount++;
                    var seqLen = inputs.First(i => i.Name == "input_ids").AsTensor<long>().Dimensions[1];
                    const int vocabSize = 10;
                    var logits = new DenseTensor<float>(new float[seqLen * vocabSize], new[] { 1, seqLen, vocabSize });
                    // First call: return token 5; second: return EOS
                    var tokenToReturn = decoderCallCount == 1 ? 5 : (int)M2M100Tokenizer.EosTokenId;
                    logits[0, seqLen - 1, tokenToReturn] = 100f;
                    process([NamedOnnxValue.CreateFromTensor("logits", logits)]);
                });

        var options = new M2M100Options { MaxTokens = 100 };
        using var translator = new M2M100Translator(options, new FakeM2M100Tokenizer(),
            encoderMock.Object, decoderMock.Object);

        await translator.TranslateAsync("hi", "eng_Latn", "ukr_Cyrl");

        Assert.Equal(2, decoderCallCount);
    }

    [Fact]
    public async Task TranslateAsync_RespectsCancellation()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var options = new M2M100Options { MaxTokens = 10 };
        using var translator = new M2M100Translator(options, new FakeM2M100Tokenizer(),
            new Mock<IOnnxSession>().Object, new Mock<IOnnxSession>().Object);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            translator.TranslateAsync("hello", "eng_Latn", "ukr_Cyrl", cts.Token));
    }

    [Fact]
    public void Constructor_ThrowsWhenMaxTokensIsZero()
    {
        var options = new M2M100Options { MaxTokens = 0 };

        Assert.Throws<ArgumentException>(() =>
            new M2M100Translator(options, new FakeM2M100Tokenizer(),
                new Mock<IOnnxSession>().Object, new Mock<IOnnxSession>().Object));
    }
}
