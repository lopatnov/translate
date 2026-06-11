using Lopatnov.Translate.LibreTranslate;

namespace Lopatnov.Translate.Grpc.Tests;

public sealed class LibreTranslateClientTests
{
    // ToIso receives BCP-47 (the system interchange format) and converts to
    // LibreTranslate's native ISO 639-1 codes.

    [Theory]
    [InlineData("en",      "en")]
    [InlineData("uk",      "uk")]
    [InlineData("ru",      "ru")]
    [InlineData("zh-Hans", "zh")]
    [InlineData("zh-Hant", "zh")]
    [InlineData("zh-CN",   "zh")]
    [InlineData("en-US",   "en")] // region subtag collapses to the primary subtag
    [InlineData("de-DE",   "de")]
    public void ToIso_Bcp47Code_ReturnsIso639_1(string bcp47, string expectedIso)
    {
        Assert.Equal(expectedIso, LibreTranslateClient.ToIso(bcp47));
    }

    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    [InlineData("auto")] // LibreTranslate accepts "auto" natively — must pass through
    public void ToIso_PlainOrUnknownCode_PassesThrough(string code)
    {
        Assert.Equal(code, LibreTranslateClient.ToIso(code));
    }
}
