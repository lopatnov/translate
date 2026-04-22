using Lopatnov.Translate.LibreTranslate;

namespace Lopatnov.Translate.Grpc.Tests;

public sealed class LibreTranslateClientTests
{
    [Theory]
    [InlineData("eng_Latn", "en")]
    [InlineData("ukr_Cyrl", "uk")]
    [InlineData("rus_Cyrl", "ru")]
    [InlineData("deu_Latn", "de")]
    [InlineData("fra_Latn", "fr")]
    [InlineData("spa_Latn", "es")]
    [InlineData("pol_Latn", "pl")]
    [InlineData("zho_Hans", "zh")]
    [InlineData("jpn_Jpan", "ja")]
    [InlineData("arb_Arab", "ar")]
    public void ToIso_KnownFloresCode_ReturnsIso639_1(string flores, string expectedIso)
    {
        Assert.Equal(expectedIso, LibreTranslateClient.ToIso(flores));
    }

    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    [InlineData("unknown_code")]
    public void ToIso_UnknownCode_PassesThrough(string code)
    {
        Assert.Equal(code, LibreTranslateClient.ToIso(code));
    }
}
