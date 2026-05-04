namespace Lopatnov.Translate.Core.LanguageDetectors;

/// <summary>
/// Result of language detection. Stores the model's native code and its format,
/// and exposes computed properties that convert on demand.
/// </summary>
public sealed record LanguageDetectionResult(string NativeCode, string NativeFormat)
{
    public string Bcp47 => LanguageCodeConverter.Convert(NativeCode, NativeFormat, "bcp47");
    public string Flores200 => LanguageCodeConverter.Convert(NativeCode, NativeFormat, "flores200");
    public string ToFormat(string format) => LanguageCodeConverter.Convert(NativeCode, NativeFormat, format);
}
