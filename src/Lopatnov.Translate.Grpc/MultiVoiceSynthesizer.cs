using Lopatnov.Translate.Core.Abstractions;
using Lopatnov.Translate.Core.Models;
using Lopatnov.Translate.Piper;

namespace Lopatnov.Translate.Grpc;

/// <summary>
/// Aggregates multiple <see cref="PiperSynthesizer"/> instances (one per language)
/// and dispatches <see cref="SynthesizeAsync"/> to the voice whose key matches
/// the requested language code.
///
/// <para>
/// Language matching uses BCP-47 primary subtag (e.g. "en" matches both "en" and "en-US").
/// </para>
/// </summary>
internal sealed class MultiVoiceSynthesizer : ISpeechSynthesizer, IDisposable
{
    // Keyed by ISO 639-1 / BCP-47 primary subtag, case-insensitive.
    private readonly IReadOnlyDictionary<string, PiperSynthesizer> _voices;

    public MultiVoiceSynthesizer(IReadOnlyDictionary<string, PiperSynthesizer> voices)
    {
        _voices = voices;
    }

    /// <inheritdoc />
    public Task<SynthesisResult> SynthesizeAsync(
        string text,
        string language,
        string voice = "",
        float speed = 1.0f,
        CancellationToken cancellationToken = default)
    {
        var synth = ResolveVoice(language)
            ?? throw new NotSupportedException(
                $"No Piper TTS voice is configured for language '{language}'. " +
                $"Available: {string.Join(", ", _voices.Keys)}.");

        return synth.SynthesizeAsync(text, language, voice, speed, cancellationToken);
    }

    /// <summary>
    /// Finds the best-matching <see cref="PiperSynthesizer"/> for the given language tag.
    /// Tries exact match first, then primary-subtag match (e.g. "en" from "en-US").
    /// </summary>
    private PiperSynthesizer? ResolveVoice(string language)
    {
        if (string.IsNullOrWhiteSpace(language))
            return _voices.Count == 1 ? _voices.Values.First() : null;

        // Exact match (e.g. "en")
        if (_voices.TryGetValue(language, out var exact))
            return exact;

        // Primary subtag match: "en-US" → try "en"
        var primary = language.Contains('-')
            ? language[..language.IndexOf('-')]
            : language;

        return _voices.TryGetValue(primary, out var byPrimary) ? byPrimary : null;
    }

    public void Dispose()
    {
        foreach (var synth in _voices.Values)
            synth.Dispose();
    }
}
