using Lopatnov.Translate.Core.Abstractions;
using Lopatnov.Translate.Core.Models;

namespace Lopatnov.Translate.Grpc;

/// <summary>
/// No-op <see cref="ISpeechSynthesizer"/> registered when <c>Translation:TextToAudio</c>
/// is not configured or empty.
/// Throws <see cref="NotSupportedException"/> on any call so the gRPC service can
/// return <see cref="Grpc.Core.StatusCode.FailedPrecondition"/> with a clear message.
/// </summary>
internal sealed class NullSpeechSynthesizer : ISpeechSynthesizer
{
    public Task<SynthesisResult> SynthesizeAsync(
        string text,
        string language,
        string voice = "",
        float speed = 1.0f,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException(
            "Text-to-speech is not configured. " +
            "Set Translation:TextToAudio to a map of language codes → Piper model entries " +
            "(Type=Piper) in appsettings.json.");
}
