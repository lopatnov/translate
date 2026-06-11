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

    // ── BCP-47 subtag fallback: strip from the right, most specific tag wins ──

    [Theory]
    [InlineData("en-US",      "eng_Latn")]
    [InlineData("de-DE",      "deu_Latn")]
    [InlineData("uk-UA",      "ukr_Cyrl")]
    [InlineData("zh-Hant-HK", "zho_Hant")] // script must survive: zh-Hant-HK → zh-Hant, NOT zh
    [InlineData("zh-Hans-SG", "zho_Hans")]
    public void Convert_Bcp47WithExtraSubtags_ResolvesToMostSpecificMatch(string bcp47, string expectedFlores)
    {
        Assert.Equal(expectedFlores, LanguageCodeConverter.Convert(bcp47, LanguageCodeFormat.Bcp47, LanguageCodeFormat.Flores200));
    }

    [Fact]
    public void Convert_ZhoHant_RoundTripsBetweenFloresAndBcp47()
    {
        Assert.Equal("zh-Hant", LanguageCodeConverter.Convert("zho_Hant", LanguageCodeFormat.Flores200, LanguageCodeFormat.Bcp47));
        Assert.Equal("zho_Hant", LanguageCodeConverter.Convert("zh-Hant", LanguageCodeFormat.Bcp47, LanguageCodeFormat.Flores200));
    }

    // ── ISO 639-3 (incl. GlotLID script-suffixed labels) → BCP-47 ─────────────

    [Theory]
    [InlineData("eng",      "en")]
    [InlineData("ukr",      "uk")]
    [InlineData("nob",      "nb")]
    [InlineData("zho",      "zh-Hans")]
    [InlineData("ukr_Cyrl", "uk")]      // GlotLID v3 label: ISO 639-3 + script
    [InlineData("eng_Latn", "en")]      // GlotLID v3 label: ISO 639-3 + script
    [InlineData("zho_Hant", "zh-Hant")] // Traditional Chinese script label
    [InlineData("ceb",      "ceb")]     // no 2-letter code — passes through as valid BCP-47
    public void Convert_Iso639_3ToBcp47_ReturnsCorrectCode(string iso, string expectedBcp47)
    {
        Assert.Equal(expectedBcp47, LanguageCodeConverter.Convert(iso, LanguageCodeFormat.ISO639_3, LanguageCodeFormat.Bcp47));
    }

    // ── ISO 639-2/B bibliographic codes → BCP-47 ──────────────────────────────

    [Theory]
    [InlineData("ger", "de")]
    [InlineData("fre", "fr")]
    [InlineData("dut", "nl")]
    [InlineData("cze", "cs")]
    [InlineData("rum", "ro")]
    [InlineData("gre", "el")]
    [InlineData("alb", "sq")]
    [InlineData("arm", "hy")]
    [InlineData("geo", "ka")]
    [InlineData("per", "fa")]
    [InlineData("chi", "zh-Hans")]
    public void Convert_Iso639_2BibliographicToBcp47_ReturnsCorrectCode(string isoB, string expectedBcp47)
    {
        Assert.Equal(expectedBcp47, LanguageCodeConverter.Convert(isoB, LanguageCodeFormat.ISO639_2, LanguageCodeFormat.Bcp47));
    }

    [Fact]
    public void Convert_Iso639_3ScriptSuffixNotInFloresTable_ResolvesViaBarePrefix()
    {
        // "ukr_Latn" is not a FLORES-200 entry — the bare ISO 639-3 prefix resolves it.
        Assert.Equal("uk", LanguageCodeConverter.Convert("ukr_Latn", LanguageCodeFormat.ISO639_3, LanguageCodeFormat.Bcp47));
    }

    // ── BCP-47 → ISO 639-3 (macro-language pins) ──────────────────────────────

    [Theory]
    [InlineData("en",      "eng")]
    [InlineData("uk",      "ukr")]
    [InlineData("en-US",   "eng")] // region subtag collapses
    [InlineData("lv",      "lvs")] // Standard Latvian, not the macro code "lav"
    [InlineData("ne",      "npi")]
    [InlineData("zh-Hans", "zho")]
    [InlineData("ms",      "zsm")]
    [InlineData("sw",      "swh")]
    [InlineData("mn",      "khk")]
    public void Convert_Bcp47ToIso639_3_ReturnsPinnedIndividualCode(string bcp47, string expectedIso3)
    {
        Assert.Equal(expectedIso3, LanguageCodeConverter.Convert(bcp47, LanguageCodeFormat.Bcp47, LanguageCodeFormat.ISO639_3));
    }

    // ── BCP-47 → ISO 639-1 (model adapters: M2M-100, LibreTranslate) ──────────

    [Theory]
    [InlineData("en",          "en")]
    [InlineData("zh-Hans",     "zh")]
    [InlineData("zh-Hant",     "zh")]
    [InlineData("yue",         "zh")]
    [InlineData("yue-Hant-HK", "zh")] // override must win over the primary subtag "yue"
    [InlineData("en-US",       "en")]
    public void Convert_Bcp47ToIso639_1_ReturnsCorrectCode(string bcp47, string expectedIso)
    {
        Assert.Equal(expectedIso, LanguageCodeConverter.Convert(bcp47, LanguageCodeFormat.Bcp47, LanguageCodeFormat.ISO639_1));
    }

    // ── LanguageDetectionResult computed properties ───────────────────────────

    [Fact]
    public void DetectionResult_Bcp47_ConvertsFromNativeFlores()
    {
        var result = new LanguageDetectionResult("ukr_Cyrl", LanguageCodeFormat.Flores200);
        Assert.Equal("uk", result.Bcp47);
        // "native" must return the model's real raw label, untouched.
        Assert.Equal("ukr_Cyrl", result.ToFormat(LanguageCodeFormat.Native));
        Assert.Equal("ukr_Cyrl", result.ToFormat(LanguageCodeFormat.Flores200));
        Assert.Equal("uk", result.ToFormat(LanguageCodeFormat.Bcp47));
    }

    [Fact]
    public void DetectionResult_GlotLidIso639_3Label_NativePreservedBcp47Normalised()
    {
        // GlotLID v3 emits ISO 639-3 + script labels; native keeps the raw label,
        // Bcp47 normalises for downstream translation.
        var result = new LanguageDetectionResult("ukr_Cyrl", LanguageCodeFormat.ISO639_3);
        Assert.Equal("ukr_Cyrl", result.ToFormat(LanguageCodeFormat.Native));
        Assert.Equal("uk", result.Bcp47);
    }

    [Fact]
    public void DetectionResult_AlreadyBcp47_RoundTrips()
    {
        var result = new LanguageDetectionResult("uk", LanguageCodeFormat.Bcp47);
        Assert.Equal("uk", result.Bcp47);
        Assert.Equal("ukr_Cyrl", result.ToFormat(LanguageCodeFormat.Flores200));
    }
}
