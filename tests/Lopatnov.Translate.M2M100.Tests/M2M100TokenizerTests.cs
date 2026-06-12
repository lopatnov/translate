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

    [Fact]
    public void GetLanguageTokenId_RegionalAliasResolvesViaProgressiveStripping()
    {
        var tokenizer = new FakeM2M100Tokenizer();

        // "nb-NO" is not in the alias map directly: it must strip to "nb",
        // hit the nb → no alias, and land on the model's "no" token.
        var bokmal = tokenizer.GetLanguageTokenId("nb-NO");

        Assert.Equal(FakeM2M100Tokenizer.NoLangId, bokmal);
    }
}

/// <summary>
/// Direct tests of the production resolver — the exact logic the real tokenizer
/// uses, exercised without model files via an injected token table.
/// </summary>
public sealed class M2M100LanguageTokenResolutionTests
{
    private static readonly Dictionary<string, long> Tokens = new(StringComparer.OrdinalIgnoreCase)
    {
        ["en"] = 1L,
        ["uk"] = 2L,
        ["no"] = 3L,
        ["zh"] = 4L,
    };

    [Theory]
    [InlineData("en",         1L)] // native ISO 639-1 == BCP-47
    [InlineData("en-US",      1L)] // region subtag stripped
    [InlineData("uk-UA",      2L)]
    [InlineData("nb",         3L)] // alias nb → no
    [InlineData("nn",         3L)] // alias nn → no
    [InlineData("nb-NO",      3L)] // strip to nb, then alias → no
    [InlineData("zh-Hans",    4L)] // alias zh-Hans → zh
    [InlineData("zh-Hant",    4L)]
    [InlineData("zh-Hans-SG", 4L)] // strip to zh-Hans, then alias → zh
    public void ResolveLanguageTokenId_ResolvesBcp47Variants(string code, long expected)
    {
        Assert.Equal(expected, M2M100Tokenizer.ResolveLanguageTokenId(code, Tokens));
    }

    [Theory]
    [InlineData("xyz_Unknown")]
    [InlineData("eng_Latn")] // FLORES-200 is no longer accepted by the M2M-100 adapter
    public void ResolveLanguageTokenId_ThrowsForUnknownCode(string code)
    {
        Assert.Throws<ArgumentException>(() => M2M100Tokenizer.ResolveLanguageTokenId(code, Tokens));
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
    public const long NoLangId = 128058L;
    public const long ZhLangId = 128103L;

    private static readonly Dictionary<string, long> IsoIds = new(StringComparer.OrdinalIgnoreCase)
    {
        ["en"] = EnLangId,
        ["uk"] = UkLangId,
        ["ru"] = RuLangId,
        ["no"] = NoLangId,
        ["zh"] = ZhLangId,
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

    // Delegates to the PRODUCTION resolver so the fake can never diverge from the
    // real adapter's language-code behaviour (only the token table is faked).
    public long GetLanguageTokenId(string languageCode) =>
        M2M100Tokenizer.ResolveLanguageTokenId(languageCode, IsoIds);

    public void Dispose() { }
}
