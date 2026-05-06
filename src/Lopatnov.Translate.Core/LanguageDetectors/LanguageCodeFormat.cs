namespace Lopatnov.Translate.Core.LanguageDetectors;

public enum LanguageCodeFormat
{
    None,
    Native,
    Bcp47,
    ISO639_1,
    ISO639_2,
    ISO639_3,
    Flores200
}

public static class LanguageCodeFormats
{
    public const string None = "N/A";
    public const string Native = "native";
    public const string Bcp47 = "bcp47";
    public const string Iso639_1 = "iso639-1";
    public const string Iso639_2 = "iso639-2";
    public const string Iso639_3 = "iso639-3";
    public const string Flores200 = "flores200";

    public static LanguageCodeFormat ToLanguageCodeFormat(this string format) {
        if (string.IsNullOrEmpty(format))
            return LanguageCodeFormat.Bcp47;
        var result = format.ToLowerInvariant() switch
        {
            None => LanguageCodeFormat.None,
            "n/a" => LanguageCodeFormat.None,
            Native => LanguageCodeFormat.Native,
            "bcp-47" => LanguageCodeFormat.Bcp47,
            Bcp47 => LanguageCodeFormat.Bcp47,
            Flores200 => LanguageCodeFormat.Flores200,
            Iso639_1 => LanguageCodeFormat.ISO639_1,
            "iso639_1" => LanguageCodeFormat.ISO639_1,
            Iso639_2 => LanguageCodeFormat.ISO639_2,
            "iso639_2" => LanguageCodeFormat.ISO639_2,
            Iso639_3 => LanguageCodeFormat.ISO639_3,
            "iso639_3" => LanguageCodeFormat.ISO639_3,
            _ => throw new ArgumentException($"Unsupported language code format: '{format}'", nameof(format))
        };
        return result;
    } 

    public static string ToLanguageCodeString(this LanguageCodeFormat format) => format switch
    {
        LanguageCodeFormat.None => None,
        LanguageCodeFormat.Native => Native,
        LanguageCodeFormat.Bcp47 => Bcp47,
        LanguageCodeFormat.Flores200 => Flores200,
        LanguageCodeFormat.ISO639_1 => Iso639_1,
        LanguageCodeFormat.ISO639_2 => Iso639_2,
        LanguageCodeFormat.ISO639_3 => Iso639_3,
        _ => throw new ArgumentOutOfRangeException(nameof(format), $"Unsupported language code format: '{format}'")
    };
}
