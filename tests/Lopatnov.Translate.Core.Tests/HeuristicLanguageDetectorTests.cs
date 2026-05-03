using Lopatnov.Translate.Core.LanguageDetectors;

namespace Lopatnov.Translate.Core.Tests;

public sealed class HeuristicLanguageDetectorTests
{
    private readonly HeuristicLanguageDetector _detector = new();

    [Theory]
    [InlineData("Hello, how are you?",             "eng_Latn")]
    [InlineData("This is a simple English sentence.", "eng_Latn")]
    [InlineData("Привіт, як справи?",              "ukr_Cyrl")]
    [InlineData("Сьогодні гарна погода.",           "ukr_Cyrl")]
    [InlineData("Привет, как дела?",               "rus_Cyrl")]
    [InlineData("Сегодня хорошая погода.",          "rus_Cyrl")]
    [InlineData("Schöne Grüße! Wie geht es Ihnen?",  "deu_Latn")]
    [InlineData("Bonjour, comment ça va?",           "fra_Latn")]
    [InlineData("El niño habla bien español.",       "spa_Latn")]
    [InlineData("Dzień dobry, jak się masz?",       "pol_Latn")]
    [InlineData("今日はいい天気ですね。",              "jpn_Jpan")]
    [InlineData("今天天气很好。",                    "zho_Hans")]
    [InlineData("مرحبا كيف حالك",                  "arb_Arab")]
    public void Detect_ReturnsExpectedLanguage(string text, string expected)
    {
        Assert.Equal(expected, _detector.Detect(text));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null!)]
    public void Detect_ReturnsEnglish_ForEmptyOrWhitespace(string? text)
    {
        Assert.Equal(Language.EnglishLatin, _detector.Detect(text!));
    }

    [Fact]
    public void Detect_DistinguishesUkrainianFromRussian_ViaSpecificChars()
    {
        // "ї" and "є" are uniquely Ukrainian
        Assert.Equal(Language.UkrainianCyrillic, _detector.Detect("Їжак їсть"));
        Assert.Equal(Language.UkrainianCyrillic, _detector.Detect("Він є студентом"));
        // Pure Russian Cyrillic without Ukrainian-specific chars
        Assert.Equal(Language.RussianCyrillic, _detector.Detect("Медведь в лесу"));
    }

    [Fact]
    public void Detect_DistinguishesJapaneseFromChinese_ViaKana()
    {
        Assert.Equal(Language.JapaneseJpan, _detector.Detect("東京は大きい都市です。"));  // hiragana present
        Assert.Equal(Language.ChineseSimplified, _detector.Detect("北京是一个大城市。")); // pure CJK
    }

    [Fact]
    public void Detect_LongTextUsesOnlyFirstSample()
    {
        // First 200 chars are Cyrillic, rest is Latin — should still detect Cyrillic
        var text = new string('А', 200) + new string('A', 1000);
        Assert.Equal(Language.RussianCyrillic, _detector.Detect(text));
    }
}
