using Grpc.Core;
using Lopatnov.Translate.Core.Abstractions;
using Lopatnov.Translate.Core.LanguageDetectors;
using Lopatnov.Translate.Grpc.Services;
using Microsoft.Extensions.Options;
using Moq;

namespace Lopatnov.Translate.Grpc.Tests;

public sealed class TranslateGrpcServiceTests
{
    private static readonly Lazy<ILanguageDetector> NoDetector = new(() => new Mock<ILanguageDetector>().Object);
    private static Lazy<ILanguageDetector> WithDetector(ILanguageDetector d) => new(() => d);

    private static readonly ISpeechRecognizer NoRecognizer = new Mock<ISpeechRecognizer>().Object;

    private static ModelSessionManager SingleProviderManager(string key, ITextTranslator translator)
        => new(
            new Dictionary<string, Func<ITextTranslator>> { [key] = () => translator },
            allowedModels: [],
            ttl: TimeSpan.FromMinutes(30));

    private static IOptions<TranslationOptions> TranslationOpts(string defaultModel = "nllb")
        => Options.Create(new TranslationOptions { DefaultModel = defaultModel });

    [Theory]
    [InlineData("nllb")]
    [InlineData("libretranslate")]
    public async Task TranslateText_DispatchesToCorrectProvider(string provider)
    {
        var mockTranslator = new Mock<ITextTranslator>();
        mockTranslator
            .Setup(t => t.TranslateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("translated");

        var svc = new TranslateGrpcService(SingleProviderManager(provider, mockTranslator.Object), NoDetector, NoRecognizer, TranslationOpts(provider));
        var ctx = new Mock<ServerCallContext>(MockBehavior.Loose);

        var response = await svc.TranslateText(new TranslateTextRequest
        {
            Text = "hello", SourceLanguage = "eng_Latn", TargetLanguage = "ukr_Cyrl",
            Model = provider, LanguageFormat = "flores200",
        }, ctx.Object);

        Assert.Equal("translated", response.TranslatedText);
        Assert.Equal(provider, response.ModelUsed);
        mockTranslator.Verify(
            t => t.TranslateAsync("hello", "eng_Latn", "ukr_Cyrl", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task TranslateText_DefaultsToConfiguredDefaultWhenProviderIsEmpty()
    {
        var mockTranslator = new Mock<ITextTranslator>();
        mockTranslator
            .Setup(t => t.TranslateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("translated");

        var svc = new TranslateGrpcService(SingleProviderManager("nllb", mockTranslator.Object), NoDetector, NoRecognizer, TranslationOpts("nllb"));
        var ctx = new Mock<ServerCallContext>(MockBehavior.Loose);

        var response = await svc.TranslateText(new TranslateTextRequest
        {
            Text = "hello", SourceLanguage = "eng_Latn", TargetLanguage = "ukr_Cyrl",
            LanguageFormat = "flores200",
        }, ctx.Object);

        Assert.Equal("nllb", response.ModelUsed);
        mockTranslator.Verify(
            t => t.TranslateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task TranslateText_ThrowsInvalidArgument_ForUnknownProvider()
    {
        var svc = new TranslateGrpcService(SingleProviderManager("nllb", new Mock<ITextTranslator>().Object), NoDetector, NoRecognizer, TranslationOpts());
        var ctx = new Mock<ServerCallContext>(MockBehavior.Loose);

        var ex = await Assert.ThrowsAsync<RpcException>(() =>
            svc.TranslateText(new TranslateTextRequest { Model = "unknown" }, ctx.Object));

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
            allowedModels: ["nllb"],
            ttl: TimeSpan.FromMinutes(30));

        var svc = new TranslateGrpcService(manager, NoDetector, NoRecognizer, TranslationOpts());
        var ctx = new Mock<ServerCallContext>(MockBehavior.Loose);

        var ex = await Assert.ThrowsAsync<RpcException>(() =>
            svc.TranslateText(new TranslateTextRequest
            {
                Text = "hello", SourceLanguage = "eng_Latn", TargetLanguage = "ukr_Cyrl",
                Model = "m2m100", LanguageFormat = "flores200",
            }, ctx.Object));

        Assert.Equal(StatusCode.PermissionDenied, ex.StatusCode);
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
        mockDetector.Setup(d => d.Detect("hello"))
            .Returns(new LanguageDetectionResult("ukr_Cyrl", LanguageCodeFormat.Flores200));

        var svc = new TranslateGrpcService(SingleProviderManager("nllb", mockTranslator.Object), WithDetector(mockDetector.Object), NoRecognizer, TranslationOpts());
        var ctx = new Mock<ServerCallContext>(MockBehavior.Loose);

        // LanguageFormat = "bcp47" → detected_language in response is BCP-47.
        var response = await svc.TranslateText(new TranslateTextRequest
        {
            Text = "hello", SourceLanguage = sourceLanguage, TargetLanguage = "eng_Latn",
            Model = "nllb", LanguageFormat = "bcp47",
        }, ctx.Object);

        mockDetector.Verify(d => d.Detect("hello"), Times.Once);
        Assert.Equal("uk", response.DetectedLanguage);
        mockTranslator.Verify(
            t => t.TranslateAsync("hello", "ukr_Cyrl", "eng_Latn", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task TranslateText_CallsDetector_Flores200Format_PreservesFloresInResponse()
    {
        var mockTranslator = new Mock<ITextTranslator>();
        mockTranslator
            .Setup(t => t.TranslateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("translated");

        var mockDetector = new Mock<ILanguageDetector>();
        mockDetector.Setup(d => d.Detect("hello"))
            .Returns(new LanguageDetectionResult("ukr_Cyrl", LanguageCodeFormat.Flores200));

        var svc = new TranslateGrpcService(SingleProviderManager("nllb", mockTranslator.Object), WithDetector(mockDetector.Object), NoRecognizer, TranslationOpts());
        var ctx = new Mock<ServerCallContext>(MockBehavior.Loose);

        var response = await svc.TranslateText(new TranslateTextRequest
        {
            Text = "hello", SourceLanguage = "auto", TargetLanguage = "eng_Latn",
            Model = "nllb", LanguageFormat = "flores200",
        }, ctx.Object);

        Assert.Equal("ukr_Cyrl", response.DetectedLanguage);
    }

    [Fact]
    public async Task TranslateText_ConvertsBcp47InputToFlores200ForTranslator()
    {
        var mockTranslator = new Mock<ITextTranslator>();
        mockTranslator
            .Setup(t => t.TranslateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("Привіт");

        var svc = new TranslateGrpcService(SingleProviderManager("nllb", mockTranslator.Object), NoDetector, NoRecognizer, TranslationOpts());
        var ctx = new Mock<ServerCallContext>(MockBehavior.Loose);

        await svc.TranslateText(new TranslateTextRequest
        {
            Text = "Hello", SourceLanguage = "en", TargetLanguage = "uk",
            Model = "nllb", LanguageFormat = "bcp47",
        }, ctx.Object);

        mockTranslator.Verify(
            t => t.TranslateAsync("Hello", "eng_Latn", "ukr_Cyrl", It.IsAny<CancellationToken>()),
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

        var svc = new TranslateGrpcService(SingleProviderManager("nllb", mockTranslator.Object), WithDetector(mockDetector.Object), NoRecognizer, TranslationOpts());
        var ctx = new Mock<ServerCallContext>(MockBehavior.Loose);

        var response = await svc.TranslateText(new TranslateTextRequest
        {
            Text = "hello", SourceLanguage = "eng_Latn", TargetLanguage = "ukr_Cyrl",
            Model = "nllb", LanguageFormat = "flores200",
        }, ctx.Object);

        mockDetector.Verify(d => d.Detect(It.IsAny<string>()), Times.Never);
        Assert.Equal(string.Empty, response.DetectedLanguage);
    }

    [Fact]
    public async Task DetectLanguage_ReturnsBcp47_ByDefault()
    {
        var mockDetector = new Mock<ILanguageDetector>();
        mockDetector.Setup(d => d.Detect("Привіт"))
            .Returns(new LanguageDetectionResult("ukr_Cyrl", LanguageCodeFormat.Flores200));

        var svc = new TranslateGrpcService(
            SingleProviderManager("nllb", new Mock<ITextTranslator>().Object),
            WithDetector(mockDetector.Object),
            NoRecognizer,
            TranslationOpts());
        var ctx = new Mock<ServerCallContext>(MockBehavior.Loose);

        var response = await svc.DetectLanguage(new DetectLanguageRequest { Text = "Привіт" }, ctx.Object);

        Assert.Equal("uk", response.Language);
    }

    [Fact]
    public async Task DetectLanguage_ReturnsFlores200_WhenRequested()
    {
        var mockDetector = new Mock<ILanguageDetector>();
        mockDetector.Setup(d => d.Detect("Привіт"))
            .Returns(new LanguageDetectionResult("ukr_Cyrl", LanguageCodeFormat.Flores200));

        var svc = new TranslateGrpcService(
            SingleProviderManager("nllb", new Mock<ITextTranslator>().Object),
            WithDetector(mockDetector.Object),
            NoRecognizer,
            TranslationOpts());
        var ctx = new Mock<ServerCallContext>(MockBehavior.Loose);

        var response = await svc.DetectLanguage(
            new DetectLanguageRequest { Text = "Привіт", LanguageFormat = "flores200" },
            ctx.Object);

        Assert.Equal("ukr_Cyrl", response.Language);
    }
}
