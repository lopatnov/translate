namespace Lopatnov.Translate.Core.Abstractions;

public interface ILanguageDetector
{
    /// <summary>
    /// Detects the language of the given text and returns a FLORES-200 code (e.g. "eng_Latn").
    /// </summary>
    string Detect(string text);
}
