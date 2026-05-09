using Lopatnov.Translate.Core.LanguageDetectors;

namespace Lopatnov.Translate.Core.Tests;

// ── Skip helpers ─────────────────────────────────────────────────────────────

[AttributeUsage(AttributeTargets.Method)]
internal sealed class IntegrationFactAttribute : FactAttribute
{
    public IntegrationFactAttribute()
    {
        if (!File.Exists(FastTextDetectorFixture.ModelPath))
            Skip = $"GlotLID model not found at '{FastTextDetectorFixture.ModelPath}'";
    }
}

[AttributeUsage(AttributeTargets.Method)]
internal sealed class IntegrationTheoryAttribute : TheoryAttribute
{
    public IntegrationTheoryAttribute()
    {
        if (!File.Exists(FastTextDetectorFixture.ModelPath))
            Skip = $"GlotLID model not found at '{FastTextDetectorFixture.ModelPath}'";
    }
}

[AttributeUsage(AttributeTargets.Method)]
internal sealed class Lid176FactAttribute : FactAttribute
{
    public Lid176FactAttribute()
    {
        if (!File.Exists(FastTextLid176Fixture.ModelPath))
            Skip = $"LID-176 model not found at '{FastTextLid176Fixture.ModelPath}'";
    }
}

[AttributeUsage(AttributeTargets.Method)]
internal sealed class Lid176TheoryAttribute : TheoryAttribute
{
    public Lid176TheoryAttribute()
    {
        if (!File.Exists(FastTextLid176Fixture.ModelPath))
            Skip = $"LID-176 model not found at '{FastTextLid176Fixture.ModelPath}'";
    }
}

// ── Fixtures ──────────────────────────────────────────────────────────────────

public sealed class FastTextDetectorFixture
{
    internal static readonly string ModelPath =
        Environment.GetEnvironmentVariable("TEST_LANGDETECT_MODEL_PATH")
        ?? Path.GetFullPath(
               Path.Combine(AppContext.BaseDirectory, "../../../../../models/detect-lang/glotlid/model_v3.bin"));

    public FastTextLanguageDetector? Detector { get; }

    public FastTextDetectorFixture()
    {
        if (File.Exists(ModelPath))
            Detector = FastTextLanguageDetector.Load(ModelPath);
    }
}

public sealed class FastTextLid176Fixture
{
    internal static readonly string ModelPath =
        Environment.GetEnvironmentVariable("TEST_LID176_MODEL_PATH")
        ?? Path.GetFullPath(
               Path.Combine(AppContext.BaseDirectory, "../../../../../models/detect-lang/fasttext-language-id/lid.176.ftz"));

    public FastTextLanguageDetector? Detector { get; }

    public FastTextLid176Fixture()
    {
        if (File.Exists(ModelPath))
            Detector = FastTextLanguageDetector.Load(ModelPath, new FastTextLanguageDetectorSettings
            {
                LabelFormat = LanguageCodeFormat.ISO639_1,
                LabelPrefix = "__label__",
            });
    }
}

// ── Unit tests (no model required) ───────────────────────────────────────────

public sealed class FastTextLanguageDetectorTests
{
    [Fact]
    public void Load_Throws_InvalidDataException_WhenMagicIsWrong()
    {
        var tmp = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(tmp, BitConverter.GetBytes(0x12345678));
            Assert.Throws<InvalidDataException>(() => FastTextLanguageDetector.Load(tmp));
        }
        finally { File.Delete(tmp); }
    }

    [Fact]
    public void Load_Throws_WhenFileDoesNotExist()
    {
        var missing = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".bin");
        Assert.Throws<FileNotFoundException>(() => FastTextLanguageDetector.Load(missing));
    }
}

// ── Integration tests (require models/langdetect/model_v3.bin) ───────────────

