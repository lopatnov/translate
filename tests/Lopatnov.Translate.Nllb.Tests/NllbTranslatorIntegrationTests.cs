namespace Lopatnov.Translate.Nllb.Tests;

[Trait("Category", "Integration")]
public sealed class NllbTranslatorIntegrationTests
{
    private static readonly string ModelPath = ResolveModelPath();

    private static string ResolveModelPath()
    {
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

    [Theory]
    [MemberData(nameof(TranslationCases))]
    public async Task TranslateAsync_ProducesExpectedEnglishOutput(
        string source, string srcLang, string tgtLang, string[] expectedKeywords)
    {
        if (!Directory.Exists(ModelPath))
            return;

        var options = new NllbOptions { Path = ModelPath, MaxTokens = 128, BeamSize = 1 };
        using var translator = new NllbTranslator(options, null, null, null);

        var result = await translator.TranslateAsync(source, srcLang, tgtLang);

        Assert.False(string.IsNullOrWhiteSpace(result), $"Empty translation for: {source}");

        var lower = result.ToLowerInvariant();
        Assert.True(
            expectedKeywords.Any(k => lower.Contains(k)),
            $"Translation of \"{source}\" was \"{result}\" — expected one of [{string.Join(", ", expectedKeywords)}]");
    }
}
