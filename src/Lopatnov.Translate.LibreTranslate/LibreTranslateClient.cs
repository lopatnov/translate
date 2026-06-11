using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Lopatnov.Translate.Core.Abstractions;
using Lopatnov.Translate.Core.LanguageDetectors;
using Microsoft.Extensions.Options;

namespace Lopatnov.Translate.LibreTranslate;

public sealed class LibreTranslateClient : ITextTranslator
{
    private readonly HttpClient _http;
    private readonly LibreTranslateOptions _options;

    public LibreTranslateClient(HttpClient http, IOptions<LibreTranslateOptions> options)
    {
        _http = http;
        _options = options.Value;
    }

    public async Task<string> TranslateAsync(string text, string sourceLanguage, string targetLanguage, CancellationToken cancellationToken = default)
    {
        var request = new
        {
            q = text,
            source = ToIso(sourceLanguage),
            target = ToIso(targetLanguage),
            api_key = _options.ApiKey,
        };

        var response = await _http.PostAsJsonAsync("/translate", request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<LibreTranslateResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Empty response from LibreTranslate");

        return result.TranslatedText;
    }

    // LibreTranslate's native codes are ISO 639-1 (e.g. "en"). Convert from BCP-47
    // (the system interchange format): "en" stays "en", "zh-Hans" collapses to "zh",
    // "en-US" collapses to "en". Unknown codes (including "auto") pass through
    // unchanged — LibreTranslate accepts "auto" natively as source language,
    // triggering its own server-side language detection.
    internal static string ToIso(string bcp47) =>
        LanguageCodeConverter.Convert(bcp47, LanguageCodeFormat.Bcp47, LanguageCodeFormat.ISO639_1);

    private sealed record LibreTranslateResponse([property: JsonPropertyName("translatedText")] string TranslatedText);
}
