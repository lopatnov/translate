using System.Text.Json;
using Lopatnov.Translate.M2M100.Abstractions;
using Microsoft.ML.Tokenizers;

namespace Lopatnov.Translate.M2M100;

public sealed class M2M100Tokenizer : IM2M100Tokenizer
{
    public const long EosTokenId = 2;
    public const long PadTokenId = 1;
    public const long BosTokenId = 0;
    public const long UnkTokenId = 3; // <unk>

    private const char SentencePiecePrefixChar = '▁'; // ▁ (U+2581) — SentencePiece word-boundary marker

    private readonly SentencePieceTokenizer _tokenizer;
    private readonly Dictionary<string, long> _vocab;        // piece string → HF token ID
    private readonly Dictionary<long, string> _reverseVocab; // HF token ID → piece string
    private readonly Dictionary<string, long> _isoToTokenId; // ISO 639-1 lang code → token ID
    private readonly HashSet<long> _langTokenIds;             // pre-computed set for Decode filtering

    // M2M-100's native language tokens use ISO 639-1-style codes (__en__, __zh__, …),
    // which for most languages equal the BCP-47 primary subtag. This map covers only the
    // BCP-47 tags whose M2M-100 token is NOT simply the primary subtag.
    private static readonly Dictionary<string, string> Bcp47ToM2MCode =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["zh-Hans"] = "zh", ["zh-Hant"] = "zh", // M2M-100 has no separate Traditional Chinese
            ["zh-CN"]   = "zh", ["zh-TW"]   = "zh",
            ["nb"]      = "no", ["nn"]      = "no", // Norwegian variants → macro code used by M2M-100
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
        _langTokenIds = new HashSet<long>(_isoToTokenId.Values); // pre-compute for Decode

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
            result[i + 1] = _vocab.TryGetValue(tokens[i].Value, out var hfId) ? hfId : UnkTokenId;
        result[^1] = EosTokenId;
        return result;
    }

    public string Decode(IEnumerable<long> tokenIds)
    {
        // Regular BPE token IDs are in [4, 128003]; filter out special tokens (0–3) and lang tokens (128004+).
        var pieces = tokenIds
            .Where(id => id >= 4 && !_langTokenIds.Contains(id))
            .Select(id => _reverseVocab.TryGetValue(id, out var p) ? p : string.Empty)
            .Where(p => p.Length > 0);

        // SentencePiece uses ▁ (U+2581) as a word-boundary prefix; replace with a regular space.
        return string.Join(string.Empty, pieces)
            .Replace(SentencePiecePrefixChar, ' ')
            .Trim();
    }

    public long GetLanguageTokenId(string languageCode) =>
        ResolveLanguageTokenId(languageCode, _isoToTokenId);

    /// <summary>
    /// Resolves a BCP-47 tag (the system interchange format) to an M2M-100 language
    /// token ID. Subtags are stripped from the right so the most specific known form
    /// wins, and the alias map is consulted at every step — e.g. "nb-NO" → "nb" →
    /// alias "no" → token. The model's native ISO 639-1-style codes match directly
    /// on the first attempt.
    /// </summary>
    internal static long ResolveLanguageTokenId(
        string languageCode, IReadOnlyDictionary<string, long> tokenIds)
    {
        var tag = languageCode;
        while (true)
        {
            var code = Bcp47ToM2MCode.TryGetValue(tag, out var alias) ? alias : tag;
            if (tokenIds.TryGetValue(code, out var id))
                return id;
            var dash = tag.LastIndexOf('-');
            if (dash <= 0)
                break;
            tag = tag[..dash];
        }

        throw new ArgumentException(
            $"Unknown language code: '{languageCode}'. " +
            "Provide a BCP-47 tag (e.g. 'en') or the model's native ISO 639-1 code.",
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
