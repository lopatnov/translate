namespace Lopatnov.Translate.Core.LanguageDetectors;

/// <summary>
/// Result of language detection. Stores the model's native code and its format,
/// and exposes computed properties that convert on demand.
/// BCP-47 is the interchange format; use <see cref="ToFormat"/> for anything else.
/// </summary>
public sealed record LanguageDetectionResult(string NativeCode, LanguageCodeFormat NativeFormat, float? Probability = null)
{
    public string Bcp47 => LanguageCodeConverter.Convert(NativeCode, NativeFormat, LanguageCodeFormat.Bcp47);
    public string ToFormat(LanguageCodeFormat format) => LanguageCodeConverter.Convert(NativeCode, NativeFormat, format);
}
