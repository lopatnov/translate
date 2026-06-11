using Lopatnov.Translate.M2M100;
using Lopatnov.Translate.M2M100.Abstractions;

namespace Lopatnov.Translate.M2M100.Tests;

public sealed class M2M100TokenizerTests
{
    [Fact]
    public void Encode_PrependsMappedLanguageToken()
    {
        var tokenizer = new FakeM2M100Tokenizer();

        var ids = tokenizer.Encode("hello", "en");

        // M2M-100 format: [src_lang_token, tokens..., EOS]
        Assert.Equal(FakeM2M100Tokenizer.EnLangId, ids[0]);
        Assert.Equal(M2M100Tokenizer.EosTokenId, ids[^1]);
    }

    [Fact]
    public void Encode_AcceptsBcp47RegionSubtag()
    {
        var tokenizer = new FakeM2M100Tokenizer();

        // "en-US" collapses to the model's "en" token via the primary subtag.
        var ids = tokenizer.Encode("hello", "en-US");

        Assert.Equal(FakeM2M100Tokenizer.EnLangId, ids[0]);
    }

    [Fact]
    public void Decode_Encode_RoundTrip()
    {
        var tokenizer = new FakeM2M100Tokenizer();
        var original = "hello";

        var ids = tokenizer.Encode(original, "en");
        // Skip lang token and EOS, then decode
        var decoded = tokenizer.Decode(ids.Skip(1).SkipLast(1));

        Assert.Equal(original, decoded);
    }

    [Fact]
    public void GetLanguageTokenId_ThrowsForUnknownCode()
    {
        var tokenizer = new FakeM2M100Tokenizer();

        Assert.Throws<ArgumentException>(() => tokenizer.GetLanguageTokenId("xyz_Unknown"));
    }

    [Fact]
    public void GetLanguageTokenId_Bcp47AndNativeIsoResolveToSameToken()
    {
        var tokenizer = new FakeM2M100Tokenizer();

        // "uk" is both the BCP-47 tag and M2M-100's native ISO 639-1 code;
        // a region-qualified BCP-47 tag must resolve to the same token.
        var native = tokenizer.GetLanguageTokenId("uk");
        var regional = tokenizer.GetLanguageTokenId("uk-UA");

        Assert.Equal(native, regional);
    }
}

/// <summary>
/// Deterministic in-memory tokenizer for unit tests — no model files needed.
/// </summary>
internal sealed class FakeM2M100Tokenizer : IM2M100Tokenizer
{
    public const long EnLangId = 128022L;
    public const long UkLangId = 128094L;
    public const long RuLangId = 128077L;

    private static readonly Dictionary<string, long> IsoIds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["en"] = EnLangId,
        ["uk"] = UkLangId,
        ["ru"] = RuLangId,
    };

    public long[] Encode(string text, string sourceLanguage)
    {
        var langId = GetLanguageTokenId(sourceLanguage);
        // Encode each character as its Unicode code point + 1000 (avoids special token range)
        var charIds = text.Select(c => (long)c + 1000L);
        return new[] { langId }.Concat(charIds).Append(M2M100Tokenizer.EosTokenId).ToArray();
    }

    public string Decode(IEnumerable<long> tokenIds)
    {
        var langIdSet = new HashSet<long>(IsoIds.Values);
        var chars = tokenIds
            .Where(id => id >= 1000L && !langIdSet.Contains(id))
            .Select(id => (char)(id - 1000L));
        return new string(chars.ToArray());
    }

    // Mirrors M2M100Tokenizer.GetLanguageTokenId: exact match, then primary subtag.
    public long GetLanguageTokenId(string languageCode)
    {
        if (IsoIds.TryGetValue(languageCode, out var id))
            return id;
        var dash = languageCode.IndexOf('-');
        if (dash > 0 && IsoIds.TryGetValue(languageCode[..dash], out id))
            return id;
        throw new ArgumentException($"Unknown language code: '{languageCode}'", nameof(languageCode));
    }

    public void Dispose() { }
}
