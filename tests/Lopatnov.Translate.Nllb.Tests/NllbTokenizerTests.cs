using Lopatnov.Translate.Nllb;
using Lopatnov.Translate.Nllb.Abstractions;

namespace Lopatnov.Translate.Nllb.Tests;

public sealed class NllbTokenizerTests
{
    [Fact]
    public void Encode_PrependsFLORES200SourceToken()
    {
        var tokenizer = new FakeNllbTokenizer();

        var ids = tokenizer.Encode("hello world", "eng_Latn");

        // NLLB format: [src_lang_code] X [EOS]
        Assert.Equal(256047L, ids[0]);
        Assert.Equal(NllbTokenizer.EosTokenId, ids[^1]);
    }

    [Fact]
    public void Decode_Encode_RoundTrip()
    {
        var tokenizer = new FakeNllbTokenizer();
        var original = "hello world";

        var ids = tokenizer.Encode(original, "eng_Latn");
        // Skip first token (src lang) and last (EOS), then decode
        var decoded = tokenizer.Decode(ids.Skip(1).SkipLast(1));

        Assert.Equal(original, decoded);
    }
}

/// <summary>
/// Deterministic in-memory tokenizer for unit tests — no model files needed.
/// Encodes each word as its UTF-8 byte sum shifted to a safe token ID range,
/// and decodes back by reversing the mapping.
/// </summary>
internal sealed class FakeNllbTokenizer : INllbTokenizer
{
    private static readonly Dictionary<string, long> LangIds = new()
    {
        ["eng_Latn"] = 256047L,
        ["ukr_Cyrl"] = 256188L,
        ["rus_Cyrl"] = 256147L,
    };

    public long[] Encode(string text, string sourceLanguage)
    {
        var langId = GetLanguageTokenId(sourceLanguage);
        // Encode each character as its Unicode code point + 1000 (avoids special token range)
        // NLLB format: [src_lang_code] X [EOS]
        var charIds = text.Select(c => (long)c + 1000L);
        return new[] { langId }.Concat(charIds).Append(NllbTokenizer.EosTokenId).ToArray();
    }

    public string Decode(IEnumerable<long> tokenIds)
    {
        var langIdSet = new HashSet<long>(LangIds.Values);
        var chars = tokenIds
            .Where(id => id >= 1000 && !langIdSet.Contains(id))
            .Select(id => (char)(id - 1000L));
        return new string(chars.ToArray());
    }

    public long GetLanguageTokenId(string languageCode)
    {
        if (LangIds.TryGetValue(languageCode, out var id))
            return id;
        throw new ArgumentException($"Unknown language code: {languageCode}", nameof(languageCode));
    }

    public void Dispose() { }
}
