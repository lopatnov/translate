using Lopatnov.Translate.Core.LanguageDetectors;

namespace Lopatnov.Translate.Core.Tests;

public sealed class LanguageCodeConverterTests
{
    // ── Flores200 → BCP-47 ────────────────────────────────────────────────────

    [Theory]
    [InlineData("eng_Latn", "en")]
    [InlineData("ukr_Cyrl", "uk")]
    [InlineData("rus_Cyrl", "ru")]
    [InlineData("deu_Latn", "de")]
    [InlineData("fra_Latn", "fr")]
    [InlineData("spa_Latn", "es")]
    [InlineData("zho_Hans", "zh-Hans")]
    [InlineData("jpn_Jpan", "ja")]
    [InlineData("arb_Arab", "ar")]
    [InlineData("nob_Latn", "nb")]
    [InlineData("nno_Latn", "nn")]
    [InlineData("cmn_Hani", "zh-Hans")]
    [InlineData("yue_Hant", "yue")]
    public void Convert_Flores200ToBcp47_ReturnsCorrectCode(string flores, string expectedBcp47)
    {
        Assert.Equal(expectedBcp47, LanguageCodeConverter.Convert(flores, LanguageCodeFormat.Flores200, LanguageCodeFormat.Bcp47));
    }

    // ── BCP-47 → Flores200 ────────────────────────────────────────────────────

    [Theory]
    [InlineData("en",      "eng_Latn")]
    [InlineData("uk",      "ukr_Cyrl")]
    [InlineData("ru",      "rus_Cyrl")]
    [InlineData("de",      "deu_Latn")]
    [InlineData("fr",      "fra_Latn")]
    [InlineData("zh",      "zho_Hans")]
    [InlineData("zh-Hans", "zho_Hans")]
    [InlineData("zh-CN",   "zho_Hans")]
    [InlineData("ja",      "jpn_Jpan")]
    [InlineData("ar",      "arb_Arab")]
    public void Convert_Bcp47ToFlores200_ReturnsCorrectCode(string bcp47, string expectedFlores)
    {
        Assert.Equal(expectedFlores, LanguageCodeConverter.Convert(bcp47, LanguageCodeFormat.Bcp47, LanguageCodeFormat.Flores200));
    }

    // ── Same format → identity ────────────────────────────────────────────────

    [Theory]
    [InlineData("eng_Latn", "flores200")]
    [InlineData("en",       "bcp47")]
    [InlineData("anything", "native")]
    public void Convert_SameFormat_ReturnsUnchanged(string code, string format)
    {
        Assert.Equal(code, LanguageCodeConverter.Convert(code, format.ToLanguageCodeFormat(), format.ToLanguageCodeFormat()));
    }

    // ── "native" target → passthrough ─────────────────────────────────────────

    [Theory]
    [InlineData("eng_Latn", "flores200")]
    [InlineData("en",       "bcp47")]
    [InlineData("whatever", "native")]
    public void Convert_ToNative_AlwaysReturnsInputUnchanged(string code, string fromFormat)
    {
        Assert.Equal(code, LanguageCodeConverter.Convert(code, fromFormat.ToLanguageCodeFormat(), LanguageCodeFormat.Native));
    }

    // ── Unknown codes passthrough ─────────────────────────────────────────────

    [Fact]
    public void Convert_UnknownCode_PassesThroughUnchanged()
    {
        Assert.Equal("xyz_Unknown", LanguageCodeConverter.Convert("xyz_Unknown", LanguageCodeFormat.Flores200, LanguageCodeFormat.Bcp47));
        Assert.Equal("xx",          LanguageCodeConverter.Convert("xx", LanguageCodeFormat.Bcp47, LanguageCodeFormat.Flores200));
    }

    // ── Empty / null input ────────────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData(null!)]
    public void Convert_EmptyOrNull_ReturnsInputUnchanged(string? code)
    {
        Assert.Equal(code, LanguageCodeConverter.Convert(code!, LanguageCodeFormat.Flores200, LanguageCodeFormat.Bcp47));
    }

    // ── Format string parsing: aliases and case insensitivity ─────────────────

    [Theory]
    [InlineData("bcp47")]
    [InlineData("BCP47")]
    [InlineData("bcp-47")]
    [InlineData("BCP-47")]
    [InlineData("")]
    [InlineData(null!)]
    public void Convert_Bcp47FormatVariants_AllResolveToSameResult(string? format)
    {
        // Tests that various "bcp47" string spellings all resolve to the same result via the string API.
        Assert.Equal("uk", LanguageCodeConverter.Convert("ukr_Cyrl", "flores200", format!));
    }

    [Theory]
    [InlineData("flores200")]
    [InlineData("FLORES200")]
    [InlineData("flores-200")]
    [InlineData("FLORES-200")]
    public void Convert_Flores200FormatVariants_AllResolveToSameResult(string format)
    {
        // Tests that various "flores200" string spellings all resolve to the same result via the string API.
        Assert.Equal("uk", LanguageCodeConverter.Convert("ukr_Cyrl", format, "bcp47"));
    }

    // ── LanguageDetectionResult computed properties ───────────────────────────

    [Fact]
    public void DetectionResult_Bcp47_ConvertsFromNativeFlores()
    {
        var result = new LanguageDetectionResult("ukr_Cyrl", LanguageCodeFormat.Flores200);
        Assert.Equal("uk", result.Bcp47);
        Assert.Equal("ukr_Cyrl", result.Flores200);
        Assert.Equal("ukr_Cyrl", result.ToFormat(LanguageCodeFormat.Native));
        Assert.Equal("ukr_Cyrl", result.ToFormat(LanguageCodeFormat.Flores200));
        Assert.Equal("uk", result.ToFormat(LanguageCodeFormat.Bcp47));
    }

    [Fact]
    public void DetectionResult_AlreadyBcp47_RoundTrips()
    {
        var result = new LanguageDetectionResult("uk", LanguageCodeFormat.Bcp47);
        Assert.Equal("uk", result.Bcp47);
        Assert.Equal("ukr_Cyrl", result.Flores200);
    }
}
