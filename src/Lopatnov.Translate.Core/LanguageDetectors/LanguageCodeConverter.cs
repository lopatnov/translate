namespace Lopatnov.Translate.Core.LanguageDetectors;

/// <summary>
/// Converts language codes between formats: BCP-47, FLORES-200, ISO 639-1/3, and "native" (passthrough).
/// Default format (empty string) is BCP-47. Unknown codes are passed through as-is.
/// </summary>
public static class LanguageCodeConverter
{
    /// <summary>
    /// Converts <paramref name="code"/> from <paramref name="fromFormat"/> to <paramref name="toFormat"/>.
    /// Supported format strings: "bcp47" (default when empty), "flores200", "native".
    /// Unknown codes are returned unchanged.
    /// </summary>
    public static string Convert(string code, string fromFormat, string toFormat) =>
        Convert(code, ParseFormat(fromFormat), ParseFormat(toFormat));

    public static string Convert(string code, LanguageCodeFormat fromFormat, LanguageCodeFormat toFormat)
    {
        if (string.IsNullOrEmpty(code)
            || fromFormat == toFormat
            || toFormat == LanguageCodeFormat.Native
            || toFormat == LanguageCodeFormat.None)
            return code;

        // Normalise to FLORES-200 as the internal pivot.
        // None/Native → passthrough (no conversion).
        string flores = fromFormat switch
        {
            LanguageCodeFormat.None      => code,
            LanguageCodeFormat.Flores200 => code,
            LanguageCodeFormat.ISO639_1  => _bcp47ToFlores200.GetValueOrDefault(code,
                                                _iso639_3ToFlores200.GetValueOrDefault(code, code)),
            LanguageCodeFormat.ISO639_2 or
            LanguageCodeFormat.ISO639_3  => _iso639_3ToFlores200.GetValueOrDefault(code,
                                                _bcp47ToFlores200.GetValueOrDefault(code, code)),
            LanguageCodeFormat.Bcp47     => _bcp47ToFlores200.GetValueOrDefault(code, code),
            _                            => code,
        };

        return toFormat switch
        {
            LanguageCodeFormat.Flores200 => flores,
            LanguageCodeFormat.Bcp47     => _flores200ToBcp47.GetValueOrDefault(flores, flores),
            LanguageCodeFormat.ISO639_1  => _flores200ToIso639_1.Value.GetValueOrDefault(flores, flores),
            LanguageCodeFormat.ISO639_2 or
            LanguageCodeFormat.ISO639_3  => _flores200ToIso639_3.Value.GetValueOrDefault(flores, flores),
            _                            => code,
        };
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
    // Primary mapping: FLORES-200 → BCP-47 (single source of truth)
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
            [Language.ChineseSimplified] = "zh-Hans",
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
        map["zh"]      = Language.ChineseSimplified; // plain "zh" → Simplified Chinese
        map["zh-CN"]   = Language.ChineseSimplified;
        map["zh-Hant"] = "zho_Hant"; // Traditional Chinese (NLLB token, not in forward map)
        map["zh-TW"]   = "zho_Hant";
        map["no"]      = "nob_Latn"; // Norwegian macro-tag → Bokmål (ISO 639-1)
        return map;
    }

