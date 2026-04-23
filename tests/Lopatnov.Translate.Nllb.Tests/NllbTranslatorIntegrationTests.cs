using Xunit.Abstractions;

namespace Lopatnov.Translate.Nllb.Tests;

[Trait("Category", "Integration")]
public sealed class NllbTranslatorIntegrationTests(ITestOutputHelper output)
{
    private static readonly string ModelPath = ResolveModelPath();

    private static string ResolveModelPath()
    {
        // Production resolves NllbOptions.Path relative to the app's content root.
        // Tests run from bin/Release/net10.0/, so we traverse up to find the solution
        // root (translate.slnx) and mirror the production default of "models/nllb".
        var envPath = Environment.GetEnvironmentVariable("Models__Nllb__Path");
        if (!string.IsNullOrEmpty(envPath))
            return envPath;

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "translate.slnx")))
                return Path.Combine(dir.FullName, "models", "nllb");
            dir = dir.Parent;
        }
        return Path.Combine("models", "nllb");
    }

    // source, srcLang, tgtLang, expected keywords (any one must appear, case-insensitive)
    public static TheoryData<string, string, string, string[]> TranslationCases() => new()
    {
        { "Привіт, як справи?",        "ukr_Cyrl", "eng_Latn", ["hi", "hello", "how are you"] },
        { "Сьогодні гарна погода.",    "ukr_Cyrl", "eng_Latn", ["weather", "today", "nice", "good"] },
        { "Дякую за вашу допомогу.",   "ukr_Cyrl", "eng_Latn", ["thank", "help"] },
        { "Добрый день, чем могу помочь?", "rus_Cyrl", "eng_Latn", ["help", "assist", "good", "day"] },
        { "Спасибо за внимание.",       "rus_Cyrl", "eng_Latn", ["thank", "attention"] },
    };

    [SkippableTheory]
    [MemberData(nameof(TranslationCases))]
    public async Task TranslateAsync_ProducesExpectedEnglishOutput(
        string source, string srcLang, string tgtLang, string[] expectedKeywords)
    {
        Skip.If(!Directory.Exists(ModelPath), $"NLLB model not found at '{ModelPath}'. Run scripts/download-models.ps1.");

        var options = new NllbOptions { Path = ModelPath, MaxTokens = 128, BeamSize = 1 };
        using var translator = new NllbTranslator(options, null, null, null);

        var result = await translator.TranslateAsync(source, srcLang, tgtLang);
        output.WriteLine($"{source} → {result}");

        Assert.False(string.IsNullOrWhiteSpace(result), $"Empty translation for: {source}");

        var lower = result.ToLowerInvariant();
        Assert.True(
            expectedKeywords.Any(k => lower.Contains(k)),
            $"Translation of \"{source}\" was \"{result}\" — expected one of [{string.Join(", ", expectedKeywords)}]");
    }
}
