using Lopatnov.Translate.Core.Models;

namespace Lopatnov.Translate.Core.Abstractions;

public interface ISpeechSynthesizer
{
    Task<SynthesisResult> SynthesizeAsync(string text, string language, string voice = "", float speed = 1.0f, CancellationToken cancellationToken = default);
}