    // ISO 639-3 → FLORES-200 (3-letter codes from GlotLID / fastText models).
    // ISO 639-1 (2-letter) lookups are served from _bcp47ToFlores200 instead,
    // which avoids duplicating FLORES literals in two separate sections.
    private static readonly Dictionary<string, string> _iso639_3ToFlores200 =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["eng"] = "eng_Latn",
            ["deu"] = "deu_Latn",
            ["fra"] = "fra_Latn",
            ["spa"] = "spa_Latn",
            ["ita"] = "ita_Latn",
            ["por"] = "por_Latn",
            ["nld"] = "nld_Latn",
            ["pol"] = "pol_Latn",
            ["ces"] = "ces_Latn",
            ["slk"] = "slk_Latn",
            ["slv"] = "slv_Latn",
            ["hun"] = "hun_Latn",
            ["ron"] = "ron_Latn",
            ["bul"] = "bul_Cyrl",
            ["hrv"] = "hrv_Latn",
            ["srp"] = "srp_Cyrl",
            ["rus"] = "rus_Cyrl",
            ["ukr"] = "ukr_Cyrl",
            ["bel"] = "bel_Cyrl",
            ["mkd"] = "mkd_Cyrl",
            ["bos"] = "bos_Latn",
            ["lit"] = "lit_Latn",
            ["lav"] = "lvs_Latn",
            ["lvs"] = "lvs_Latn",
            ["est"] = "est_Latn",
            ["fin"] = "fin_Latn",
            ["swe"] = "swe_Latn",
            ["nob"] = "nob_Latn",
            ["nno"] = "nno_Latn",
            ["dan"] = "dan_Latn",
            ["tur"] = "tur_Latn",
            ["aze"] = "azj_Latn",
            ["kaz"] = "kaz_Cyrl",
            ["kir"] = "kir_Cyrl",
            ["uzb"] = "uzn_Latn",
            ["tgk"] = "tgk_Cyrl",
            ["ara"] = "arb_Arab",
            ["fas"] = "pes_Arab",
            ["urd"] = "urd_Arab",
            ["hin"] = "hin_Deva",
            ["ben"] = "ben_Beng",
            ["mar"] = "mar_Deva",
            ["tam"] = "tam_Taml",
            ["tel"] = "tel_Telu",
            ["mal"] = "mal_Mlym",
            ["kan"] = "kan_Knda",
            ["guj"] = "guj_Gujr",
            ["pan"] = "pan_Guru",
            ["nep"] = "npi_Deva",
            ["npi"] = "npi_Deva",
            ["sin"] = "sin_Sinh",
            ["zho"] = Language.ChineseSimplified,
            ["cmn"] = Language.ChineseSimplified,
            ["yue"] = "yue_Hant",
            ["jpn"] = "jpn_Jpan",
            ["kor"] = "kor_Hang",
            ["vie"] = "vie_Latn",
            ["tha"] = "tha_Thai",
            ["khm"] = "khm_Khmr",
            ["lao"] = "lao_Laoo",
            ["mya"] = "mya_Mymr",
            ["kat"] = "kat_Geor",
            ["hye"] = "hye_Armn",
            ["heb"] = "heb_Hebr",
            ["ind"] = "ind_Latn",
            ["zsm"] = "zsm_Latn",
            ["msa"] = "zsm_Latn",
            ["tgl"] = "tgl_Latn",
            ["swa"] = "swh_Latn",
            ["swh"] = "swh_Latn",
            ["cym"] = "cym_Latn",
            ["eus"] = "eus_Latn",
            ["glg"] = "glg_Latn",
            ["cat"] = "cat_Latn",
            ["afr"] = "afr_Latn",
            ["isl"] = "isl_Latn",
            ["mlt"] = "mlt_Latn",
            ["sqi"] = "als_Latn",
            ["khk"] = "khk_Cyrl",
            ["mon"] = "khk_Cyrl",
            ["jav"] = "jav_Latn",
            ["sun"] = "sun_Latn",
            ["mlg"] = "plt_Latn",
            ["epo"] = "epo_Latn",
            ["ell"] = "ell_Grek",
        };

    // FLORES-200 → ISO 639-1: derive from the primary map (BCP-47 2-letter = ISO 639-1 for most languages).
    private static readonly Lazy<Dictionary<string, string>> _flores200ToIso639_1 = new(() =>
    {
        var d = _flores200ToBcp47
            .Where(kv => kv.Value.Length == 2)
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);
        // BCP-47 complex tags (zh-Hans etc.) collapse to the 2-letter ISO 639-1 macro-tag
        d[Language.ChineseSimplified] = "zh";
        d["zho_Hant"] = "zh";
        d["cmn_Hani"] = "zh";
        d["yue_Hant"] = "zh";
        return d;
    });

    // FLORES-200 → ISO 639-3: invert the 3-letter section of _iso639_3ToFlores200.
    // When multiple ISO codes share a FLORES target (e.g. "swa"/"swh" → "swh_Latn"),
    // prefer the code whose first 3 characters match the FLORES prefix (e.g. "swh"),
    // falling back to alphabetical order for full determinism.
    private static readonly Lazy<Dictionary<string, string>> _flores200ToIso639_3 = new(() =>
        _iso639_3ToFlores200
            .Where(kv => kv.Key.Length == 3)
            .GroupBy(kv => kv.Value)
            .ToDictionary(
                g => g.Key,
                g =>
                {
                    // e.g. "swh" from "swh_Latn"
                    var prefix = g.Key.Split('_')[0];
                    return g
                        .OrderByDescending(kv => kv.Key.Equals(prefix, StringComparison.OrdinalIgnoreCase))
                        .ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
                        .First().Key;
                },
                StringComparer.OrdinalIgnoreCase));
}
