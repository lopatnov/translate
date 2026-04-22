using System.Text.Json;
using Lopatnov.Translate.Nllb.Abstractions;
using Microsoft.ML.Tokenizers;

namespace Lopatnov.Translate.Nllb;

public sealed class NllbTokenizer : INllbTokenizer
{
    public const long EosTokenId = 2;
    public const long PadTokenId = 1;
    public const long BosTokenId = 0;

    private readonly SentencePieceTokenizer _tokenizer;
    private readonly IReadOnlyDictionary<string, long> _langTokenIds;

    public NllbTokenizer(string modelDir, string tokenizerFile = "sentencepiece.bpe.model", string configFile = "tokenizer.json")
    {
        var modelPath = Path.Combine(modelDir, tokenizerFile);
        using var stream = File.OpenRead(modelPath);
        _tokenizer = SentencePieceTokenizer.Create(stream, addBeginningOfSentence: false, addEndOfSentence: false, specialTokens: null);

        var configPath = Path.Combine(modelDir, configFile);
        _langTokenIds = LoadLanguageTokenIds(configPath);
    }

    public long[] Encode(string text, string sourceLanguage)
    {
        var langId = GetLanguageTokenId(sourceLanguage);
        var tokenIds = _tokenizer.EncodeToIds(text, considerPreTokenization: true, considerNormalization: true);

        // NLLB encoder format: [src_lang_code] X [EOS]
        // SentencePiece IDs need +1 because HuggingFace NLLB inserts <pad> at ID 1,
        // shifting all regular token IDs up by one relative to the raw .bpe.model IDs.
        var result = new long[tokenIds.Count + 2];
        result[0] = langId;
        for (var i = 0; i < tokenIds.Count; i++)
            result[i + 1] = tokenIds[i] + 1;
        result[^1] = EosTokenId;
        return result;
    }

    public string Decode(IEnumerable<long> tokenIds)
    {
        var langIds = new HashSet<long>(_langTokenIds.Values);
        var filtered = tokenIds
            .Where(id => id > 3 && !langIds.Contains(id))
            .Select(id => (int)(id - 1))  // HF model IDs → SentencePiece IDs (reverse the +1 from Encode)
            .ToList();
        return _tokenizer.Decode(filtered) ?? string.Empty;
    }

    public long GetLanguageTokenId(string languageCode)
    {
        if (_langTokenIds.TryGetValue(languageCode, out var id))
            return id;
        throw new ArgumentException($"Unknown FLORES-200 language code: {languageCode}", nameof(languageCode));
    }

    public void Dispose() { }

    private static IReadOnlyDictionary<string, long> LoadLanguageTokenIds(string configPath)
    {
        if (!File.Exists(configPath))
            return BuiltInLanguageTokenIds;

        // Let parse errors propagate — a malformed tokenizer.json would silently
        // produce wrong language IDs and very hard-to-debug translation failures.
        var parsed = ParseTokenizerJson(configPath);
        return parsed.Count > 0 ? parsed : BuiltInLanguageTokenIds;
    }

    private static IReadOnlyDictionary<string, long> ParseTokenizerJson(string path)
    {
        using var stream = File.OpenRead(path);
        using var doc = JsonDocument.Parse(stream);

        var map = new Dictionary<string, long>();
        var addedTokens = doc.RootElement.GetProperty("added_tokens");

        foreach (var token in addedTokens.EnumerateArray())
        {
            var content = token.GetProperty("content").GetString() ?? string.Empty;
            var id = token.GetProperty("id").GetInt64();

            // Support both __ukr_Cyrl__ (some HF exports) and ukr_Cyrl (forkjoin export)
            string? code = null;
            if (content.StartsWith("__") && content.EndsWith("__") && content.Length > 4)
                code = content[2..^2];
            else if (IsFlores200Code(content))
                code = content;

            if (code != null)
                map[code] = id;
        }
        return map;
    }

    // Matches FLORES-200 format: 3 lowercase letters + '_' + capital + 3 lowercase (e.g. ukr_Cyrl)
    private static bool IsFlores200Code(string s) =>
        s.Length == 8 && char.IsLower(s[0]) && char.IsLower(s[1]) && char.IsLower(s[2])
        && s[3] == '_' && char.IsUpper(s[4]) && char.IsLower(s[5]) && char.IsLower(s[6]) && char.IsLower(s[7]);

