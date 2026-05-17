using Lopatnov.Translate.Core.Abstractions;
using Lopatnov.Translate.Grpc.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Lopatnov.Translate.Grpc.Tests;

/// <summary>
/// Unit tests for <see cref="WarmUpHostedService"/> covering the dispatch and
/// guard logic without requiring real ML models.
/// </summary>
public sealed class WarmUpHostedServiceTests
{
    private static ModelSessionManager EmptyManager() =>
        new(new Dictionary<string, Func<ITextTranslator>>(), [], TimeSpan.FromMinutes(1));

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
        string audioToText = "",
        Dictionary<string, string>? textToAudio = null)
    {
        var opts = Options.Create(new TranslationOptions
        {
            WarmUp    = warmUpModels,
            AudioToText = audioToText,
            TextToAudio = textToAudio ?? [],
        });

        return new WarmUpHostedService(
            config   ?? new ConfigurationBuilder().Build(),
            manager  ?? EmptyManager(),
            new Mock<ISpeechRecognizer>().Object,
            new Mock<ISpeechSynthesizer>().Object,
            opts,
            NullLogger<WarmUpHostedService>.Instance);
    }

    // ── Empty list ────────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_ReturnsImmediately_WhenWarmUpIsEmpty()
    {
        using var svc = Build([]);
        var ex = await Record.ExceptionAsync(
            () => svc.StartAsync(CancellationToken.None));
        Assert.Null(ex);
        await svc.StopAsync(CancellationToken.None);
    }

    // ── Model type not configured ─────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_SkipsModel_WhenTypeNotInConfig()
    {
        // Config has no entry for "unknown-model" → should log and skip without throwing.
        using var svc = Build(
            warmUpModels: ["unknown-model"],
            config: new ConfigurationBuilder().Build());

        var ex = await Record.ExceptionAsync(
            () => svc.StartAsync(CancellationToken.None));
        Assert.Null(ex);
        await svc.StopAsync(CancellationToken.None);
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

        var ex = await Record.ExceptionAsync(
            () => svc.StartAsync(CancellationToken.None));
        Assert.Null(ex);
        await svc.StopAsync(CancellationToken.None);
    }

    // ── NLLB / M2M100 / LibreTranslate → KeyNotFoundException caught ─────────

    [Theory]
    [InlineData("NLLB")]
    [InlineData("M2M100")]
    [InlineData("LibreTranslate")]
    public async Task ExecuteAsync_CatchesKeyNotFound_WhenTranslatorNotAllowed(string modelType)
    {
        // EmptyManager has no factories, so Get() will throw KeyNotFoundException.
        using var svc = Build(
            warmUpModels: ["model1"],
            config: ConfigWithModelType("model1", modelType),
            manager: EmptyManager());

        var ex = await Record.ExceptionAsync(
            () => svc.StartAsync(CancellationToken.None));
        Assert.Null(ex);
        await svc.StopAsync(CancellationToken.None);
    }

    // ── Whisper: name mismatch → skips ───────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_SkipsWhisper_WhenNameDiffersFromAudioToText()
    {
        // WarmUp asks for "whisper-large" but AudioToText is "whisper-small" → skip.
        using var svc = Build(
            warmUpModels: ["whisper-large"],
            config: ConfigWithModelType("whisper-large", "Whisper"),
            audioToText: "whisper-small");

        var ex = await Record.ExceptionAsync(
            () => svc.StartAsync(CancellationToken.None));
        Assert.Null(ex);
        await svc.StopAsync(CancellationToken.None);
    }

    // ── Piper: no language mapping → skips ───────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_SkipsPiper_WhenModelNotInTextToAudio()
    {
        // "piper-de-DE" is not referenced in TextToAudio → skip.
        using var svc = Build(
            warmUpModels: ["piper-de-DE"],
            config: ConfigWithModelType("piper-de-DE", "Piper"),
            textToAudio: new Dictionary<string, string> { ["en"] = "piper-en-US" });

        var ex = await Record.ExceptionAsync(
            () => svc.StartAsync(CancellationToken.None));
        Assert.Null(ex);
        await svc.StopAsync(CancellationToken.None);
    }

    // ── Cancellation ──────────────────────────────────────────────────────────

    [Fact]
    public async Task ExecuteAsync_RespectsPreCancelledToken()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        using var svc = Build(
            warmUpModels: ["model1"],
            config: ConfigWithModelType("model1", "NLLB"));

        var ex = await Record.ExceptionAsync(
            () => svc.StartAsync(cts.Token));
        Assert.Null(ex);
        await svc.StopAsync(CancellationToken.None);
    }
}
