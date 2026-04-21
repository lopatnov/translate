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

    public static TheoryData<string, string, string> ReferenceSentences() => new()
    {
        { "Привіт, як справи?", "ukr_Cyrl", "eng_Latn" },
        { "Сьогодні гарна погода.", "ukr_Cyrl", "eng_Latn" },
        { "Дякую за вашу допомогу.", "ukr_Cyrl", "eng_Latn" },
    };

    [Theory]
    [MemberData(nameof(ReferenceSentences))]
    public async Task TranslateAsync_ProducesNonEmptyEnglishOutput(string source, string srcLang, string tgtLang)
    {
        if (!Directory.Exists(ModelPath))
            return;

        var options = new NllbOptions
        {
            Path = ModelPath,
            MaxTokens = 128,
            BeamSize = 1,
        };

        using var translator = new NllbTranslator(options, null, null, null);

        var result = await translator.TranslateAsync(source, srcLang, tgtLang);

        Assert.NotNull(result);
        Assert.False(string.IsNullOrWhiteSpace(result), $"Empty translation for: {source}");
    }
}
