using System.Text.Json;
using Lopatnov.Translate.M2M100.Abstractions;
using Microsoft.ML.Tokenizers;

namespace Lopatnov.Translate.M2M100;

public sealed class M2M100Tokenizer : IM2M100Tokenizer
{
    public const long EosTokenId = 2;
    public const long PadTokenId = 1;
    public const long BosTokenId = 0;

    private const char SentencePiecePrefixChar = '▁'; // ▁ (U+2581) — SentencePiece word-boundary marker

    private readonly SentencePieceTokenizer _tokenizer;
    private readonly Dictionary<string, long> _vocab;        // piece string → HF token ID
    private readonly Dictionary<long, string> _reverseVocab; // HF token ID → piece string
    private readonly Dictionary<string, long> _isoToTokenId; // ISO 639-1 lang code → token ID

    // Maps FLORES-200 codes (used by ITextTranslator callers) to M2M-100 ISO 639-1/language codes.
    // Covers all 100 languages supported by M2M-100, plus zho_Hant → zh fallback
    // (M2M-100 has no separate Traditional Chinese token; zh covers both scripts).
    private static readonly Dictionary<string, string> FlorestoIso =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // European — Latin
            ["eng_Latn"] = "en", ["deu_Latn"] = "de", ["fra_Latn"] = "fr",
            ["spa_Latn"] = "es", ["por_Latn"] = "pt", ["ita_Latn"] = "it",
            ["nld_Latn"] = "nl", ["pol_Latn"] = "pl", ["ces_Latn"] = "cs",
            ["ron_Latn"] = "ro", ["hun_Latn"] = "hu", ["swe_Latn"] = "sv",
            ["dan_Latn"] = "da", ["fin_Latn"] = "fi", ["hrv_Latn"] = "hr",
            ["slk_Latn"] = "sk", ["slv_Latn"] = "sl", ["lit_Latn"] = "lt",
            ["lvs_Latn"] = "lv", ["est_Latn"] = "et", ["nob_Latn"] = "no",
            ["isl_Latn"] = "is", ["ltz_Latn"] = "lb", ["oci_Latn"] = "oc",
            ["cat_Latn"] = "ca", ["glg_Latn"] = "gl", ["ast_Latn"] = "ast",
            ["bre_Latn"] = "br", ["cym_Latn"] = "cy", ["gle_Latn"] = "ga",
            ["gla_Latn"] = "gd", ["als_Latn"] = "sq", ["bos_Latn"] = "bs",
            ["hat_Latn"] = "ht",
            // European — Cyrillic / other scripts
            ["ukr_Cyrl"] = "uk", ["rus_Cyrl"] = "ru", ["bul_Cyrl"] = "bg",
            ["mkd_Cyrl"] = "mk", ["srp_Cyrl"] = "sr", ["bel_Cyrl"] = "be",
            ["bak_Cyrl"] = "ba", ["ell_Grek"] = "el", ["ydd_Hebr"] = "yi",
            // Frisian / other Western European
            ["fry_Latn"] = "fy",
            // East Asian
            ["zho_Hans"] = "zh", ["zho_Hant"] = "zh", // M2M-100 has no separate Traditional Chinese
            ["jpn_Jpan"] = "ja", ["kor_Hang"] = "ko",
            // South / Southeast Asian
            ["hin_Deva"] = "hi", ["ben_Beng"] = "bn", ["mar_Deva"] = "mr",
            ["npi_Deva"] = "ne", ["guj_Gujr"] = "gu", ["pan_Guru"] = "pa",
            ["kan_Knda"] = "kn", ["mal_Mlym"] = "ml", ["tam_Taml"] = "ta",
            ["ory_Orya"] = "or", ["sin_Sinh"] = "si", ["urd_Arab"] = "ur",
            ["snd_Arab"] = "sd",
            // Southeast Asian
            ["tha_Thai"] = "th", ["vie_Latn"] = "vi", ["khm_Khmr"] = "km",
            ["lao_Laoo"] = "lo", ["mya_Mymr"] = "my", ["ind_Latn"] = "id",
            ["zsm_Latn"] = "ms", ["tgl_Latn"] = "tl", ["jav_Latn"] = "jv",
            ["sun_Latn"] = "su", ["ceb_Latn"] = "ceb", ["ilo_Latn"] = "ilo",
            // Central / West Asian
            ["tur_Latn"] = "tr", ["azj_Latn"] = "az", ["kaz_Cyrl"] = "kk",
            ["uzn_Latn"] = "uz", ["khk_Cyrl"] = "mn", ["hye_Armn"] = "hy",
            ["kat_Geor"] = "ka", ["pes_Arab"] = "fa", ["pbt_Arab"] = "ps",
            // Middle Eastern / Semitic
            ["arb_Arab"] = "ar", ["heb_Hebr"] = "he",
            // African
            ["afr_Latn"] = "af", ["amh_Ethi"] = "am", ["hau_Latn"] = "ha",
            ["ibo_Latn"] = "ig", ["yor_Latn"] = "yo", ["swh_Latn"] = "sw",
            ["som_Latn"] = "so", ["fuv_Latn"] = "ff", ["wol_Latn"] = "wo",
            ["lug_Latn"] = "lg", ["lin_Latn"] = "ln", ["plt_Latn"] = "mg",
            ["nso_Latn"] = "ns", ["tsn_Latn"] = "tn", ["ssw_Latn"] = "ss",
            ["xho_Latn"] = "xh", ["zul_Latn"] = "zu",
        };

    public M2M100Tokenizer(string modelDir, string tokenizerFile = "sentencepiece.bpe.model",
        string configFile = "added_tokens.json", string vocabFile = "vocab.json")
    {
        var modelPath = Path.Combine(modelDir, tokenizerFile);
        using var stream = File.OpenRead(modelPath);
        _tokenizer = SentencePieceTokenizer.Create(stream, addBeginningOfSentence: false,
            addEndOfSentence: false, specialTokens: null);

        var configPath = Path.Combine(modelDir, configFile);
        _isoToTokenId = LoadLanguageTokenIds(configPath);

        var vocabPath = Path.Combine(modelDir, vocabFile);
        (_vocab, _reverseVocab) = LoadVocabJson(vocabPath);
    }

    public long[] Encode(string text, string sourceLanguage)
    {
        var langId = GetLanguageTokenId(sourceLanguage);

        // M2M-100 uses vocab.json as the authoritative piece→ID mapping (same as HuggingFace
        // M2M100Tokenizer in Python). SP IDs from EncodeToIds cannot be used directly because
        // HuggingFace's ID numbering diverges from the raw SP model's internal IDs.
        var tokens = _tokenizer.EncodeToTokens(text, out _,
            addBeginningOfSentence: false, addEndOfSentence: false,
            considerPreTokenization: true, considerNormalization: true);

        // M2M-100 encoder input format: [src_lang_token] BPE_tokens... [EOS]
        var result = new long[tokens.Count + 2];
        result[0] = langId;
        for (var i = 0; i < tokens.Count; i++)
            result[i + 1] = _vocab.TryGetValue(tokens[i].Value, out var hfId) ? hfId : 3L; // 3 = <unk>
        result[^1] = EosTokenId;
        return result;
    }

    public string Decode(IEnumerable<long> tokenIds)
    {
        var langIds = new HashSet<long>(_isoToTokenId.Values);

        // Regular BPE token IDs are in [4, 128003]; filter out special tokens (0–3) and lang tokens (128004+).
        var pieces = tokenIds
            .Where(id => id >= 4 && !langIds.Contains(id))
            .Select(id => _reverseVocab.TryGetValue(id, out var p) ? p : string.Empty)
            .Where(p => p.Length > 0);

        // SentencePiece uses ▁ (U+2581) as a word-boundary prefix; replace with a regular space.
        return string.Join(string.Empty, pieces)
            .Replace(SentencePiecePrefixChar, ' ')
            .Trim();
    }

    public long GetLanguageTokenId(string languageCode)
    {
        // Accept both FLORES-200 (eng_Latn) and ISO 639-1 (en) codes.
        var isoCode = FlorestoIso.TryGetValue(languageCode, out var iso) ? iso : languageCode;
        if (_isoToTokenId.TryGetValue(isoCode, out var id))
            return id;
        throw new ArgumentException(
            $"Unknown language code: '{languageCode}'. " +
            "Provide a FLORES-200 code (e.g. 'eng_Latn') or an ISO 639-1 code (e.g. 'en').",
            nameof(languageCode));
    }

    public void Dispose() { }

    private static (Dictionary<string, long>, Dictionary<long, string>) LoadVocabJson(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"vocab.json not found at '{path}'. " +
                "M2M-100 requires vocab.json for piece→HF-ID mapping.", path);

        using var stream = File.OpenRead(path);
        using var doc = JsonDocument.Parse(stream);
        var vocab = new Dictionary<string, long>();
        var reverse = new Dictionary<long, string>();

        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            var id = prop.Value.GetInt64();
            vocab[prop.Name] = id;
            reverse[id] = prop.Name;
        }

        return (vocab, reverse);
    }

    private static Dictionary<string, long> LoadLanguageTokenIds(string configPath)
    {
        if (!File.Exists(configPath))
            throw new FileNotFoundException(
                $"Language token config not found at '{configPath}'. " +
                "M2M-100 language token IDs must be loaded from added_tokens.json.", configPath);

        var parsed = ParseLangConfig(configPath);
        if (parsed.Count == 0)
            throw new InvalidOperationException(
                $"'{configPath}' exists but contains no M2M-100 language token entries. " +
                "Verify its 'added_tokens' array contains entries like \"__en__\".");
        return parsed;
    }

    private static Dictionary<string, long> ParseLangConfig(string path)
    {
        using var stream = File.OpenRead(path);
        using var doc = JsonDocument.Parse(stream);

        var map = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var root = doc.RootElement;

        if (root.ValueKind == JsonValueKind.Object
            && root.TryGetProperty("added_tokens", out var addedTokensArr)
            && addedTokensArr.ValueKind == JsonValueKind.Array)
        {
            // tokenizer.json format: {"added_tokens": [{"content": "__en__", "id": 128022}, ...]}
            foreach (var token in addedTokensArr.EnumerateArray())
            {
                var content = token.GetProperty("content").GetString() ?? string.Empty;
                var id = token.GetProperty("id").GetInt64();
                if (TryExtractLangCode(content, out var code))
                    map[code!] = id;
            }
        }
        else if (root.ValueKind == JsonValueKind.Object)
        {
            // added_tokens.json format: {"__en__": 128022, "__uk__": 128094, ...}
            foreach (var prop in root.EnumerateObject())
            {
                if (TryExtractLangCode(prop.Name, out var code))
                    map[code!] = prop.Value.GetInt64();
            }
        }

        return map;
    }

    private static bool TryExtractLangCode(string content, out string? code)
    {
        if (content.StartsWith("__") && content.EndsWith("__") && content.Length is > 4 and <= 10)
        {
            code = content[2..^2];
            return true;
        }
        code = null;
        return false;
    }
}
