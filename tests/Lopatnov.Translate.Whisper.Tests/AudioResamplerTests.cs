using NAudio.Wave;
using Lopatnov.Translate.Whisper;

namespace Lopatnov.Translate.Whisper.Tests;

/// <summary>
/// Unit tests for the audio resampling logic inside WhisperRecognizer.
/// No Whisper model is required — these tests run fully in-memory.
/// </summary>
public sealed class AudioResamplerTests
{
    // ---------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------

    /// <summary>Builds a WAV byte[] of the given duration, format and frequency.</summary>
    private static byte[] BuildSineWav(
        int sampleRate, int channels, double durationSeconds, float frequencyHz = 440f)
    {
        int totalFrames = (int)(sampleRate * durationSeconds);
        using var ms     = new MemoryStream();
        using var writer = new WaveFileWriter(ms, WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels));

        var frame = new float[channels];
        for (int i = 0; i < totalFrames; i++)
        {
            float sample = MathF.Sin(2 * MathF.PI * frequencyHz * i / sampleRate);
            for (int ch = 0; ch < channels; ch++)
                frame[ch] = sample;
            writer.WriteSamples(frame, 0, channels);
        }
        writer.Flush();
        return ms.ToArray();
    }

    // ---------------------------------------------------------------------------
    // Tests
    // ---------------------------------------------------------------------------

    [Fact]
    public void ResampleToWhisperFormat_AlreadyCorrectFormat_ReturnsSameSampleCount()
    {
        // Arrange: 16 kHz mono, 0.5 seconds → 8000 samples
        var wav = BuildSineWav(sampleRate: 16000, channels: 1, durationSeconds: 0.5);

        // Act
        var result = WhisperRecognizer.ResampleToWhisperFormat(wav);

        // Assert: sample count should be ≈ 8000 (allow ±1 rounding tolerance)
        Assert.InRange(result.Length, 7999, 8001);
    }

    [Fact]
    public void ResampleToWhisperFormat_StereoTo16kHz_ProducesMonoOutput()
    {
        // Arrange: 8 kHz stereo, 1 second
        // After resample: mono 16 kHz → ≈ 16000 samples
        var wav = BuildSineWav(sampleRate: 8000, channels: 2, durationSeconds: 1.0);

        // Act
        var result = WhisperRecognizer.ResampleToWhisperFormat(wav);

        // Assert: mono 16 kHz for 1 second ≈ 16000 samples (allow ±50 for resampler rounding)
        Assert.InRange(result.Length, 15950, 16050);
    }

    [Fact]
    public void ResampleToWhisperFormat_44100HzMono_UpsamplesCorrectly()
    {
        // Arrange: 44100 Hz mono, 1 second → after downsample to 16 kHz ≈ 16000 samples
        var wav = BuildSineWav(sampleRate: 44100, channels: 1, durationSeconds: 1.0);

        // Act
        var result = WhisperRecognizer.ResampleToWhisperFormat(wav);

        // Assert
        Assert.InRange(result.Length, 15900, 16100);
    }

    [Fact]
    public void ResampleToWhisperFormat_SamplesAreNormalised()
    {
        // Whisper expects float samples in [-1, 1]
        var wav = BuildSineWav(sampleRate: 16000, channels: 1, durationSeconds: 0.1);
        var result = WhisperRecognizer.ResampleToWhisperFormat(wav);

        Assert.All(result, s => Assert.InRange(s, -1.01f, 1.01f));
    }
}