    private static readonly IReadOnlyDictionary<string, long> BuiltInLanguageTokenIds =
        new Dictionary<string, long>
        {
            ["ace_Arab"] = 256001, ["ace_Latn"] = 256002, ["acm_Arab"] = 256003, ["acq_Arab"] = 256004,
            ["aeb_Arab"] = 256005, ["afr_Latn"] = 256006, ["ajp_Arab"] = 256007, ["aka_Latn"] = 256008,
            ["amh_Ethi"] = 256009, ["apc_Arab"] = 256010, ["arb_Arab"] = 256011, ["ars_Arab"] = 256012,
            ["ary_Arab"] = 256013, ["arz_Arab"] = 256014, ["asm_Beng"] = 256015, ["ast_Latn"] = 256016,
            ["awa_Deva"] = 256017, ["ayr_Latn"] = 256018, ["azb_Arab"] = 256019, ["azj_Latn"] = 256020,
            ["bak_Cyrl"] = 256021, ["bam_Latn"] = 256022, ["ban_Latn"] = 256023, ["bel_Cyrl"] = 256024,
            ["bem_Latn"] = 256025, ["ben_Beng"] = 256026, ["bho_Deva"] = 256027, ["bjn_Arab"] = 256028,
            ["bjn_Latn"] = 256029, ["bod_Tibt"] = 256030, ["bos_Latn"] = 256031, ["bug_Latn"] = 256032,
            ["bul_Cyrl"] = 256033, ["cat_Latn"] = 256034, ["ceb_Latn"] = 256035, ["ces_Latn"] = 256036,
            ["cjk_Latn"] = 256037, ["ckb_Arab"] = 256038, ["crh_Latn"] = 256039, ["cym_Latn"] = 256040,
            ["dan_Latn"] = 256041, ["deu_Latn"] = 256042, ["dik_Latn"] = 256043, ["dyu_Latn"] = 256044,
            ["dzo_Tibt"] = 256045, ["ell_Grek"] = 256046, ["eng_Latn"] = 256047, ["epo_Latn"] = 256048,
            ["est_Latn"] = 256049, ["eus_Latn"] = 256050, ["ewe_Latn"] = 256051, ["fao_Latn"] = 256052,
            ["pes_Arab"] = 256053, ["fij_Latn"] = 256054, ["fin_Latn"] = 256055, ["fon_Latn"] = 256056,
            ["fra_Latn"] = 256057, ["fur_Latn"] = 256058, ["fuv_Latn"] = 256059, ["gla_Latn"] = 256060,
            ["gle_Latn"] = 256061, ["glg_Latn"] = 256062, ["grn_Latn"] = 256063, ["guj_Gujr"] = 256064,
            ["hat_Latn"] = 256065, ["hau_Latn"] = 256066, ["heb_Hebr"] = 256067, ["hin_Deva"] = 256068,
            ["hne_Deva"] = 256069, ["hrv_Latn"] = 256070, ["hun_Latn"] = 256071, ["hye_Armn"] = 256072,
            ["ibo_Latn"] = 256073, ["ilo_Latn"] = 256074, ["ind_Latn"] = 256075, ["isl_Latn"] = 256076,
            ["ita_Latn"] = 256077, ["jav_Latn"] = 256078, ["jpn_Jpan"] = 256079, ["kab_Latn"] = 256080,
            ["kac_Latn"] = 256081, ["kam_Latn"] = 256082, ["kan_Knda"] = 256083, ["kas_Arab"] = 256084,
            ["kas_Deva"] = 256085, ["kat_Geor"] = 256086, ["knc_Arab"] = 256087, ["knc_Latn"] = 256088,
            ["kaz_Cyrl"] = 256089, ["kbp_Latn"] = 256090, ["kea_Latn"] = 256091, ["khm_Khmr"] = 256092,
            ["kik_Latn"] = 256093, ["kin_Latn"] = 256094, ["kir_Cyrl"] = 256095, ["kmb_Latn"] = 256096,
            ["kon_Latn"] = 256097, ["kor_Hang"] = 256098, ["kmr_Latn"] = 256099, ["lao_Laoo"] = 256100,
            ["lvs_Latn"] = 256101, ["lij_Latn"] = 256102, ["lim_Latn"] = 256103, ["lin_Latn"] = 256104,
            ["lit_Latn"] = 256105, ["lmo_Latn"] = 256106, ["ltg_Latn"] = 256107, ["ltz_Latn"] = 256108,
            ["lua_Latn"] = 256109, ["lug_Latn"] = 256110, ["luo_Latn"] = 256111, ["lus_Latn"] = 256112,
            ["mag_Deva"] = 256113, ["mai_Deva"] = 256114, ["mal_Mlym"] = 256115, ["mar_Deva"] = 256116,
            ["min_Latn"] = 256117, ["mkd_Cyrl"] = 256118, ["plt_Latn"] = 256119, ["mlt_Latn"] = 256120,
            ["mni_Beng"] = 256121, ["khk_Cyrl"] = 256122, ["mos_Latn"] = 256123, ["mri_Latn"] = 256124,
            ["zsm_Latn"] = 256125, ["mya_Mymr"] = 256126, ["nld_Latn"] = 256127, ["nno_Latn"] = 256128,
            ["nob_Latn"] = 256129, ["npi_Deva"] = 256130, ["nso_Latn"] = 256131, ["nus_Latn"] = 256132,
            ["nya_Latn"] = 256133, ["oci_Latn"] = 256134, ["gaz_Latn"] = 256135, ["ory_Orya"] = 256136,
            ["pag_Latn"] = 256137, ["pan_Guru"] = 256138, ["pap_Latn"] = 256139, ["pol_Latn"] = 256140,
            ["por_Latn"] = 256141, ["prs_Arab"] = 256142, ["pbt_Arab"] = 256143, ["quy_Latn"] = 256144,
            ["ron_Latn"] = 256145, ["run_Latn"] = 256146, ["rus_Cyrl"] = 256147, ["sag_Latn"] = 256148,
            ["san_Deva"] = 256149, ["sat_Beng"] = 256150, ["scn_Latn"] = 256151, ["shn_Mymr"] = 256152,
            ["sin_Sinh"] = 256153, ["slk_Latn"] = 256154, ["slv_Latn"] = 256155, ["smo_Latn"] = 256156,
            ["sna_Latn"] = 256157, ["snd_Arab"] = 256158, ["som_Latn"] = 256159, ["sot_Latn"] = 256160,
            ["spa_Latn"] = 256161, ["als_Latn"] = 256162, ["srd_Latn"] = 256163, ["srp_Cyrl"] = 256164,
            ["ssw_Latn"] = 256165, ["sun_Latn"] = 256166, ["swe_Latn"] = 256167, ["swh_Latn"] = 256168,
            ["szl_Latn"] = 256169, ["tam_Taml"] = 256170, ["tat_Cyrl"] = 256171, ["tel_Telu"] = 256172,
            ["tgk_Cyrl"] = 256173, ["tgl_Latn"] = 256174, ["tha_Thai"] = 256175, ["tir_Ethi"] = 256176,
            ["taq_Latn"] = 256177, ["taq_Tfng"] = 256178, ["tpi_Latn"] = 256179, ["tsn_Latn"] = 256180,
            ["tso_Latn"] = 256181, ["tuk_Latn"] = 256182, ["tum_Latn"] = 256183, ["tur_Latn"] = 256184,
            ["twi_Latn"] = 256185, ["tzm_Tfng"] = 256186, ["uig_Arab"] = 256187, ["ukr_Cyrl"] = 256188,
            ["umb_Latn"] = 256189, ["urd_Arab"] = 256190, ["uzn_Latn"] = 256191, ["vec_Latn"] = 256192,
            ["vie_Latn"] = 256193, ["war_Latn"] = 256194, ["wol_Latn"] = 256195, ["xho_Latn"] = 256196,
            ["ydd_Hebr"] = 256197, ["yor_Latn"] = 256198, ["yue_Hant"] = 256199, ["zho_Hans"] = 256200,
            ["zho_Hant"] = 256201, ["zul_Latn"] = 256202,
        };
}
