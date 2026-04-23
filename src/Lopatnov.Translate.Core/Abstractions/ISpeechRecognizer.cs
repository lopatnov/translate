using Lopatnov.Translate.Core.Models;

namespace Lopatnov.Translate.Core.Abstractions;

public interface ISpeechRecognizer
{
    Task<TranscriptionResult> TranscribeAsync(byte[] audioData, string language = "auto", CancellationToken cancellationToken = default);
}
