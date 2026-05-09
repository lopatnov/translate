using Lopatnov.Translate.Core.Abstractions;
using Lopatnov.Translate.Core.Models;

namespace Lopatnov.Translate.Grpc;

/// <summary>
/// No-op ISpeechRecognizer registered when Translation:AudioToText is not configured.
/// Throws <see cref="NotSupportedException"/> on any call so the gRPC service can
/// return FailedPrecondition with a clear message.
/// </summary>
internal sealed class NullSpeechRecognizer : ISpeechRecognizer
{
    public Task<TranscriptionResult> TranscribeAsync(
        byte[] audioData,
        string language = "auto",
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException(
            "Speech-to-text is not configured. " +
            "Set Translation:AudioToText to a model entry of Type=Whisper in appsettings.json.");
}
