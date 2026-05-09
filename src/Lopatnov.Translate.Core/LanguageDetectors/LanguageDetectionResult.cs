namespace Lopatnov.Translate.Core.LanguageDetectors;

/// <summary>
/// Result of language detection. Stores the model's native code and its format,
/// and exposes computed properties that convert on demand.
/// </summary>
public sealed record LanguageDetectionResult(string NativeCode, LanguageCodeFormat NativeFormat, float? Probability = null)
{
    public string Bcp47 => LanguageCodeConverter.Convert(NativeCode, NativeFormat, LanguageCodeFormat.Bcp47);
    public string Flores200 => LanguageCodeConverter.Convert(NativeCode, NativeFormat, LanguageCodeFormat.Flores200);
    public string ToFormat(LanguageCodeFormat format) => LanguageCodeConverter.Convert(NativeCode, NativeFormat, format);
}
