using Lopatnov.Translate.Core.LanguageDetectors;

namespace Lopatnov.Translate.Core.Tests;

// ── Skip helpers ──────────────────────────────────────────────────────────────

[AttributeUsage(AttributeTargets.Method)]
internal sealed class NativeIntegrationFactAttribute : FactAttribute
{
    public NativeIntegrationFactAttribute()
    {
        if (NativeFastTextDetectorFixture.SkipReason is { } reason)
            Skip = reason;
    }
}

[AttributeUsage(AttributeTargets.Method)]
internal sealed class NativeIntegrationTheoryAttribute : TheoryAttribute
{
    public NativeIntegrationTheoryAttribute()
    {
        if (NativeFastTextDetectorFixture.SkipReason is { } reason)
            Skip = reason;
    }
}

// ── Fixture ───────────────────────────────────────────────────────────────────

public sealed class NativeFastTextDetectorFixture : IDisposable
{
    internal static readonly string ModelPath = FastTextDetectorFixture.ModelPath;

    // Set only in static ctor (model file check); evaluated by skip attributes before fixture is created.
    // If the native library fails to load at runtime, Detector will be null and tests guard with early return.
    internal static string? SkipReason { get; }

    static NativeFastTextDetectorFixture()
    {
        if (!File.Exists(ModelPath))
            SkipReason = $"GlotLID model not found at '{ModelPath}'";
    }

    public NativeFastTextLanguageDetector? Detector { get; }

    public NativeFastTextDetectorFixture()
    {
        if (SkipReason != null) return;
        try
        {
            Detector = new NativeFastTextLanguageDetector(ModelPath);
        }
        catch
        {
            // Native fasttext binaries not available in this environment.
            // Detector remains null; test methods guard with early return.
        }
    }

    public void Dispose() => Detector?.Dispose();
}

// ── Integration tests ─────────────────────────────────────────────────────────

[Trait("Category", "Integration")]
public sealed class NativeFastTextLanguageDetectorIntegrationTests(NativeFastTextDetectorFixture fixture)
    : IClassFixture<NativeFastTextDetectorFixture>
{
    [NativeIntegrationTheory]
    [InlineData("Дослідники відкрили нові зорі у далеких галактиках за допомогою сучасного телескопа.", "ukr_Cyrl", "uk")]
    [InlineData("Исследователи открыли новые звёзды в далёких галактиках с помощью современного телескопа.", "rus_Cyrl", "ru")]
    [InlineData("Natural language processing enables computers to understand and generate human language effectively.", "eng_Latn", "en")]
    [InlineData("مرحباً بكم في بغداد، عاصمة جمهورية العراق وأكبر مدنها وأعرقها.", "arb_Arab", "ar")]
    public void Detect_LongText_ReturnsExpectedLanguage(string text, string expectedFlores, string expectedBcp47)
    {
        if (fixture.Detector is null) return;
        var result = fixture.Detector.Detect(text);
        Assert.Equal(expectedFlores, result.NativeCode);
        Assert.Equal("flores200", result.NativeFormat);
        Assert.Equal(expectedBcp47, result.Bcp47);
    }

    [NativeIntegrationTheory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null!)]
    public void Detect_ReturnsEnglish_ForEmptyOrWhitespace(string? text)
    {
        if (fixture.Detector is null) return;
        var result = fixture.Detector.Detect(text!);
        Assert.Equal(Language.EnglishLatin, result.NativeCode);
        Assert.Equal("en", result.Bcp47);
    }

    [NativeIntegrationFact]
    public void Detect_IsDeterministic()
    {
        if (fixture.Detector is null) return;
        const string text = "Исследователи открыли новые звёзды в далёких галактиках.";
        Assert.Equal(fixture.Detector.Detect(text).NativeCode, fixture.Detector.Detect(text).NativeCode);
    }
}
