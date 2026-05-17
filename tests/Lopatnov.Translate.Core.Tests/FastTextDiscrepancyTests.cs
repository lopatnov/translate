using Lopatnov.Translate.Core.LanguageDetectors;

namespace Lopatnov.Translate.Core.Tests;

/// <summary>
/// Compares .NET FastTextLanguageDetector output against the reference Python
/// fasttext library (run via WSL: conda activate fasttext).
///
/// Python reference results captured on 2026-05-17 via:
///   wsl bash -ic "conda activate fasttext && python -c 'import fasttext; ...'"
///
/// Run: dotnet test --filter "FastTextDiscrepancy&amp;Category=Integration"
/// </summary>
public sealed class FastTextDiscrepancyTests
{
    // ── Model paths (relative to test output bin/Debug/net10.0/) ─────────────

    private static readonly string Lid176BinPath =
        Path.GetFullPath(@"..\..\..\..\..\models\detect-lang\fasttext-language-id\lid.176.bin");
    private static readonly string Lid176FtzPath =
        Path.GetFullPath(@"..\..\..\..\..\models\detect-lang\fasttext-language-id\lid.176.ftz");
    private static readonly string GlotlidPath =
        Path.GetFullPath(@"..\..\..\..\..\models\detect-lang\glotlid\model_v3.bin");

    // ── Python reference top-1 results ────────────────────────────────────────
    // Format: (inputText, expectedFlores200)
    // lid-176 ISO 639-1 labels are converted to FLORES-200 for comparison.

    private static readonly (string Text, string PyFlores)[] s_lid176 =
    [
        ("Hello, how are you today?",                   "eng_Latn"),
        ("Привет как дела",                             "rus_Cyrl"),
        ("Bonjour le monde",                            "fra_Latn"),
        ("Hola como estas",                             "spa_Latn"),
        ("Сьогодні чудова погода",                      "ukr_Cyrl"),
        ("The quick brown fox jumps over the lazy dog", "eng_Latn"),
    ];

    private static readonly (string Text, string PyFlores)[] s_glotlid =
    [
        ("Hello, how are you today?",                   "eng_Latn"),
        ("Привет как дела",                             "rus_Cyrl"),
        ("Bonjour le monde",                            "fra_Latn"),
        ("Hola como estas",                             "spa_Latn"),
        ("Сьогодні чудова погода",                      "ukr_Cyrl"),
        ("The quick brown fox jumps over the lazy dog", "eng_Latn"),
    ];

    // ── lid.176.bin (loss=hs, full precision) ────────────────────────────────

    [Theory]
    [Trait("Category", "Integration")]
    [MemberData(nameof(Lid176Data))]
    public void Lid176Bin_MatchesPython(string text, string pyFlores)
    {
        if (!File.Exists(Lid176BinPath))
            return; // model not present — treated as passing (informational only)

        var det = FastTextLanguageDetector.Load(Lid176BinPath,
            new FastTextLanguageDetectorSettings
            {
                LabelFormat = LanguageCodeFormat.ISO639_1,
                LabelPrefix = "__label__",
            });

        string got = det.Detect(text).Flores200;
        Assert.True(got == pyFlores,
            $"lid.176.bin DISCREPANCY\n  text   : \"{text}\"\n  .NET   : {got}\n  python : {pyFlores}");
    }

    // ── lid.176.ftz (loss=hs, quantized) ─────────────────────────────────────

    [Theory]
    [Trait("Category", "Integration")]
    [MemberData(nameof(Lid176Data))]
    public void Lid176Ftz_MatchesPython(string text, string pyFlores)
    {
        if (!File.Exists(Lid176FtzPath))
            return;

        var det = FastTextLanguageDetector.Load(Lid176FtzPath,
            new FastTextLanguageDetectorSettings
            {
                LabelFormat = LanguageCodeFormat.ISO639_1,
                LabelPrefix = "__label__",
            });

        string got = det.Detect(text).Flores200;
        Assert.True(got == pyFlores,
            $"lid.176.ftz DISCREPANCY\n  text   : \"{text}\"\n  .NET   : {got}\n  python : {pyFlores}");
    }

    // ── GlotLID v3 (loss=ova, full precision) ────────────────────────────────

    [Theory]
    [Trait("Category", "Integration")]
    [MemberData(nameof(GlotlidData))]
    public void Glotlid_MatchesPython(string text, string pyFlores)
    {
        if (!File.Exists(GlotlidPath))
            return;

        var det = FastTextLanguageDetector.Load(GlotlidPath,
            new FastTextLanguageDetectorSettings
            {
                LabelFormat = LanguageCodeFormat.Flores200,
                LabelPrefix = "__label__",
            });

        string got = det.Detect(text).Flores200;
        Assert.True(got == pyFlores,
            $"GlotLID DISCREPANCY\n  text   : \"{text}\"\n  .NET   : {got}\n  python : {pyFlores}");
    }

    // ── MemberData ────────────────────────────────────────────────────────────

    public static IEnumerable<object[]> Lid176Data =>
        s_lid176.Select(c => new object[] { c.Text, c.PyFlores });

    public static IEnumerable<object[]> GlotlidData =>
        s_glotlid.Select(c => new object[] { c.Text, c.PyFlores });
}
