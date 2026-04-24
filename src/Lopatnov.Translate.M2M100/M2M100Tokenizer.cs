using System.Text.Json;
using Lopatnov.Translate.M2M100.Abstractions;
using Microsoft.ML.Tokenizers;

namespace Lopatnov.Translate.M2M100;

public sealed class M2M100Tokenizer : IM2M100Tokenizer
{
    public const long EosTokenId = 2;
    public const long PadTokenId = 1;
    public const long BosTokenId = 0;

    private readonly SentencePieceTokenizer _tokenizer;
    private readonly IReadOnlyDictionary<string, long> _isoToTokenId;
    private readonly int _spOffset;

    // Maps FLORES-200 codes used by ITextTranslator callers to M2M-100 ISO 639-1 codes.
    private static readonly IReadOnlyDictionary<string, string> FlorestoIso =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["eng_Latn"] = "en", ["ukr_Cyrl"] = "uk", ["rus_Cyrl"] = "ru",
            ["deu_Latn"] = "de", ["fra_Latn"] = "fr", ["spa_Latn"] = "es",
            ["pol_Latn"] = "pl", ["zho_Hans"] = "zh", ["jpn_Jpan"] = "ja",
            ["arb_Arab"] = "ar", ["por_Latn"] = "pt", ["ita_Latn"] = "it",
            ["nld_Latn"] = "nl", ["kor_Hang"] = "ko", ["hin_Deva"] = "hi",
            ["tur_Latn"] = "tr", ["vie_Latn"] = "vi", ["tha_Thai"] = "th",
            ["swe_Latn"] = "sv", ["dan_Latn"] = "da", ["fin_Latn"] = "fi",
            ["ces_Latn"] = "cs", ["ron_Latn"] = "ro", ["hun_Latn"] = "hu",
            ["bul_Cyrl"] = "bg", ["hrv_Latn"] = "hr", ["slk_Latn"] = "sk",
            ["slv_Latn"] = "sl", ["lit_Latn"] = "lt", ["lvs_Latn"] = "lv",
            ["est_Latn"] = "et",
        };

    public M2M100Tokenizer(string modelDir, string tokenizerFile = "sentencepiece.bpe.model",
        string configFile = "tokenizer.json", int sentencePieceOffset = 4)
    {
        _spOffset = sentencePieceOffset;

        var modelPath = System.IO.Path.Combine(modelDir, tokenizerFile);
        using var stream = File.OpenRead(modelPath);
        _tokenizer = SentencePieceTokenizer.Create(stream, addBeginningOfSentence: false,
            addEndOfSentence: false, specialTokens: null);

        var configPath = System.IO.Path.Combine(modelDir, configFile);
        _isoToTokenId = LoadLanguageTokenIds(configPath);
    }

    public long[] Encode(string text, string sourceLanguage)
    {
        var langId = GetLanguageTokenId(sourceLanguage);
        var tokenIds = _tokenizer.EncodeToIds(text, considerPreTokenization: true, considerNormalization: true);

        // M2M-100 encoder format: [src_lang_token] tokens [EOS]
        // SentencePiece IDs need +_spOffset because HuggingFace M2M-100 reserves
        // IDs 0-3 for BOS/PAD/EOS/UNK before the SP vocabulary starts.
        var result = new long[tokenIds.Count + 2];
        result[0] = langId;
        for (var i = 0; i < tokenIds.Count; i++)
            result[i + 1] = tokenIds[i] + _spOffset;
        result[^1] = EosTokenId;
        return result;
    }

    public string Decode(IEnumerable<long> tokenIds)
    {
        var langIds = new HashSet<long>(_isoToTokenId.Values);
        var filtered = tokenIds
            .Where(id => id >= _spOffset && !langIds.Contains(id))
            .Select(id => (int)(id - _spOffset))
            .ToList();
        return _tokenizer.Decode(filtered) ?? string.Empty;
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

    private static IReadOnlyDictionary<string, long> LoadLanguageTokenIds(string configPath)
    {
        if (!File.Exists(configPath))
            throw new FileNotFoundException(
                $"tokenizer.json not found at '{configPath}'. " +
                "M2M-100 language token IDs must be loaded from the model's tokenizer.json.", configPath);

        var parsed = ParseTokenizerJson(configPath);
        if (parsed.Count == 0)
            throw new InvalidOperationException(
                $"'{configPath}' exists but contains no M2M-100 language token entries. " +
                "Verify its 'added_tokens' array contains entries like \"__en__\".");
        return parsed;
    }

    private static Dictionary<string, long> ParseTokenizerJson(string path)
    {
        using var stream = File.OpenRead(path);
        using var doc = JsonDocument.Parse(stream);

        var map = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var addedTokens = doc.RootElement.GetProperty("added_tokens");

        foreach (var token in addedTokens.EnumerateArray())
        {
            var content = token.GetProperty("content").GetString() ?? string.Empty;
            var id = token.GetProperty("id").GetInt64();

            // M2M-100 language tokens are formatted as "__en__", "__uk__", etc.
            string? code = null;
            if (content.StartsWith("__") && content.EndsWith("__") && content.Length is > 4 and <= 10)
                code = content[2..^2];

            if (code != null)
                map[code] = id;
        }
        return map;
    }
}
