namespace Lopatnov.Translate.Core.LanguageDetectors;

/// <summary>
/// Converts language codes between formats: BCP-47, FLORES-200, ISO 639-1/2/3, and "native" (passthrough).
/// <para>
/// <b>BCP-47 is the internal pivot</b>: every conversion first normalises the input to BCP-47,
/// then maps from BCP-47 to the target format. FLORES-200 is treated as just another
/// model-native format (NLLB / GlotLID), never as an intermediate.
/// </para>
/// Default format (empty string) is BCP-47. Unknown codes are passed through unchanged,
/// which lets model-native codes flow through the system untouched.
/// </summary>
public static class LanguageCodeConverter
{
    /// <summary>
    /// Converts <paramref name="code"/> from <paramref name="fromFormat"/> to <paramref name="toFormat"/>.
    /// Supported format strings: "bcp47" (default when empty), "flores200", "iso639-1/2/3", "native".
    /// Unknown codes are returned unchanged.
    /// </summary>
    public static string Convert(string code, string fromFormat, string toFormat) =>
        Convert(code, ParseFormat(fromFormat), ParseFormat(toFormat));

    public static string Convert(string code, LanguageCodeFormat fromFormat, LanguageCodeFormat toFormat)
    {
        if (string.IsNullOrEmpty(code)
            || fromFormat == toFormat
            || fromFormat == LanguageCodeFormat.Native
            || toFormat == LanguageCodeFormat.Native
            || toFormat == LanguageCodeFormat.None)
            return code;

        return FromBcp47(ToBcp47(code, fromFormat), toFormat, code);
    }

    /// <summary>
    /// Normalises a code from any source format to BCP-47 (the internal pivot).
    /// Unknown codes pass through unchanged.
    /// </summary>
    public static string ToBcp47(string code, LanguageCodeFormat fromFormat) => fromFormat switch
    {
        LanguageCodeFormat.Flores200 => _flores200ToBcp47.GetValueOrDefault(code, code),
        LanguageCodeFormat.ISO639_1 or
        LanguageCodeFormat.ISO639_2 or
        LanguageCodeFormat.ISO639_3 => Iso639ToBcp47(code),
        _ => code, // Bcp47, Native, None — already the pivot or passthrough
    };

    // ISO 639-1 two-letter codes are valid BCP-47 primary subtags already and fall
    // through to passthrough. Three-letter codes resolve via the ISO 639-3 table.
    // GlotLID v3 labels are ISO 639-3 codes with a script suffix (e.g. "ukr_Cyrl",
    // 2102 labels) — the FLORES-200 table shares that shape and covers the common
    // ones; otherwise the bare ISO 639-3 prefix is tried. Unknown codes pass through.
    private static string Iso639ToBcp47(string code)
    {
        if (_iso639_3ToBcp47.TryGetValue(code, out var bcp47))
            return bcp47;
        if (_flores200ToBcp47.TryGetValue(code, out bcp47))
            return bcp47;
        var underscore = code.IndexOf('_');
        if (underscore > 0 && _iso639_3ToBcp47.TryGetValue(code[..underscore], out bcp47))
            return bcp47;
        return code;
    }

    private static string FromBcp47(string bcp47, LanguageCodeFormat toFormat, string original) => toFormat switch
    {
        LanguageCodeFormat.Bcp47     => bcp47,
        LanguageCodeFormat.Flores200 => LookupWithPrimarySubtag(_bcp47ToFlores200, bcp47),
        LanguageCodeFormat.ISO639_1  => ToIso639_1(bcp47),
        LanguageCodeFormat.ISO639_2 or
        LanguageCodeFormat.ISO639_3  => LookupWithPrimarySubtag(_bcp47ToIso639_3.Value, bcp47),
        _                            => original,
    };

