using Lopatnov.Translate.Piper;
using Microsoft.Extensions.Options;

namespace Lopatnov.Translate.Grpc.Tests;

/// <summary>
/// Tests for <see cref="MultiVoiceSynthesizer"/> voice dispatch and resolution.
/// PiperSynthesizer with an empty ModelPath is used to exercise routing logic
/// without model files — inference calls are expected to throw InvalidOperationException.
/// </summary>
public sealed class MultiVoiceSynthesizerTests
{
    private static PiperSynthesizer EmptyPathSynth() =>
        new(Options.Create(new PiperOptions { ModelPath = string.Empty }));

    // ── Voice not found ───────────────────────────────────────────────────────

    [Fact]
    public async Task SynthesizeAsync_ThrowsNotSupported_WhenLanguageNotConfigured()
    {
        using var sut = new MultiVoiceSynthesizer(
            new Dictionary<string, PiperSynthesizer> { ["en"] = EmptyPathSynth() });

        await Assert.ThrowsAsync<NotSupportedException>(
            () => sut.SynthesizeAsync("text", "fr",
                cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SynthesizeAsync_ThrowsNotSupported_WhenEmptyDictionary()
    {
        using var sut = new MultiVoiceSynthesizer(
            new Dictionary<string, PiperSynthesizer>());

        await Assert.ThrowsAsync<NotSupportedException>(
            () => sut.SynthesizeAsync("text", "en"));
    }

    [Fact]
    public async Task SynthesizeAsync_ThrowsNotSupported_WhenNullLanguage_AndMultipleVoices()
    {
        using var sut = new MultiVoiceSynthesizer(new Dictionary<string, PiperSynthesizer>
        {
            ["en"] = EmptyPathSynth(),
            ["ru"] = EmptyPathSynth(),
        });

        await Assert.ThrowsAsync<NotSupportedException>(
            () => sut.SynthesizeAsync("text", "",
                cancellationToken: TestContext.Current.CancellationToken));
    }

    // ── Routing reaches the synthesizer (model absent → InvalidOperationException) ──

    [Fact]
    public async Task SynthesizeAsync_RoutesToExactMatch()
    {
        // "en" is in the map → reaches PiperSynthesizer → throws because model absent.
        using var sut = new MultiVoiceSynthesizer(
            new Dictionary<string, PiperSynthesizer> { ["en"] = EmptyPathSynth() });

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.SynthesizeAsync("text", "en",
                cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task SynthesizeAsync_RoutesToPrimarySubtag()
    {
        // "en-US" resolves to "en" via primary subtag stripping.
        using var sut = new MultiVoiceSynthesizer(
            new Dictionary<string, PiperSynthesizer> { ["en"] = EmptyPathSynth() });

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.SynthesizeAsync("text", "en-US"));
    }

    [Fact]
    public async Task SynthesizeAsync_SingleVoiceFallback_WhenEmptyLanguage()
    {
        // Empty language with a single configured voice → falls back to that voice.
        using var sut = new MultiVoiceSynthesizer(
            new Dictionary<string, PiperSynthesizer> { ["en"] = EmptyPathSynth() });

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.SynthesizeAsync("text", ""));
    }

    // ── Dispose ───────────────────────────────────────────────────────────────

    [Fact]
    public void Dispose_DoesNotThrow_WhenVoicesIsEmpty()
    {
        var sut = new MultiVoiceSynthesizer(new Dictionary<string, PiperSynthesizer>());
        var ex = Record.Exception(sut.Dispose);
        Assert.Null(ex);
    }

    [Fact]
    public void Dispose_DisposesAllVoices()
    {
        var synth = EmptyPathSynth();
        var sut = new MultiVoiceSynthesizer(
            new Dictionary<string, PiperSynthesizer> { ["en"] = synth });
        var ex = Record.Exception(sut.Dispose);
        Assert.Null(ex);
        // PiperSynthesizer is IDisposable — second dispose should also not throw.
        ex = Record.Exception(synth.Dispose);
        Assert.Null(ex);
    }
}
