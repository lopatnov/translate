using Lopatnov.Translate.Core.Abstractions;
using Lopatnov.Translate.Core.Models;
using Lopatnov.Translate.Grpc.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Lopatnov.Translate.Grpc.Tests;

/// <summary>
/// Unit tests for <see cref="WarmUpHostedService"/> covering the dispatch and
/// guard logic without requiring real ML models.
///
/// IMPORTANT: After StartAsync, always await <c>svc.ExecuteTask</c> (available
/// since .NET 8) to ensure ExecuteAsync completes before assertions — without it
/// the background task races against the test and coverage is not captured.
/// </summary>
public sealed class WarmUpHostedServiceTests
{
    private static ModelSessionManager EmptyManager() =>
        new(new Dictionary<string, Func<ITextTranslator>>(), [], TimeSpan.FromMinutes(1));

    private static ModelSessionManager ManagerWith(string key, ITextTranslator translator) =>
        new(new Dictionary<string, Func<ITextTranslator>> { [key] = () => translator },
            allowedModels: [],
            ttl: TimeSpan.FromMinutes(1));

    private static IConfiguration ConfigWithModelType(string modelName, string modelType) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"Models:{modelName}:Type"] = modelType,
            })
            .Build();

    private static WarmUpHostedService Build(
        string[] warmUpModels,
        IConfiguration? config = null,
        ModelSessionManager? manager = null,
        ISpeechRecognizer? recognizer = null,
        ISpeechSynthesizer? synthesizer = null,
        string audioToText = "",
        Dictionary<string, string>? textToAudio = null)
    {
        var opts = Options.Create(new TranslationOptions
        {
            WarmUp      = warmUpModels,
            AudioToText = audioToText,
            TextToAudio = textToAudio ?? [],
        });

        return new WarmUpHostedService(
            config     ?? new ConfigurationBuilder().Build(),
            manager    ?? EmptyManager(),
            recognizer ?? new Mock<ISpeechRecognizer>().Object,
            synthesizer ?? new Mock<ISpeechSynthesizer>().Object,
            opts,
            NullLogger<WarmUpHostedService>.Instance);
    }

    /// Starts the service, waits for ExecuteAsync to complete, then stops cleanly.
    private static async Task RunAsync(WarmUpHostedService svc,
        CancellationToken ct = default)
    {
        await svc.StartAsync(ct);
        if (svc.ExecuteTask is not null)
            await svc.ExecuteTask;
        await svc.StopAsync(CancellationToken.None);
    }

    // ── Empty list ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_ReturnsImmediately_WhenWarmUpIsEmpty()
    {
        using var svc = Build([]);
        var ex = await Record.ExceptionAsync(() => RunAsync(svc, TestContext.Current.CancellationToken));
        Assert.Null(ex);
    }

    // ── Model type not configured ─────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_SkipsModel_WhenTypeNotInConfig()
    {
        using var svc = Build(
            warmUpModels: ["unknown-model"],
            config: new ConfigurationBuilder().Build());

        var ex = await Record.ExceptionAsync(() => RunAsync(svc, TestContext.Current.CancellationToken));
        Assert.Null(ex);
    }

    // ── FastText / unrecognised types → no-op dispatch ───────────────────────

    [Theory]
    [InlineData("FastText")]
    [InlineData("Redirect")]
    [InlineData("Unknown")]
    public async Task ExecuteAsync_SkipsModel_WhenTypeHasNoWarmUp(string modelType)
    {
        using var svc = Build(
            warmUpModels: ["model1"],
            config: ConfigWithModelType("model1", modelType));

        var ex = await Record.ExceptionAsync(() => RunAsync(svc, TestContext.Current.CancellationToken));
        Assert.Null(ex);
    }

    // ── NLLB / M2M100 / LibreTranslate → KeyNotFoundException caught ──────────

    [Theory]
    [InlineData("NLLB")]
    [InlineData("M2M100")]
    [InlineData("LibreTranslate")]
    public async Task ExecuteAsync_CatchesKeyNotFound_WhenTranslatorNotInFactory(string modelType)
    {
        using var svc = Build(
            warmUpModels: ["model1"],
            config: ConfigWithModelType("model1", modelType),
            manager: EmptyManager());

        var ex = await Record.ExceptionAsync(() => RunAsync(svc, TestContext.Current.CancellationToken));
        Assert.Null(ex);
    }

    // ── NLLB warm-up success path ─────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_CallsTranslateAsync_WhenTranslatorIsAvailable()
    {
        var mockTranslator = new Mock<ITextTranslator>();
        mockTranslator
            .Setup(t => t.TranslateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("bonjour");

        using var svc = Build(
            warmUpModels: ["nllb"],
            config: ConfigWithModelType("nllb", "NLLB"),
            manager: ManagerWith("nllb", mockTranslator.Object));

        await RunAsync(svc, TestContext.Current.CancellationToken);

        mockTranslator.Verify(
            t => t.TranslateAsync(" ", "eng_Latn", "fra_Latn", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ── Whisper: name mismatch → skips ───────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_SkipsWhisper_WhenNameDiffersFromAudioToText()
    {
        using var svc = Build(
            warmUpModels: ["whisper-large"],
            config: ConfigWithModelType("whisper-large", "Whisper"),
            audioToText: "whisper-small");

        var ex = await Record.ExceptionAsync(() => RunAsync(svc, TestContext.Current.CancellationToken));
        Assert.Null(ex);
    }

    // ── Whisper warm-up success path ──────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_CallsTranscribeAsync_WhenWhisperIsActiveModel()
    {
        var mockRecognizer = new Mock<ISpeechRecognizer>();
        mockRecognizer
            .Setup(r => r.TranscribeAsync(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new TranscriptionResult([], string.Empty, string.Empty));

        using var svc = Build(
            warmUpModels: ["whisper-small"],
            config: ConfigWithModelType("whisper-small", "Whisper"),
            recognizer: mockRecognizer.Object,
            audioToText: "whisper-small");

        await RunAsync(svc, TestContext.Current.CancellationToken);

        mockRecognizer.Verify(
            r => r.TranscribeAsync(It.IsAny<byte[]>(), "auto", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ── Piper: no language mapping → skips ───────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_SkipsPiper_WhenModelNotInTextToAudio()
    {
        using var svc = Build(
            warmUpModels: ["piper-de-DE"],
            config: ConfigWithModelType("piper-de-DE", "Piper"),
            textToAudio: new Dictionary<string, string> { ["en"] = "piper-en-US" });

        var ex = await Record.ExceptionAsync(() => RunAsync(svc, TestContext.Current.CancellationToken));
        Assert.Null(ex);
    }

    // ── Piper warm-up success path ────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_CallsSynthesizeAsync_WhenPiperLanguageIsMapped()
    {
        var mockSynth = new Mock<ISpeechSynthesizer>();
        mockSynth
            .Setup(s => s.SynthesizeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<float>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SynthesisResult([], 22050));

        using var svc = Build(
            warmUpModels: ["piper-en"],
            config: ConfigWithModelType("piper-en", "Piper"),
            synthesizer: mockSynth.Object,
            textToAudio: new Dictionary<string, string> { ["en"] = "piper-en" });

        await RunAsync(svc, TestContext.Current.CancellationToken);

        mockSynth.Verify(
            s => s.SynthesizeAsync(" ", "en", It.IsAny<string>(), It.IsAny<float>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ── NLLB warm-up UnauthorizedAccess path ─────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_SkipsModel_WhenTranslatorNotInAllowedList()
    {
        // ModelSessionManager.Get() throws UnauthorizedAccessException when a model is in
        // _configured (factories) but NOT in allowedModels — covered by the catch at line 126.
        var mockTranslator = new Mock<ITextTranslator>();
        var manager = new ModelSessionManager(
            new Dictionary<string, Func<ITextTranslator>>
            {
                ["nllb"] = () => mockTranslator.Object,
            },
            allowedModels: ["other-model"], // "nllb" is configured but not allowed
            ttl: TimeSpan.FromMinutes(1));

        using var svc = Build(
            warmUpModels: ["nllb"],
            config: ConfigWithModelType("nllb", "NLLB"),
            manager: manager);

        var ex = await Record.ExceptionAsync(() => RunAsync(svc, TestContext.Current.CancellationToken));
        Assert.Null(ex);
    }

    // ── WarmUpOneAsync outer catch path ───────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_LogsWarningAndContinues_WhenWarmUpThrowsUnexpected()
    {
        // If TranslateAsync throws something other than KeyNotFoundException /
        // UnauthorizedAccessException, it propagates up to WarmUpOneAsync which
        // catches all non-cancellation exceptions and logs a warning.
        var mockTranslator = new Mock<ITextTranslator>();
        mockTranslator
            .Setup(t => t.TranslateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Model failed to load unexpectedly"));

        using var svc = Build(
            warmUpModels: ["nllb"],
            config: ConfigWithModelType("nllb", "NLLB"),
            manager: ManagerWith("nllb", mockTranslator.Object));

        // Must NOT rethrow — the outer catch logs and moves on.
        var ex = await Record.ExceptionAsync(() => RunAsync(svc, TestContext.Current.CancellationToken));
        Assert.Null(ex);
    }

    // ── Cancellation ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_ExitsGracefully_WhenStoppedExternally()
    {
        // Start normally, then stop before the loop has a chance to proceed past
        // the first IsCancellationRequested check.
        using var svc = Build(
            warmUpModels: ["model1"],
            config: ConfigWithModelType("model1", "FastText")); // FastText = no-op dispatch

        await svc.StartAsync(CancellationToken.None);
        if (svc.ExecuteTask is not null)
        {
            try { await svc.ExecuteTask; }
            catch (OperationCanceledException) { /* expected when service is stopped externally */ }
        }
        await svc.StopAsync(CancellationToken.None);

        // After stopping, the task must be in a terminal state (not still running).
        Assert.True(svc.ExecuteTask is null or { IsCompleted: true });
    }
}
