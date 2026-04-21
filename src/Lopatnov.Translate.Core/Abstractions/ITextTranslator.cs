namespace Lopatnov.Translate.Core.Abstractions;

public interface ITextTranslator
{
    Task<string> TranslateAsync(string text, string sourceLanguage, string targetLanguage, CancellationToken cancellationToken = default);
}
