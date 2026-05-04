using Lopatnov.Translate.Core.LanguageDetectors;

namespace Lopatnov.Translate.Core.Abstractions;

public interface ILanguageDetector
{
    LanguageDetectionResult Detect(string text);
}
