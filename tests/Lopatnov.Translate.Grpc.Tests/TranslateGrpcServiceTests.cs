using Grpc.Core;
using Lopatnov.Translate.Core.Abstractions;
using Lopatnov.Translate.Grpc.Services;
using Moq;

namespace Lopatnov.Translate.Grpc.Tests;

public sealed class TranslateGrpcServiceTests
{
    private static readonly IEnumerable<ILanguageDetector> NoDetector = [];
    private static IEnumerable<ILanguageDetector> WithDetector(ILanguageDetector d) => [d];

    private static ModelSessionManager SingleProviderManager(string key, ITextTranslator translator)
        => new(
            new Dictionary<string, Func<ITextTranslator>> { [key] = () => translator },
            allowedProviders: [],
            ttl: TimeSpan.FromMinutes(30));

    [Theory]
    [InlineData("nllb")]
    [InlineData("libretranslate")]
    public async Task TranslateText_DispatchesToCorrectProvider(string provider)
    {
        var mockTranslator = new Mock<ITextTranslator>();
        mockTranslator
            .Setup(t => t.TranslateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("translated");

        var svc = new TranslateGrpcService(SingleProviderManager(provider, mockTranslator.Object), NoDetector);
        var ctx = new Mock<ServerCallContext>(MockBehavior.Loose);

        var response = await svc.TranslateText(new TranslateTextRequest
        {
            Text = "hello", SourceLanguage = "eng_Latn", TargetLanguage = "ukr_Cyrl", Provider = provider,
        }, ctx.Object);

        Assert.Equal("translated", response.TranslatedText);
        Assert.Equal(provider, response.ProviderUsed);
        mockTranslator.Verify(
            t => t.TranslateAsync("hello", "eng_Latn", "ukr_Cyrl", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task TranslateText_DefaultsToNllbWhenProviderIsEmpty()
    {
        var mockTranslator = new Mock<ITextTranslator>();
        mockTranslator
            .Setup(t => t.TranslateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("translated");

        var svc = new TranslateGrpcService(SingleProviderManager("nllb", mockTranslator.Object), NoDetector);
        var ctx = new Mock<ServerCallContext>(MockBehavior.Loose);

        var response = await svc.TranslateText(new TranslateTextRequest
        {
            Text = "hello", SourceLanguage = "eng_Latn", TargetLanguage = "ukr_Cyrl",
        }, ctx.Object);

        Assert.Equal("nllb", response.ProviderUsed);
        mockTranslator.Verify(
            t => t.TranslateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task TranslateText_ThrowsInvalidArgument_ForUnknownProvider()
    {
        var svc = new TranslateGrpcService(SingleProviderManager("nllb", new Mock<ITextTranslator>().Object), NoDetector);
        var ctx = new Mock<ServerCallContext>(MockBehavior.Loose);

        var ex = await Assert.ThrowsAsync<RpcException>(() =>
            svc.TranslateText(new TranslateTextRequest { Provider = "unknown" }, ctx.Object));

        Assert.Equal(StatusCode.InvalidArgument, ex.StatusCode);
    }

    [Fact]
    public async Task TranslateText_ThrowsPermissionDenied_ForNotAllowedProvider()
    {
        var mock = new Mock<ITextTranslator>();
        var manager = new ModelSessionManager(
            new Dictionary<string, Func<ITextTranslator>>
            {
                ["nllb"] = () => mock.Object,
                ["m2m100"] = () => mock.Object,
            },
            allowedProviders: ["nllb"],
            ttl: TimeSpan.FromMinutes(30));

        var svc = new TranslateGrpcService(manager, NoDetector);
        var ctx = new Mock<ServerCallContext>(MockBehavior.Loose);

        var ex = await Assert.ThrowsAsync<RpcException>(() =>
            svc.TranslateText(new TranslateTextRequest
            {
                Text = "hello", SourceLanguage = "eng_Latn", TargetLanguage = "ukr_Cyrl", Provider = "m2m100",
            }, ctx.Object));

        Assert.Equal(StatusCode.PermissionDenied, ex.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("auto")]
    public async Task TranslateText_ThrowsInvalidArgument_WhenAutoAndNoDetector(string sourceLanguage)
    {
        var svc = new TranslateGrpcService(SingleProviderManager("nllb", new Mock<ITextTranslator>().Object), NoDetector);
        var ctx = new Mock<ServerCallContext>(MockBehavior.Loose);

        var ex = await Assert.ThrowsAsync<RpcException>(() =>
            svc.TranslateText(new TranslateTextRequest { Text = "hello", SourceLanguage = sourceLanguage, Provider = "nllb" }, ctx.Object));

        Assert.Equal(StatusCode.InvalidArgument, ex.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("auto")]
    [InlineData("AUTO")]
    public async Task TranslateText_CallsDetector_WhenSourceLanguageIsAutoOrEmpty(string sourceLanguage)
    {
        var mockTranslator = new Mock<ITextTranslator>();
        mockTranslator
            .Setup(t => t.TranslateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("translated");

        var mockDetector = new Mock<ILanguageDetector>();
        mockDetector.Setup(d => d.Detect("hello")).Returns("ukr_Cyrl");

        var svc = new TranslateGrpcService(SingleProviderManager("nllb", mockTranslator.Object), WithDetector(mockDetector.Object));
        var ctx = new Mock<ServerCallContext>(MockBehavior.Loose);

        var response = await svc.TranslateText(new TranslateTextRequest
        {
            Text = "hello", SourceLanguage = sourceLanguage, TargetLanguage = "eng_Latn", Provider = "nllb",
        }, ctx.Object);

        mockDetector.Verify(d => d.Detect("hello"), Times.Once);
        Assert.Equal("ukr_Cyrl", response.DetectedLanguage);
        mockTranslator.Verify(
            t => t.TranslateAsync("hello", "ukr_Cyrl", "eng_Latn", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task TranslateText_DoesNotCallDetector_WhenSourceLanguageIsExplicit()
    {
        var mockTranslator = new Mock<ITextTranslator>();
        mockTranslator
            .Setup(t => t.TranslateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("translated");

        var mockDetector = new Mock<ILanguageDetector>();

        var svc = new TranslateGrpcService(SingleProviderManager("nllb", mockTranslator.Object), WithDetector(mockDetector.Object));
        var ctx = new Mock<ServerCallContext>(MockBehavior.Loose);

        var response = await svc.TranslateText(new TranslateTextRequest
        {
            Text = "hello", SourceLanguage = "eng_Latn", TargetLanguage = "ukr_Cyrl", Provider = "nllb",
        }, ctx.Object);

        mockDetector.Verify(d => d.Detect(It.IsAny<string>()), Times.Never);
        Assert.Equal(string.Empty, response.DetectedLanguage);
    }
}
