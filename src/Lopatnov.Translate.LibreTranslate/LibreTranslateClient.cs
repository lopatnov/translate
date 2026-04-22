using System.Net.Http.Json;
using Lopatnov.Translate.Core.Abstractions;
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
        _http.BaseAddress = new Uri(_options.BaseUrl);
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

    // LibreTranslate expects ISO 639-1 codes (e.g. "en"), not FLORES-200 (e.g. "eng_Latn").
    // Unknown codes pass through unchanged so callers using native ISO codes still work.
    internal static string ToIso(string flores) =>
        FloresIsoCodes.TryGetValue(flores, out var iso) ? iso : flores;

    private static readonly IReadOnlyDictionary<string, string> FloresIsoCodes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["eng_Latn"] = "en",
            ["ukr_Cyrl"] = "uk",
            ["rus_Cyrl"] = "ru",
            ["deu_Latn"] = "de",
            ["fra_Latn"] = "fr",
            ["spa_Latn"] = "es",
            ["pol_Latn"] = "pl",
            ["por_Latn"] = "pt",
            ["ita_Latn"] = "it",
            ["nld_Latn"] = "nl",
            ["zho_Hans"] = "zh",
            ["zho_Hant"] = "zh",
            ["jpn_Jpan"] = "ja",
            ["kor_Hang"] = "ko",
            ["arb_Arab"] = "ar",
            ["hin_Deva"] = "hi",
            ["tur_Latn"] = "tr",
            ["vie_Latn"] = "vi",
            ["tha_Thai"] = "th",
            ["swe_Latn"] = "sv",
            ["dan_Latn"] = "da",
            ["fin_Latn"] = "fi",
            ["ces_Latn"] = "cs",
            ["ron_Latn"] = "ro",
            ["hun_Latn"] = "hu",
            ["bul_Cyrl"] = "bg",
            ["hrv_Latn"] = "hr",
            ["slk_Latn"] = "sk",
            ["slv_Latn"] = "sl",
            ["lit_Latn"] = "lt",
            ["lvs_Latn"] = "lv",
            ["est_Latn"] = "et",
        };

    private sealed record LibreTranslateResponse(string TranslatedText);
}
