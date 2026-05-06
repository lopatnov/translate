using Lopatnov.Translate.Core.Models;
using Lopatnov.Translate.Whisper;
using Microsoft.Extensions.Options;
using NAudio.Wave;

namespace Lopatnov.Translate.Whisper.Tests;

/// <summary>
/// Integration tests for WhisperRecognizer that require a real ggml model file.
/// Tests are skipped automatically when the model is not present.
///
/// To run:
///   1. Execute scripts/download-whisper.ps1 (or manually place ggml-small.bin).
///   2. dotnet test --filter "Category=Integration"
/// </summary>
[Trait("Category", "Integration")]
public sealed class WhisperRecognizerIntegrationTests
{
    private static readonly string ModelPath = ResolveModelPath();

    private static string ResolveModelPath()
    {
        var env = Environment.GetEnvironmentVariable("Whisper__ModelPath");
        if (!string.IsNullOrEmpty(env))
            return env;

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "translate.slnx")))
                return Path.Combine(
                    dir.FullName,
                    "models", "audio-to-text", "whisper.cpp", "ggml-small.bin");
            dir = dir.Parent;
        }
        return Path.Combine("models", "audio-to-text", "whisper.cpp", "ggml-small.bin");
    }

    /// <summary>
    /// Generates a 1-second silent WAV at 16 kHz mono PCM (16-bit).
    /// A silent clip won't produce speech segments, but exercises the full
    /// model-loading and audio-processing pipeline without requiring a
    /// real voice recording checked into source control.
    /// </summary>
    private static byte[] BuildSilentWav(double durationSeconds = 1.0)
    {
        int sampleRate  = 16000;
        int totalFrames = (int)(sampleRate * durationSeconds);
        using var ms     = new MemoryStream();
        using var writer = new WaveFileWriter(ms, new WaveFormat(sampleRate, 16, 1));
        var silence = new byte[totalFrames * 2]; // 16-bit = 2 bytes/frame
        writer.Write(silence, 0, silence.Length);
        writer.Flush();
        return ms.ToArray();
    }

    [SkippableFact]
    public async Task TranscribeAsync_SilentAudio_ReturnsResultWithoutCrashing()
    {
        Skip.If(!File.Exists(ModelPath),
            $"Whisper model not found at '{ModelPath}'. Run scripts/download-whisper.ps1.");

        var options  = Options.Create(new WhisperOptions { ModelPath = ModelPath });
        using var sut = new WhisperRecognizer(options);

        var wav    = BuildSilentWav(durationSeconds: 1.0);
        var result = await sut.TranscribeAsync(wav, "auto");

        // A silent clip may produce empty text — that's fine.
        // The important thing is that the model loaded and returned a valid result.
        Assert.NotNull(result);
        Assert.NotNull(result.Segments);
        Assert.NotNull(result.FullText);
    }

    [SkippableFact]
    public async Task TranscribeAsync_CancellationRequested_ThrowsOrCompletes()
    {
        Skip.If(!File.Exists(ModelPath),
            $"Whisper model not found at '{ModelPath}'. Run scripts/download-whisper.ps1.");

        var options  = Options.Create(new WhisperOptions { ModelPath = ModelPath });
        using var sut = new WhisperRecognizer(options);
        var wav       = BuildSilentWav(durationSeconds: 0.5);

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // cancel immediately

        // Should either complete quickly or throw OperationCanceledException — both are valid.
        TranscriptionResult? result = null;
        var wasCancelled = false;
        try
        {
            result = await sut.TranscribeAsync(wav, "auto", cts.Token);
        }
        catch (OperationCanceledException) { wasCancelled = true; }

        Assert.True(wasCancelled || result is not null,
            "Expected either OperationCanceledException or a valid TranscriptionResult.");
    }
}
