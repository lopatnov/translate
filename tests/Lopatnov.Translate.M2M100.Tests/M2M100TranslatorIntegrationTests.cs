using Xunit.Abstractions;

namespace Lopatnov.Translate.M2M100.Tests;

[Trait("Category", "Integration")]
public sealed class M2M100TranslatorIntegrationTests(ITestOutputHelper output)
{
    private static readonly string ModelPath = ResolveModelPath();

    private static string ResolveModelPath()
    {
        var envPath = Environment.GetEnvironmentVariable("Models__m2m100_418M__Path");
        if (!string.IsNullOrEmpty(envPath))
            return envPath;

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "translate.slnx")))
                return Path.Combine(dir.FullName, "models", "translate", "m2m100_418M");
            dir = dir.Parent;
        }
        return Path.Combine("models", "translate", "m2m100_418M");
    }

    // source, srcLang, tgtLang, expected keywords (any one must appear, case-insensitive)
    public static TheoryData<string, string, string, string[]> TranslationCases() => new()
    {
        { "Привіт, як справи?",            "ukr_Cyrl", "eng_Latn", ["hi", "hello", "how are you"] },
        { "Сьогодні гарна погода.",        "ukr_Cyrl", "eng_Latn", ["weather", "today", "nice", "good"] },
        { "Дякую за вашу допомогу.",       "ukr_Cyrl", "eng_Latn", ["thank", "help"] },
        { "Добрый день, чем могу помочь?", "rus_Cyrl", "eng_Latn", ["help", "assist", "good", "day"] },
        { "Спасибо за внимание.",          "rus_Cyrl", "eng_Latn", ["thank", "attention"] },
    };

    [SkippableTheory]
    [MemberData(nameof(TranslationCases))]
    public async Task TranslateAsync_ProducesExpectedEnglishOutput(
        string source, string srcLang, string tgtLang, string[] expectedKeywords)
    {
        Skip.If(!Directory.Exists(ModelPath),
            $"M2M-100 model not found at '{ModelPath}'. " +
            $"Download with: huggingface-cli download lopatnov/m2m100_418M-onnx --local-dir {ModelPath}");

        var options = new M2M100Options
        {
            Path = ModelPath,
            MaxTokens = 128,
            TokenizerConfigFile = "added_tokens.json",
        };
        using var translator = new M2M100Translator(options, null, null, null);

        var result = await translator.TranslateAsync(source, srcLang, tgtLang);
        output.WriteLine($"{source} → {result}");

        Assert.False(string.IsNullOrWhiteSpace(result), $"Empty translation for: {source}");

        var lower = result.ToLowerInvariant();
        Assert.True(
            expectedKeywords.Any(k => lower.Contains(k)),
            $"Translation of \"{source}\" was \"{result}\" — expected one of [{string.Join(", ", expectedKeywords)}]");
    }
}
