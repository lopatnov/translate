namespace Lopatnov.Translate.Nllb.Abstractions;

public interface INllbTokenizer : IDisposable
{
    long[] Encode(string text, string sourceLanguage);
    string Decode(IEnumerable<long> tokenIds);
    long GetLanguageTokenId(string languageCode);
}
