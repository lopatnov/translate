namespace Lopatnov.Translate.M2M100.Abstractions;

public interface IM2M100Tokenizer : IDisposable
{
    long[] Encode(string text, string sourceLanguage);
    string Decode(IEnumerable<long> tokenIds);
    long GetLanguageTokenId(string languageCode);
}
