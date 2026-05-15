using Lopatnov.Translate.Piper;
using Microsoft.Extensions.Options;

namespace Lopatnov.Translate.Piper.Tests;

/// <summary>
/// Unit tests for PiperSynthesizer that do NOT require a model file or espeak-ng.
/// </summary>
public sealed class PiperSynthesizerTests
{
    // -------------------------------------------------------------------------
    // Guard tests (no model or espeak needed)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SynthesizeAsync_ThrowsInvalidOperation_WhenModelPathIsEmpty()
    {
        var options = Options.Create(new PiperOptions { ModelPath = string.Empty });
        using var sut = new PiperSynthesizer(options);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.SynthesizeAsync("hello", "en"));
    }

    [Fact]
    public async Task SynthesizeAsync_ThrowsObjectDisposed_AfterDispose()
    {
        var options = Options.Create(new PiperOptions { ModelPath = "dummy.onnx" });
        var sut = new PiperSynthesizer(options);
        sut.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => sut.SynthesizeAsync("hello", "en"));
    }

    // -------------------------------------------------------------------------
    // BuildPhonemeIds
    // -------------------------------------------------------------------------

    [Fact]
    public void BuildPhonemeIds_AlwaysStartsWithBosAndEndsWithEos()
    {
        var map = new Dictionary<string, long[]>
        {
            ["h"] = [20L],
            ["i"] = [21L],
        };

        var ids = PiperSynthesizer.BuildPhonemeIds("hi", map);

        Assert.Equal(0L, ids[0]);                  // BOS "_"
        Assert.Equal(2L, ids[^1]);                 // EOS "$"
    }

    [Fact]
    public void BuildPhonemeIds_EmptyInput_ReturnsBosEosOnly()
    {
        var map = new Dictionary<string, long[]>();
        var ids = PiperSynthesizer.BuildPhonemeIds(string.Empty, map);

        Assert.Equal(2, ids.Length);
        Assert.Equal(0L, ids[0]); // BOS
        Assert.Equal(2L, ids[1]); // EOS
    }

    [Fact]
    public void BuildPhonemeIds_UnknownPhonemes_AreSkipped()
    {
        // Map with only one known key
        var map = new Dictionary<string, long[]> { ["a"] = [14L] };

        // "abc" — only "a" is known
        var ids = PiperSynthesizer.BuildPhonemeIds("abc", map);

        // Expected: [BOS=0, 14 (a), PAD=3, EOS=2]
        Assert.Equal(new long[] { 0L, 14L, 3L, 2L }, ids);
    }

    [Fact]
    public void BuildPhonemeIds_AddsWordBreakAfterEachPhoneme()
    {
        var map = new Dictionary<string, long[]>
        {
            ["a"] = [14L],
            ["b"] = [15L],
        };

        var ids = PiperSynthesizer.BuildPhonemeIds("ab", map);

        // BOS, a, PAD, b, PAD, EOS
        Assert.Equal(new long[] { 0L, 14L, 3L, 15L, 3L, 2L }, ids);
    }

    // -------------------------------------------------------------------------
    // EncodeWav
    // -------------------------------------------------------------------------

    [Fact]
    public void EncodeWav_ProducesValidRiffWavHeader()
    {
        // Generate 0.1 s of silence at 22050 Hz
        var silence = new float[2205];
        var wav = PiperSynthesizer.EncodeWav(silence, 22050);

        // RIFF header: bytes 0-3 = "RIFF"
        Assert.Equal((byte)'R', wav[0]);
        Assert.Equal((byte)'I', wav[1]);
        Assert.Equal((byte)'F', wav[2]);
        Assert.Equal((byte)'F', wav[3]);

        // WAVE marker: bytes 8-11 = "WAVE"
        Assert.Equal((byte)'W', wav[8]);
        Assert.Equal((byte)'A', wav[9]);
        Assert.Equal((byte)'V', wav[10]);
        Assert.Equal((byte)'E', wav[11]);
    }

    [Fact]
    public void EncodeWav_OutputIsNonEmpty()
    {
        var samples = new float[100];
        var wav = PiperSynthesizer.EncodeWav(samples, 22050);
        Assert.True(wav.Length > 44, "WAV output must be longer than the 44-byte header");
    }

    // -------------------------------------------------------------------------
    // Integration (skipped when voices are not present)
    // -------------------------------------------------------------------------

    private const string EnglishModelPath =
        @"..\..\..\..\..\..\models\text-to-audio\piper-voices\en_US\en_US-joe-medium.onnx";

    [SkippableFact]
    [Trait("Category", "Integration")]
    public async Task SynthesizeAsync_ProducesAudioForEnglishText()
    {
        Skip.If(!File.Exists(EnglishModelPath),
            $"Piper voice not found at '{EnglishModelPath}'. " +
            "Download voices to models/text-to-audio/piper-voices/ and ensure espeak-ng is installed.");

        var options = Options.Create(new PiperOptions
        {
            ModelPath = Path.GetFullPath(EnglishModelPath),
        });

        using var sut = new PiperSynthesizer(options);

        var result = await sut.SynthesizeAsync("Hello, world!", "en");

        Assert.NotNull(result);
        Assert.True(result.AudioData.Length > 1000, "Expected non-trivial audio output");
        Assert.Equal(22050, result.SampleRate);

        // Validate WAV signature
        Assert.Equal((byte)'R', result.AudioData[0]);
        Assert.Equal((byte)'I', result.AudioData[1]);
        Assert.Equal((byte)'F', result.AudioData[2]);
        Assert.Equal((byte)'F', result.AudioData[3]);
    }
}