[Trait("Category", "Integration")]
public sealed class FastTextLanguageDetectorIntegrationTests(FastTextDetectorFixture fixture)
    : IClassFixture<FastTextDetectorFixture>
{
    [IntegrationTheory]
    [InlineData("Дослідники відкрили нові зорі у далеких галактиках за допомогою сучасного телескопа.", "ukr_Cyrl", "uk")]
    [InlineData("Исследователи открыли новые звёзды в далёких галактиках с помощью современного телескопа.", "rus_Cyrl", "ru")]
    [InlineData("Das Wetter in Berlin ist heute sehr schön, mit viel Sonnenschein und wenig Wind.", "deu_Latn", "de")]
    [InlineData("Le gouvernement français a annoncé de nouvelles mesures économiques pour soutenir les entreprises.", "fra_Latn", "fr")]
    [InlineData("Natural language processing enables computers to understand and generate human language effectively.", "eng_Latn", "en")]
    [InlineData("مرحباً بكم في بغداد، عاصمة جمهورية العراق وأكبر مدنها وأعرقها.", "arb_Arab", "ar")]
    public void Detect_ReturnsExpectedLanguage(string text, string expectedFlores, string expectedBcp47)
    {
        var result = fixture.Detector!.Detect(text);
        Assert.Equal(expectedFlores, result.NativeCode);
        Assert.Equal(LanguageCodeFormat.Flores200, result.NativeFormat);
        Assert.Equal(expectedBcp47, result.Bcp47);
    }

    [IntegrationTheory]
    [InlineData("北京是中华人民共和国的首都，也是中国的政治、文化和经济中心。", "zh-Hans")]
    public void Detect_Chinese_ReturnsBcp47(string text, string expectedBcp47)
    {
        Assert.Equal(expectedBcp47, fixture.Detector!.Detect(text).Bcp47);
    }

    [IntegrationTheory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null!)]
    public void Detect_ReturnsEnglish_ForEmptyOrWhitespace(string? text)
    {
        var result = fixture.Detector!.Detect(text!);
        Assert.Equal(Language.EnglishLatin, result.NativeCode);
        Assert.Equal("en", result.Bcp47);
    }

    [IntegrationFact]
    public void Detect_IsDeterministic()
    {
        const string text = "Исследователи открыли новые звёзды в далёких галактиках.";
        Assert.Equal(fixture.Detector!.Detect(text).NativeCode, fixture.Detector.Detect(text).NativeCode);
    }
}

// ── LID-176 integration tests (require models/fasttext-language-id/lid.176.ftz) ──

[Trait("Category", "Integration")]
public sealed class FastTextLid176IntegrationTests(FastTextLid176Fixture fixture)
    : IClassFixture<FastTextLid176Fixture>
{
    [Lid176Theory]
    [InlineData("Das Wetter in Berlin ist heute sehr schön, mit viel Sonnenschein und wenig Wind.", "deu_Latn", "de")]
    [InlineData("Le gouvernement français a annoncé de nouvelles mesures économiques pour soutenir les entreprises.", "fra_Latn", "fr")]
    [InlineData("Natural language processing enables computers to understand and generate human language effectively.", "eng_Latn", "en")]
    [InlineData("Исследователи открыли новые звёзды в далёких галактиках с помощью современного телескопа.", "rus_Cyrl", "ru")]
    [InlineData("Дослідники відкрили нові зорі у далеких галактиках за допомогою сучасного телескопа.", "ukr_Cyrl", "uk")]
    [InlineData("Los investigadores descubrieron nuevas estrellas en galaxias lejanas utilizando un telescopio moderno.", "spa_Latn", "es")]
    [InlineData("Gli scienziati hanno scoperto nuove stelle in galassie lontane usando un telescopio moderno.", "ita_Latn", "it")]
    [InlineData("Onderzoekers ontdekten nieuwe sterren in verre sterrenstelsels met behulp van een moderne telescoop.", "nld_Latn", "nl")]
    public void Detect_ReturnsExpectedLanguage_LongText(string text, string expectedFlores, string expectedBcp47)
    {
        var result = fixture.Detector!.Detect(text);
        Assert.Equal(expectedFlores, result.NativeCode);
        Assert.Equal(expectedBcp47, result.Bcp47);
    }

    [Lid176Theory]
    [InlineData("Guten Morgen", "deu_Latn")]
    [InlineData("Bonjour le monde", "fra_Latn")]
    [InlineData("Hello world", "eng_Latn")]
    [InlineData("Привет мир", "rus_Cyrl")]
    public void Detect_ReturnsExpectedNativeCode_ShortPhrase(string text, string expectedFlores)
    {
        Assert.Equal(expectedFlores, fixture.Detector!.Detect(text).NativeCode);
    }

    [Lid176Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null!)]
    public void Detect_ReturnsEnglish_ForEmptyOrWhitespace(string? text)
    {
        Assert.Equal(Language.EnglishLatin, fixture.Detector!.Detect(text!).NativeCode);
    }

    [Lid176Fact]
    public void Detect_IsDeterministic()
    {
        const string text = "Das Wetter in Berlin ist heute sehr schön.";
        Assert.Equal(fixture.Detector!.Detect(text).NativeCode, fixture.Detector.Detect(text).NativeCode);
    }
}