    // Exact match first; then progressively strip subtags from the right so the most
    // specific known tag wins: "zh-Hant-HK" → "zh-Hant" (Traditional) → "zh", and
    // "en-US" → "en" → "eng_Latn". Stripping the leftmost subtag instead would turn
    // "zh-Hant-HK" into "zh" and silently lose the script. Unknown codes pass through.
    private static string LookupWithPrimarySubtag(IReadOnlyDictionary<string, string> map, string bcp47)
    {
        var tag = bcp47;
        while (true)
        {
            if (map.TryGetValue(tag, out var mapped))
                return mapped;
            var dash = tag.LastIndexOf('-');
            if (dash <= 0)
                return bcp47;
            tag = tag[..dash];
        }
    }

    private static string ToIso639_1(string bcp47)
    {
        // Tags whose ISO 639-1 form is not simply the primary subtag (zh-Hant, yue, …).
        // Strip subtags from the right so "yue-Hant-HK" still hits the "yue" override.
        var tag = bcp47;
        while (true)
        {
            if (_bcp47ToIso639_1Overrides.TryGetValue(tag, out var iso))
                return iso;
            var dash = tag.LastIndexOf('-');
            if (dash <= 0)
                break;
            tag = tag[..dash];
        }
        // "en-US" → "en"; bare 2-letter tags are already ISO 639-1.
        var first = bcp47.IndexOf('-');
        return first > 0 ? bcp47[..first] : bcp47;
    }

    // Lenient parsing for string-based API: handles hyphen variants, empty → BCP-47, unknown → Native.
    // Distinct from ToLanguageCodeFormat() which throws on unknown values.
    private static LanguageCodeFormat ParseFormat(string? format) =>
        format?.ToLowerInvariant() switch
        {
            null or "" or "bcp47" or "bcp-47" => LanguageCodeFormat.Bcp47,
            "flores200" or "flores-200"        => LanguageCodeFormat.Flores200,
            "native"                           => LanguageCodeFormat.Native,
            "n/a" or "none"                    => LanguageCodeFormat.None,
            "iso639-1" or "iso639_1"           => LanguageCodeFormat.ISO639_1,
            "iso639-2" or "iso639_2"           => LanguageCodeFormat.ISO639_2,
            "iso639-3" or "iso639_3"           => LanguageCodeFormat.ISO639_3,
            _                                  => LanguageCodeFormat.Native,
        };

    // -------------------------------------------------------------------------
    // FLORES-200 ↔ BCP-47 (FLORES side is the NLLB / GlotLID native vocabulary)
    // -------------------------------------------------------------------------

