using Lopatnov.Translate.Core.LanguageDetectors;

namespace Lopatnov.Translate.Core.Tests;

public sealed class HeuristicLanguageDetectorTests
{
    private readonly HeuristicLanguageDetector _detector = new();

    // ── Native output (the heuristic detector's native format is BCP-47) ─────

    [Theory]
    // Original languages
    [InlineData("Hello, how are you? This is a simple English sentence.", "en")]
    [InlineData("Привіт, як справи? Сьогодні гарна погода.",              "uk")]
    [InlineData("Привет, как дела? Сегодня хорошая погода.",              "ru")]
    [InlineData("Schöne Grüße! Wie geht es Ihnen? Das Wetter ist schön.", "de")]
    [InlineData("Bonjour, comment ça va? Il fait beau aujourd'hui.",      "fr")]
    [InlineData("El niño habla bien español con sus amigos.",              "es")]
    [InlineData("Dzień dobry, jak się masz? Wszystko dobrze.",            "pl")]
    [InlineData("今日はいい天気ですね。東京は大きい都市です。",            "ja")]
    [InlineData("今天天气很好。北京是一个大城市。",                        "zh-Hans")]
    [InlineData("مرحبا كيف حالك؟ أنا بخير شكرا.",                        "ar")]
    // New script-based languages
    [InlineData("안녕하세요? 서울은 대한민국의 수도입니다.",              "ko")]
    [InlineData("Καλημέρα! Η Αθήνα είναι η πρωτεύουσα της Ελλάδας.",    "el")]
    [InlineData("שלום, מה שלומך? תל אביב היא עיר גדולה.",               "he")]
    // New Latin languages — diacritics
    [InlineData("Bom dia! O Brasil é um país muito grande e bonito.",     "pt")]
    [InlineData("Buongiorno! Così è più facile capire l'italiano.",       "it")]
    [InlineData("Bună ziua! România este o țară frumoasă în Europa.",     "ro")]
    [InlineData("Årets vackraste säsong i Sverige är nog sommaren.",        "sv")]
    [InlineData("Dobrý den! Češi mají rádi pivo a svíčkovou.",           "cs")]
    [InlineData("Merhaba! Türkiye güzel bir ülkedir, şehirler çok büyük.", "tr")]
    [InlineData("Jó reggelt! A főváros Budapest, Győrből is látható.",      "hu")]
    public void Detect_NativeCode_IsBcp47(string text, string expected)
    {
        Assert.Equal(expected, _detector.Detect(text).NativeCode);
    }

    // ── BCP-47 output ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Hello, how are you?",                        "en")]
    [InlineData("Привіт, як справи?",                         "uk")]
    [InlineData("Привет, как дела?",                          "ru")]
    [InlineData("Schöne Grüße! Wie geht es Ihnen?",           "de")]
    [InlineData("Bonjour, comment ça va?",                    "fr")]
    [InlineData("El niño habla bien español.",                 "es")]
    [InlineData("Dzień dobry, jak się masz?",                 "pl")]
    [InlineData("今日はいい天気ですね。",                      "ja")]
    [InlineData("今天天气很好。",                              "zh-Hans")]
    [InlineData("مرحبا كيف حالك",                             "ar")]
    [InlineData("안녕하세요?",                                "ko")]
    [InlineData("Καλημέρα!",                                  "el")]
    [InlineData("שלום",                                       "he")]
    [InlineData("O Brasil é um país bonito.",                  "pt")]
    [InlineData("România este o țară frumoasă.",               "ro")]
    [InlineData("Vi går på gatan i Sverige.",                   "sv")]
    [InlineData("Češi mají rádi pivo.",                       "cs")]
    [InlineData("Türkiye güzel, şehirler büyük.",              "tr")]
    [InlineData("A főváros Budapest, Győrből is látható.",      "hu")]
    public void Detect_Bcp47_IsCorrect(string text, string expectedBcp47)
    {
        Assert.Equal(expectedBcp47, _detector.Detect(text).Bcp47);
    }

    // ── Edge cases ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null!)]
    public void Detect_ReturnsEnglish_ForEmptyOrWhitespace(string? text)
    {
        var result = _detector.Detect(text!);
        Assert.Equal(LanguageCodeFormat.Bcp47, result.NativeFormat);
        Assert.Equal(Language.English, result.NativeCode);
        Assert.Equal("en", result.Bcp47);
    }

    [Fact]
    public void Detect_NativeFormat_IsBcp47()
    {
        Assert.Equal(LanguageCodeFormat.Bcp47, _detector.Detect("Hello").NativeFormat);
    }

    [Fact]
    public void Detect_DistinguishesUkrainianFromRussian_ViaSpecificChars()
    {
        Assert.Equal(Language.Ukrainian, _detector.Detect("Їжак їсть").NativeCode);
        Assert.Equal(Language.Ukrainian, _detector.Detect("Він є студентом").NativeCode);
        Assert.Equal(Language.Russian,   _detector.Detect("Медведь в лесу").NativeCode);
    }

    [Fact]
    public void Detect_DistinguishesJapaneseFromChinese_ViaKana()
    {
        Assert.Equal(Language.Japanese,    _detector.Detect("東京は大きい都市です。").NativeCode);
        Assert.Equal(Language.ChineseSimplified, _detector.Detect("北京是一个大城市。").NativeCode);
    }

    [Fact]
    public void Detect_LongTextUsesOnlyFirstSample()
    {
        var text = new string('А', 300) + new string('A', 1000);
        Assert.Equal(Language.Russian, _detector.Detect(text).NativeCode);
    }

    [Fact]
    public void Detect_DistinguishesGermanFromSwedish_ViaEszett()
    {
        // ß is unique to German
        Assert.Equal(Language.German, _detector.Detect("Straße und Fußgänger").NativeCode);
        Assert.Equal(Language.Swedish, _detector.Detect("Vi går på gatan i Sverige.").NativeCode);
    }

    [Fact]
    public void Detect_DistinguishesTurkishFromGerman_ViaUniqueChars()
    {
        // ş and ğ are unique to Turkish
        Assert.Equal(Language.Turkish, _detector.Detect("Şu an Türkiye'de güzel hava var.").NativeCode);
    }

    [Fact]
    public void Detect_DistinguishesHungarianViaDoubleAcute()
    {
        // ő and ű are unique to Hungarian
        Assert.Equal(Language.Hungarian, _detector.Detect("A főváros Győr közelében található.").NativeCode);
    }
}
