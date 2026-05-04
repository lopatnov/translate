using Lopatnov.Translate.Core.LanguageDetectors;

namespace Lopatnov.Translate.Core.Tests;

public sealed class HeuristicLanguageDetectorTests
{
    private readonly HeuristicLanguageDetector _detector = new();

    // ── FLORES-200 native output ──────────────────────────────────────────────

    [Theory]
    // Original languages
    [InlineData("Hello, how are you? This is a simple English sentence.", "eng_Latn")]
    [InlineData("Привіт, як справи? Сьогодні гарна погода.",              "ukr_Cyrl")]
    [InlineData("Привет, как дела? Сегодня хорошая погода.",              "rus_Cyrl")]
    [InlineData("Schöne Grüße! Wie geht es Ihnen? Das Wetter ist schön.", "deu_Latn")]
    [InlineData("Bonjour, comment ça va? Il fait beau aujourd'hui.",      "fra_Latn")]
    [InlineData("El niño habla bien español con sus amigos.",              "spa_Latn")]
    [InlineData("Dzień dobry, jak się masz? Wszystko dobrze.",            "pol_Latn")]
    [InlineData("今日はいい天気ですね。東京は大きい都市です。",            "jpn_Jpan")]
    [InlineData("今天天气很好。北京是一个大城市。",                        "zho_Hans")]
    [InlineData("مرحبا كيف حالك؟ أنا بخير شكرا.",                        "arb_Arab")]
    // New script-based languages
    [InlineData("안녕하세요? 서울은 대한민국의 수도입니다.",              "kor_Hang")]
    [InlineData("Καλημέρα! Η Αθήνα είναι η πρωτεύουσα της Ελλάδας.",    "ell_Grek")]
    [InlineData("שלום, מה שלומך? תל אביב היא עיר גדולה.",               "heb_Hebr")]
    // New Latin languages — diacritics
    [InlineData("Bom dia! O Brasil é um país muito grande e bonito.",     "por_Latn")]
    [InlineData("Buongiorno! Così è più facile capire l'italiano.",       "ita_Latn")]
    [InlineData("Bună ziua! România este o țară frumoasă în Europa.",     "ron_Latn")]
    [InlineData("Årets vackraste säsong i Sverige är nog sommaren.",        "swe_Latn")]
    [InlineData("Dobrý den! Češi mají rádi pivo a svíčkovou.",           "ces_Latn")]
    [InlineData("Merhaba! Türkiye güzel bir ülkedir, şehirler çok büyük.", "tur_Latn")]
    [InlineData("Jó reggelt! A főváros Budapest, Győrből is látható.",      "hun_Latn")]
    public void Detect_NativeCode_IsFlores200(string text, string expected)
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
        Assert.Equal(LanguageCodeFormat.None, result.NativeFormat);
        Assert.Equal(Language.EnglishLatin, result.Flores200); // None → default English
        Assert.Equal("en", result.Bcp47);
    }

    [Fact]
    public void Detect_NativeFormat_IsFlores200()
    {
        Assert.Equal(LanguageCodeFormat.Flores200, _detector.Detect("Hello").NativeFormat);
    }

    [Fact]
    public void Detect_DistinguishesUkrainianFromRussian_ViaSpecificChars()
    {
        Assert.Equal(Language.UkrainianCyrillic, _detector.Detect("Їжак їсть").NativeCode);
        Assert.Equal(Language.UkrainianCyrillic, _detector.Detect("Він є студентом").NativeCode);
        Assert.Equal(Language.RussianCyrillic,   _detector.Detect("Медведь в лесу").NativeCode);
    }

    [Fact]
    public void Detect_DistinguishesJapaneseFromChinese_ViaKana()
    {
        Assert.Equal(Language.JapaneseJpan,    _detector.Detect("東京は大きい都市です。").NativeCode);
        Assert.Equal(Language.ChineseSimplified, _detector.Detect("北京是一个大城市。").NativeCode);
    }

    [Fact]
    public void Detect_LongTextUsesOnlyFirstSample()
    {
        var text = new string('А', 300) + new string('A', 1000);
        Assert.Equal(Language.RussianCyrillic, _detector.Detect(text).NativeCode);
    }

    [Fact]
    public void Detect_DistinguishesGermanFromSwedish_ViaEszett()
    {
        // ß is unique to German
        Assert.Equal(Language.GermanLatin, _detector.Detect("Straße und Fußgänger").NativeCode);
        Assert.Equal(Language.SwedishLatin, _detector.Detect("Vi går på gatan i Sverige.").NativeCode);
    }

    [Fact]
    public void Detect_DistinguishesTurkishFromGerman_ViaUniqueChars()
    {
        // ş and ğ are unique to Turkish
        Assert.Equal(Language.TurkishLatin, _detector.Detect("Şu an Türkiye'de güzel hava var.").NativeCode);
    }

    [Fact]
    public void Detect_DistinguishesHungarianViaDoubleAcute()
    {
        // ő and ű are unique to Hungarian
        Assert.Equal(Language.HungarianLatin, _detector.Detect("A főváros Győr közelében található.").NativeCode);
    }
}