    private static readonly Dictionary<string, string> _flores200ToBcp47 =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["eng_Latn"] = "en",
            ["deu_Latn"] = "de",
            ["fra_Latn"] = "fr",
            ["spa_Latn"] = "es",
            ["ita_Latn"] = "it",
            ["por_Latn"] = "pt",
            ["nld_Latn"] = "nl",
            ["pol_Latn"] = "pl",
            ["ces_Latn"] = "cs",
            ["slk_Latn"] = "sk",
            ["slv_Latn"] = "sl",
            ["hun_Latn"] = "hu",
            ["ron_Latn"] = "ro",
            ["bul_Cyrl"] = "bg",
            ["hrv_Latn"] = "hr",
            ["srp_Cyrl"] = "sr",
            ["rus_Cyrl"] = "ru",
            ["ukr_Cyrl"] = "uk",
            ["bel_Cyrl"] = "be",
            ["mkd_Cyrl"] = "mk",
            ["bos_Latn"] = "bs",
            ["lit_Latn"] = "lt",
            ["lvs_Latn"] = "lv",
            ["est_Latn"] = "et",
            ["fin_Latn"] = "fi",
            ["swe_Latn"] = "sv",
            ["nob_Latn"] = "nb",
            ["nno_Latn"] = "nn",
            ["dan_Latn"] = "da",
            ["tur_Latn"] = "tr",
            ["azj_Latn"] = "az",
            ["kaz_Cyrl"] = "kk",
            ["kir_Cyrl"] = "ky",
            ["uzn_Latn"] = "uz",
            ["tgk_Cyrl"] = "tg",
            ["arb_Arab"] = "ar",
            ["pes_Arab"] = "fa",
            ["urd_Arab"] = "ur",
            ["hin_Deva"] = "hi",
            ["ben_Beng"] = "bn",
            ["mar_Deva"] = "mr",
            ["tam_Taml"] = "ta",
            ["tel_Telu"] = "te",
            ["mal_Mlym"] = "ml",
            ["kan_Knda"] = "kn",
            ["guj_Gujr"] = "gu",
            ["pan_Guru"] = "pa",
            ["npi_Deva"] = "ne",
            ["sin_Sinh"] = "si",
            ["zho_Hans"] = "zh-Hans",
            ["zho_Hant"] = "zh-Hant",
            ["cmn_Hani"] = "zh-Hans",
            ["yue_Hant"] = "yue",
            ["jpn_Jpan"] = "ja",
            ["kor_Hang"] = "ko",
            ["vie_Latn"] = "vi",
            ["tha_Thai"] = "th",
            ["khm_Khmr"] = "km",
            ["lao_Laoo"] = "lo",
            ["mya_Mymr"] = "my",
            ["kat_Geor"] = "ka",
            ["hye_Armn"] = "hy",
            ["heb_Hebr"] = "he",
            ["ind_Latn"] = "id",
            ["zsm_Latn"] = "ms",
            ["tgl_Latn"] = "tl",
            ["swh_Latn"] = "sw",
            ["cym_Latn"] = "cy",
            ["eus_Latn"] = "eu",
            ["glg_Latn"] = "gl",
            ["cat_Latn"] = "ca",
            ["afr_Latn"] = "af",
            ["isl_Latn"] = "is",
            ["mlt_Latn"] = "mt",
            ["als_Latn"] = "sq",
            ["khk_Cyrl"] = "mn",
            ["jav_Latn"] = "jv",
            ["sun_Latn"] = "su",
            ["plt_Latn"] = "mg",
            ["epo_Latn"] = "eo",
            ["ell_Grek"] = "el",
        };

    // Derived inverse: BCP-47 → FLORES-200.
    // Built from _flores200ToBcp47 so FLORES codes are not duplicated as literals.
    // Extra BCP-47 aliases appended for common tags not representable via a 1-to-1 inverse.
    private static readonly Dictionary<string, string> _bcp47ToFlores200 = BuildBcp47ToFlores200();

    private static Dictionary<string, string> BuildBcp47ToFlores200()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (flores, bcp47) in _flores200ToBcp47)
            map.TryAdd(bcp47, flores); // first entry wins for duplicate BCP-47 values

        // BCP-47 aliases and macro-tags not covered by the simple inverse
        map["zh"]    = "zho_Hans"; // plain "zh" → Simplified Chinese
        map["zh-CN"] = "zho_Hans";
        map["zh-TW"] = "zho_Hant";
        map["no"]    = "nob_Latn"; // Norwegian macro-tag → Bokmål
        return map;
    }

    // -------------------------------------------------------------------------
    // ISO 639-3 ↔ BCP-47 (3-letter codes from GlotLID / fastText models)
    // -------------------------------------------------------------------------

    // ISO 639-1 (2-letter) inputs need no table — they are valid BCP-47 primary subtags.
    private static readonly Dictionary<string, string> _iso639_3ToBcp47 =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["eng"] = "en",
            ["deu"] = "de",
            ["fra"] = "fr",
            ["spa"] = "es",
            ["ita"] = "it",
            ["por"] = "pt",
            ["nld"] = "nl",
            ["pol"] = "pl",
            ["ces"] = "cs",
            ["slk"] = "sk",
            ["slv"] = "sl",
            ["hun"] = "hu",
            ["ron"] = "ro",
            ["bul"] = "bg",
            ["hrv"] = "hr",
            ["srp"] = "sr",
            ["rus"] = "ru",
            ["ukr"] = "uk",
            ["bel"] = "be",
            ["mkd"] = "mk",
            ["bos"] = "bs",
            ["lit"] = "lt",
            ["lav"] = "lv",
            ["lvs"] = "lv",
            ["est"] = "et",
            ["fin"] = "fi",
            ["swe"] = "sv",
            ["nob"] = "nb",
            ["nno"] = "nn",
            ["dan"] = "da",
            ["tur"] = "tr",
            ["aze"] = "az",
            ["kaz"] = "kk",
            ["kir"] = "ky",
            ["uzb"] = "uz",
            ["tgk"] = "tg",
            ["ara"] = "ar",
            ["fas"] = "fa",
            ["urd"] = "ur",
            ["hin"] = "hi",
            ["ben"] = "bn",
            ["mar"] = "mr",
            ["tam"] = "ta",
            ["tel"] = "te",
            ["mal"] = "ml",
            ["kan"] = "kn",
            ["guj"] = "gu",
            ["pan"] = "pa",
            ["nep"] = "ne",
            ["npi"] = "ne",
            ["sin"] = "si",
            ["zho"] = "zh-Hans",
            ["cmn"] = "zh-Hans",
            ["yue"] = "yue",
            ["jpn"] = "ja",
            ["kor"] = "ko",
            ["vie"] = "vi",
            ["tha"] = "th",
            ["khm"] = "km",
            ["lao"] = "lo",
            ["mya"] = "my",
            ["kat"] = "ka",
            ["hye"] = "hy",
            ["heb"] = "he",
            ["ind"] = "id",
            ["zsm"] = "ms",
            ["msa"] = "ms",
            ["tgl"] = "tl",
            ["swa"] = "sw",
            ["swh"] = "sw",
            ["cym"] = "cy",
            ["eus"] = "eu",
            ["glg"] = "gl",
            ["cat"] = "ca",
            ["afr"] = "af",
            ["isl"] = "is",
            ["mlt"] = "mt",
            ["sqi"] = "sq",
            ["khk"] = "mn",
            ["mon"] = "mn",
            ["jav"] = "jv",
            ["sun"] = "su",
            ["mlg"] = "mg",
            ["epo"] = "eo",
            ["ell"] = "el",
        };

    // BCP-47 → ISO 639-3: invert _iso639_3ToBcp47. Where several ISO codes share one
    // BCP-47 tag (macro-language vs individual code), prefer the individual code the
    // models actually use — pinned explicitly for full determinism.
    private static readonly Lazy<Dictionary<string, string>> _bcp47ToIso639_3 = new(() =>
    {
        var map = _iso639_3ToBcp47
            .GroupBy(kv => kv.Value, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase).First().Key,
                StringComparer.OrdinalIgnoreCase);

        // Ambiguous groups: pin the variant used by NLLB / GlotLID vocabularies.
        map["lv"]      = "lvs"; // Standard Latvian, not the macro code "lav"
        map["ne"]      = "npi"; // Nepali (individual), not the macro code "nep"
        map["zh-Hans"] = "zho";
        map["ms"]      = "zsm"; // Standard Malay, not the macro code "msa"
        map["sw"]      = "swh"; // Coastal Swahili, not the macro code "swa"
        map["mn"]      = "khk"; // Halh Mongolian, not the macro code "mon"
        return map;
    });

    // BCP-47 tags whose ISO 639-1 form is not simply the primary subtag.
    private static readonly Dictionary<string, string> _bcp47ToIso639_1Overrides =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["zh-Hans"] = "zh",
            ["zh-Hant"] = "zh",
            ["zh-CN"]   = "zh",
            ["zh-TW"]   = "zh",
            ["yue"]     = "zh", // Cantonese has no ISO 639-1 code; collapse to the macro tag
        };
}
