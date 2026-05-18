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
    private static readonly ISpeechSynthesizer NoSynthesizer = new Mock<ISpeechSynthesizer>().Object;

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

        var svc = new TranslateGrpcService(SingleProviderManager(provider, mockTranslator.Object), NoDetector, NoRecognizer, NoSynthesizer, TranslationOpts(provider));
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

        var svc = new TranslateGrpcService(SingleProviderManager("nllb", mockTranslator.Object), NoDetector, NoRecognizer, NoSynthesizer, TranslationOpts("nllb"));
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
        var svc = new TranslateGrpcService(SingleProviderManager("nllb", new Mock<ITextTranslator>().Object), NoDetector, NoRecognizer, NoSynthesizer, TranslationOpts());
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

        var svc = new TranslateGrpcService(manager, NoDetector, NoRecognizer, NoSynthesizer, TranslationOpts());
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

        var svc = new TranslateGrpcService(SingleProviderManager("nllb", mockTranslator.Object), WithDetector(mockDetector.Object), NoRecognizer, NoSynthesizer, TranslationOpts());
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

        var svc = new TranslateGrpcService(SingleProviderManager("nllb", mockTranslator.Object), WithDetector(mockDetector.Object), NoRecognizer, NoSynthesizer, TranslationOpts());
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

        var svc = new TranslateGrpcService(SingleProviderManager("nllb", mockTranslator.Object), NoDetector, NoRecognizer, NoSynthesizer, TranslationOpts());
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

        var svc = new TranslateGrpcService(SingleProviderManager("nllb", mockTranslator.Object), WithDetector(mockDetector.Object), NoRecognizer, NoSynthesizer, TranslationOpts());
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
            NoSynthesizer,
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
            NoSynthesizer,
            TranslationOpts());
        var ctx = new Mock<ServerCallContext>(MockBehavior.Loose);

        var response = await svc.DetectLanguage(
            new DetectLanguageRequest { Text = "Привіт", LanguageFormat = "flores200" },
            ctx.Object);

        Assert.Equal("ukr_Cyrl", response.Language);
    }

    // ── TranscribeAudio ───────────────────────────────────────────────────────

    private static TranslateGrpcService SvcWithRecognizer(ISpeechRecognizer recognizer)
        => new(EmptyManager(), NoDetector,
               recognizer, NoSynthesizer,
               TranslationOpts());

    private static ModelSessionManager EmptyManager() =>
        new(new Dictionary<string, Func<ITextTranslator>>(), [], TimeSpan.FromMinutes(1));

    [Fact]
    public async Task TranscribeAudio_ReturnsFullText_OnSuccess()
    {
        var mockRecognizer = new Mock<ISpeechRecognizer>();
        mockRecognizer
            .Setup(r => r.TranscribeAsync(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Core.Models.TranscriptionResult(
                Segments: [new Core.Models.TranscriptionSegment("hello", 0f, 1f)],
                DetectedLanguage: "en",
                FullText: "hello"));

        var svc = SvcWithRecognizer(mockRecognizer.Object);
        var ctx = new Mock<ServerCallContext>(MockBehavior.Loose);

        var response = await svc.TranscribeAudio(new TranscribeAudioRequest
        {
            AudioData    = Google.Protobuf.ByteString.Empty,
            Language     = "en",
            LanguageFormat = "bcp47",
        }, ctx.Object);

        Assert.Equal("hello", response.FullText);
        Assert.Single(response.Segments);
        Assert.Equal("hello", response.Segments[0].Text);
    }

    [Fact]
    public async Task TranscribeAudio_ReturnsAutoLanguage_WhenLanguageIsEmpty()
    {
        var mockRecognizer = new Mock<ISpeechRecognizer>();
        mockRecognizer
            .Setup(r => r.TranscribeAsync(It.IsAny<byte[]>(), "auto", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Core.Models.TranscriptionResult([], "en", "hi"));

        var svc = SvcWithRecognizer(mockRecognizer.Object);
        var ctx = new Mock<ServerCallContext>(MockBehavior.Loose);

        var response = await svc.TranscribeAudio(new TranscribeAudioRequest
        {
            AudioData = Google.Protobuf.ByteString.Empty,
        }, ctx.Object);

        Assert.Equal("hi", response.FullText);
        mockRecognizer.Verify(r => r.TranscribeAsync(It.IsAny<byte[]>(), "auto", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task TranscribeAudio_ThrowsFailedPrecondition_WhenRecognizerNotSupported()
    {
        var mockRecognizer = new Mock<ISpeechRecognizer>();
        mockRecognizer
            .Setup(r => r.TranscribeAsync(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new NotSupportedException("STT not configured"));

        var svc = SvcWithRecognizer(mockRecognizer.Object);
        var ctx = new Mock<ServerCallContext>(MockBehavior.Loose);

        var ex = await Assert.ThrowsAsync<RpcException>(() =>
            svc.TranscribeAudio(new TranscribeAudioRequest { AudioData = Google.Protobuf.ByteString.Empty }, ctx.Object));

        Assert.Equal(StatusCode.FailedPrecondition, ex.StatusCode);
    }

    // ── SynthesizeSpeech ──────────────────────────────────────────────────────

    private static TranslateGrpcService SvcWithSynthesizer(ISpeechSynthesizer synthesizer,
        string audioToText = "", Dictionary<string, string>? textToAudio = null)
        => new(EmptyManager(), NoDetector, NoRecognizer, synthesizer,
               Options.Create(new TranslationOptions
               {
                   DefaultModel = "nllb",
                   AudioToText  = audioToText,
                   TextToAudio  = textToAudio ?? [],
               }));

    [Fact]
    public async Task SynthesizeSpeech_ReturnsAudio_OnSuccess()
    {
        var audio = new byte[] { 0x52, 0x49, 0x46, 0x46 }; // "RIFF"
        var mockSynth = new Mock<ISpeechSynthesizer>();
        mockSynth
            .Setup(s => s.SynthesizeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<float>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Core.Models.SynthesisResult(audio, 22050));

        var svc = SvcWithSynthesizer(mockSynth.Object);
        var ctx = new Mock<ServerCallContext>(MockBehavior.Loose);

        var response = await svc.SynthesizeSpeech(new SynthesizeSpeechRequest
        {
            Text = "hello", Language = "en", LanguageFormat = "bcp47",
        }, ctx.Object);

        Assert.Equal(22050, response.SampleRate);
        Assert.Equal(audio, response.AudioData.ToByteArray());
    }

    [Fact]
    public async Task SynthesizeSpeech_ThrowsFailedPrecondition_WhenNotSupported()
    {
        var mockSynth = new Mock<ISpeechSynthesizer>();
        mockSynth
            .Setup(s => s.SynthesizeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<float>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new NotSupportedException("TTS not configured"));

        var svc = SvcWithSynthesizer(mockSynth.Object);
        var ctx = new Mock<ServerCallContext>(MockBehavior.Loose);

        var ex = await Assert.ThrowsAsync<RpcException>(() =>
            svc.SynthesizeSpeech(new SynthesizeSpeechRequest { Text = "hi", Language = "en", LanguageFormat = "bcp47" }, ctx.Object));

        Assert.Equal(StatusCode.FailedPrecondition, ex.StatusCode);
    }

    [Fact]
    public async Task SynthesizeSpeech_ThrowsInternal_WhenInvalidOperation()
    {
        var mockSynth = new Mock<ISpeechSynthesizer>();
        mockSynth
            .Setup(s => s.SynthesizeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<float>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("espeak-ng not found"));

        var svc = SvcWithSynthesizer(mockSynth.Object);
        var ctx = new Mock<ServerCallContext>(MockBehavior.Loose);

        var ex = await Assert.ThrowsAsync<RpcException>(() =>
            svc.SynthesizeSpeech(new SynthesizeSpeechRequest { Text = "hi", Language = "en", LanguageFormat = "bcp47" }, ctx.Object));

        Assert.Equal(StatusCode.Internal, ex.StatusCode);
    }

    [Fact]
    public async Task SynthesizeSpeech_ThrowsFailedPrecondition_WhenFileNotFound()
    {
        var mockSynth = new Mock<ISpeechSynthesizer>();
        mockSynth
            .Setup(s => s.SynthesizeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<float>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new FileNotFoundException("model.onnx not found"));

        var svc = SvcWithSynthesizer(mockSynth.Object);
        var ctx = new Mock<ServerCallContext>(MockBehavior.Loose);

        var ex = await Assert.ThrowsAsync<RpcException>(() =>
            svc.SynthesizeSpeech(new SynthesizeSpeechRequest { Text = "hi", Language = "en", LanguageFormat = "bcp47" }, ctx.Object));

        Assert.Equal(StatusCode.FailedPrecondition, ex.StatusCode);
    }

    // ── TranslateAudio ────────────────────────────────────────────────────────

    private static TranslateGrpcService SvcForAudioTranslation(
        ISpeechRecognizer recognizer, ISpeechSynthesizer synthesizer, ITextTranslator translator)
        => new(SingleProviderManager("default", translator), NoDetector,
               recognizer, synthesizer,
               Options.Create(new TranslationOptions { DefaultModel = "default" }));

    [Fact]
    public async Task TranslateAudio_ReturnsEmpty_WhenTranscriptionIsEmpty()
    {
        var mockRecognizer = new Mock<ISpeechRecognizer>();
        mockRecognizer
            .Setup(r => r.TranscribeAsync(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Core.Models.TranscriptionResult([], string.Empty, string.Empty));

        var svc = SvcForAudioTranslation(mockRecognizer.Object,
            new Mock<ISpeechSynthesizer>().Object, new Mock<ITextTranslator>().Object);
        var ctx = new Mock<ServerCallContext>(MockBehavior.Loose);

        var response = await svc.TranslateAudio(new TranslateAudioRequest
        {
            AudioData = Google.Protobuf.ByteString.Empty,
            TargetLanguage = "uk", LanguageFormat = "bcp47",
        }, ctx.Object);

        Assert.Equal(string.Empty, response.Transcription);
        Assert.Equal(string.Empty, response.TranslatedText);
        Assert.Equal(0, response.SampleRate);
    }

    [Fact]
    public async Task TranslateAudio_ThrowsFailedPrecondition_WhenRecognizerNotSupported()
    {
        var mockRecognizer = new Mock<ISpeechRecognizer>();
        mockRecognizer
            .Setup(r => r.TranscribeAsync(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new NotSupportedException("STT not configured"));

        var svc = SvcForAudioTranslation(mockRecognizer.Object,
            new Mock<ISpeechSynthesizer>().Object, new Mock<ITextTranslator>().Object);
        var ctx = new Mock<ServerCallContext>(MockBehavior.Loose);

        var ex = await Assert.ThrowsAsync<RpcException>(() =>
            svc.TranslateAudio(new TranslateAudioRequest
            {
                AudioData = Google.Protobuf.ByteString.Empty,
                TargetLanguage = "uk", LanguageFormat = "bcp47",
            }, ctx.Object));

        Assert.Equal(StatusCode.FailedPrecondition, ex.StatusCode);
    }

    [Fact]
    public async Task TranslateAudio_ThrowsFailedPrecondition_WhenNoSourceLanguageDetected()
    {
        // Whisper returns empty DetectedLanguage AND no source_language was specified.
        var mockRecognizer = new Mock<ISpeechRecognizer>();
        mockRecognizer
            .Setup(r => r.TranscribeAsync(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Core.Models.TranscriptionResult([], DetectedLanguage: string.Empty, FullText: "hello"));

        var svc = SvcForAudioTranslation(mockRecognizer.Object,
            new Mock<ISpeechSynthesizer>().Object, new Mock<ITextTranslator>().Object);
        var ctx = new Mock<ServerCallContext>(MockBehavior.Loose);

        var ex = await Assert.ThrowsAsync<RpcException>(() =>
            svc.TranslateAudio(new TranslateAudioRequest
            {
                AudioData      = Google.Protobuf.ByteString.Empty,
                SourceLanguage = "auto",
                TargetLanguage = "uk",
                LanguageFormat = "bcp47",
            }, ctx.Object));

        Assert.Equal(StatusCode.FailedPrecondition, ex.StatusCode);
    }

    [Fact]
    public async Task TranslateAudio_FullPipeline_WhenDetectedLanguageIsSet()
    {
        var mockRecognizer = new Mock<ISpeechRecognizer>();
        mockRecognizer
            .Setup(r => r.TranscribeAsync(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Core.Models.TranscriptionResult([], DetectedLanguage: "en", FullText: "hello world"));

        var mockTranslator = new Mock<ITextTranslator>();
        mockTranslator
            .Setup(t => t.TranslateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("привіт");

        var mockSynth = new Mock<ISpeechSynthesizer>();
        mockSynth
            .Setup(s => s.SynthesizeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<float>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Core.Models.SynthesisResult(new byte[] { 1, 2, 3 }, 22050));

        var svc = SvcForAudioTranslation(mockRecognizer.Object, mockSynth.Object, mockTranslator.Object);
        var ctx = new Mock<ServerCallContext>(MockBehavior.Loose);

        var response = await svc.TranslateAudio(new TranslateAudioRequest
        {
            AudioData      = Google.Protobuf.ByteString.Empty,
            SourceLanguage = "auto",
            TargetLanguage = "uk",
            LanguageFormat = "bcp47",
        }, ctx.Object);

        Assert.Equal("hello world", response.Transcription);
        Assert.Equal("привіт", response.TranslatedText);
        Assert.Equal(22050, response.SampleRate);
        mockTranslator.Verify(
            t => t.TranslateAsync("hello world", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task TranslateAudio_UsesExplicitSourceLanguage_WhenWhisperDetectsNothing()
    {
        // Whisper returns empty DetectedLanguage, but caller provided explicit source_language.
        var mockRecognizer = new Mock<ISpeechRecognizer>();
        mockRecognizer
            .Setup(r => r.TranscribeAsync(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Core.Models.TranscriptionResult([], DetectedLanguage: string.Empty, FullText: "hello"));

        var mockTranslator = new Mock<ITextTranslator>();
        mockTranslator
            .Setup(t => t.TranslateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("bonjour");

        var mockSynth = new Mock<ISpeechSynthesizer>();
        mockSynth
            .Setup(s => s.SynthesizeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<float>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Core.Models.SynthesisResult([], 22050));

        var svc = SvcForAudioTranslation(mockRecognizer.Object, mockSynth.Object, mockTranslator.Object);
        var ctx = new Mock<ServerCallContext>(MockBehavior.Loose);

        var response = await svc.TranslateAudio(new TranslateAudioRequest
        {
            AudioData      = Google.Protobuf.ByteString.Empty,
            SourceLanguage = "en",    // explicit, not "auto"
            TargetLanguage = "fr",
            LanguageFormat = "bcp47",
        }, ctx.Object);

        Assert.Equal("hello", response.Transcription);
        Assert.Equal("bonjour", response.TranslatedText);
    }
}
