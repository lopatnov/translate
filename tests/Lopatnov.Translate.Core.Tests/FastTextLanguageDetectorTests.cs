using Lopatnov.Translate.Core;

namespace Lopatnov.Translate.Core.Tests;

// ── Skip helpers (xUnit 2.x: programmatic skip via attribute constructor) ────

[AttributeUsage(AttributeTargets.Method)]
internal sealed class IntegrationFactAttribute : FactAttribute
{
    public IntegrationFactAttribute()
    {
        if (!File.Exists(FastTextDetectorFixture.ModelPath))
            Skip = $"LangDetect model not found at '{FastTextDetectorFixture.ModelPath}'";
    }
}

[AttributeUsage(AttributeTargets.Method)]
internal sealed class IntegrationTheoryAttribute : TheoryAttribute
{
    public IntegrationTheoryAttribute()
    {
        if (!File.Exists(FastTextDetectorFixture.ModelPath))
            Skip = $"LangDetect model not found at '{FastTextDetectorFixture.ModelPath}'";
    }
}

// ── Fixture (loads model once per class) ─────────────────────────────────────

public sealed class FastTextDetectorFixture
{
    internal static readonly string ModelPath =
        Environment.GetEnvironmentVariable("TEST_LANGDETECT_MODEL_PATH")
        ?? Path.GetFullPath(
               Path.Combine(AppContext.BaseDirectory, "../../../../../models/langdetect/model_v3.bin"));

    public FastTextLanguageDetector? Detector { get; }

    public FastTextDetectorFixture()
    {
        if (File.Exists(ModelPath))
            Detector = FastTextLanguageDetector.Load(ModelPath);
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
    [InlineData("Дослідники відкрили нові зорі у далеких галактиках за допомогою сучасного телескопа.", "ukr_Cyrl")]
    [InlineData("Исследователи открыли новые звёзды в далёких галактиках с помощью современного телескопа.", "rus_Cyrl")]
    [InlineData("Das Wetter in Berlin ist heute sehr schön, mit viel Sonnenschein und wenig Wind.", "deu_Latn")]
    [InlineData("Le gouvernement français a annoncé de nouvelles mesures économiques pour soutenir les entreprises.", "fra_Latn")]
    [InlineData("Natural language processing enables computers to understand and generate human language effectively.", "eng_Latn")]
    [InlineData("北京是中华人民共和国的首都，也是中国的政治、文化和经济中心。", "cmn_Hani")]
    [InlineData("مرحباً بكم في بغداد، عاصمة جمهورية العراق وأكبر مدنها وأعرقها.", "arb_Arab")]
    public void Detect_ReturnsExpectedLanguage(string text, string expected)
    {
        Assert.Equal(expected, fixture.Detector!.Detect(text));
    }

    [IntegrationTheory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null!)]
    public void Detect_ReturnsEnglish_ForEmptyOrWhitespace(string? text)
    {
        Assert.Equal(Language.EnglishLatin, fixture.Detector!.Detect(text!));
    }

    [IntegrationFact]
    public void Detect_IsDeterministic()
    {
        const string text = "Исследователи открыли новые звёзды в далёких галактиках.";
        Assert.Equal(fixture.Detector!.Detect(text), fixture.Detector.Detect(text));
    }
}
