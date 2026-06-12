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

    // Delegates to the PRODUCTION resolver so the fake can never diverge from the
    // real adapter's language-code behaviour (only the token table is faked).
    public long GetLanguageTokenId(string languageCode) =>
        NllbTokenizer.ResolveLanguageTokenId(languageCode, LangIds);

    public void Dispose() { }
}

/// <summary>
/// Direct tests of the production resolver — the exact logic the real tokenizer
/// uses, exercised without model files via an injected token table.
/// </summary>
public sealed class NllbLanguageTokenResolutionTests
{
    private static readonly Dictionary<string, long> Tokens = new()
    {
        ["eng_Latn"] = 256047L,
        ["ukr_Cyrl"] = 256188L,
        ["zho_Hant"] = 256202L,
    };

    [Theory]
    [InlineData("en",         256047L)] // BCP-47 → FLORES-200 via the converter
    [InlineData("en-US",      256047L)] // region subtag collapses
    [InlineData("eng_Latn",   256047L)] // native FLORES-200 passes through
    [InlineData("uk",         256188L)]
    [InlineData("ukr_Cyrl",   256188L)]
    [InlineData("zh-Hant",    256202L)] // Traditional Chinese script preserved
    [InlineData("zh-Hant-HK", 256202L)] // most specific known tag wins, script intact
    public void ResolveLanguageTokenId_ResolvesBcp47AndNativeCodes(string code, long expected)
    {
        Assert.Equal(expected, NllbTokenizer.ResolveLanguageTokenId(code, Tokens));
    }

    [Fact]
    public void ResolveLanguageTokenId_ThrowsForUnknownCode()
    {
        Assert.Throws<ArgumentException>(() => NllbTokenizer.ResolveLanguageTokenId("xx-Unknown", Tokens));
    }
}
