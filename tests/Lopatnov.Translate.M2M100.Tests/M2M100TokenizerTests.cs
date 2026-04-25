using Lopatnov.Translate.M2M100;
using Lopatnov.Translate.M2M100.Abstractions;

namespace Lopatnov.Translate.M2M100.Tests;

public sealed class M2M100TokenizerTests
{
    [Fact]
    public void Encode_PrependsMappedLanguageToken()
    {
        var tokenizer = new FakeM2M100Tokenizer();

        var ids = tokenizer.Encode("hello", "eng_Latn");

        // M2M-100 format: [src_lang_token, tokens..., EOS]
        Assert.Equal(FakeM2M100Tokenizer.EnLangId, ids[0]);
        Assert.Equal(M2M100Tokenizer.EosTokenId, ids[^1]);
    }

    [Fact]
    public void Encode_AcceptsIsoCodes()
    {
        var tokenizer = new FakeM2M100Tokenizer();

        var ids = tokenizer.Encode("hello", "en");

        Assert.Equal(FakeM2M100Tokenizer.EnLangId, ids[0]);
    }

    [Fact]
    public void Decode_Encode_RoundTrip()
    {
        var tokenizer = new FakeM2M100Tokenizer();
        var original = "hello";

        var ids = tokenizer.Encode(original, "eng_Latn");
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
    public void GetLanguageTokenId_MapsFloresToIso()
    {
        var tokenizer = new FakeM2M100Tokenizer();

        var flores = tokenizer.GetLanguageTokenId("ukr_Cyrl");
        var iso = tokenizer.GetLanguageTokenId("uk");

        Assert.Equal(flores, iso);
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

    // Mirrors M2M100Tokenizer.FlorestoIso for the languages used in tests.
    private static readonly Dictionary<string, string> FlorestoIso = new(StringComparer.OrdinalIgnoreCase)
    {
        ["eng_Latn"] = "en",
        ["ukr_Cyrl"] = "uk",
        ["rus_Cyrl"] = "ru",
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

    public long GetLanguageTokenId(string languageCode)
    {
        var isoCode = FlorestoIso.TryGetValue(languageCode, out var iso) ? iso : languageCode;
        if (IsoIds.TryGetValue(isoCode, out var id))
            return id;
        throw new ArgumentException($"Unknown language code: '{languageCode}'", nameof(languageCode));
    }

    public void Dispose() { }
}
