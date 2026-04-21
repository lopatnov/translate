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
        // LibreTranslate uses ISO 639-1 codes; FLORES-200 → ISO 639-1 mapping is caller's responsibility.
        var request = new
        {
            q = text,
            source = sourceLanguage,
            target = targetLanguage,
            api_key = _options.ApiKey,
        };

        var response = await _http.PostAsJsonAsync("/translate", request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<LibreTranslateResponse>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException("Empty response from LibreTranslate");

        return result.TranslatedText;
    }

    private sealed record LibreTranslateResponse(string TranslatedText);
}
