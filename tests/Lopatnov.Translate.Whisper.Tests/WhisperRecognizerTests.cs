using Lopatnov.Translate.Whisper;
using Microsoft.Extensions.Options;

namespace Lopatnov.Translate.Whisper.Tests;

/// <summary>
/// Unit tests for WhisperRecognizer that do NOT require a model file.
/// </summary>
public sealed class WhisperRecognizerTests
{
    [Fact]
    public async Task TranscribeAsync_ThrowsInvalidOperation_WhenModelPathIsEmpty()
    {
        var options  = Options.Create(new WhisperOptions { ModelPath = string.Empty });
        using var sut = new WhisperRecognizer(options);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.TranscribeAsync([], "auto"));
    }

    [Fact]
    public async Task TranscribeAsync_ThrowsFileNotFound_WhenModelFileMissing()
    {
        var options  = Options.Create(new WhisperOptions { ModelPath = "/nonexistent/path/ggml.bin" });
        using var sut = new WhisperRecognizer(options);

        await Assert.ThrowsAsync<FileNotFoundException>(
            () => sut.TranscribeAsync([], "auto"));
    }

    [Fact]
    public async Task TranscribeAsync_ThrowsObjectDisposed_AfterDispose()
    {
        var options  = Options.Create(new WhisperOptions { ModelPath = "dummy.bin" });
        var sut      = new WhisperRecognizer(options);
        sut.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(
            () => sut.TranscribeAsync([], "auto"));
    }
}
