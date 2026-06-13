using Grpc.Core;
using Lopatnov.Translate.Core.Abstractions;
using Lopatnov.Translate.Core.LanguageDetectors;
using Lopatnov.Translate.Grpc.Services;
using Lopatnov.Translate.M2M100;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Lopatnov.Translate.Grpc.Tests;

/// <summary>
/// End-to-end integration tests for the BCP-47 language code contract, exercising
/// the full pipeline with REAL models: gRPC service → language format handling →
/// detector (raw native labels) → BCP-47 normalisation → translator adapter →
/// model-native codes → actual inference.
///
/// Requires local models (skipped otherwise):
///   models/translate/m2m100_418M                          — translation
///   models/detect-lang/glotlid/model_v3.bin               — GlotLID v3 detector
///   models/detect-lang/fasttext-language-id/lid.176.bin   — LID-176 detector
///
/// Run: dotnet test --filter "Category=Integration"
/// </summary>
[Trait("Category", "Integration")]
public sealed class TranslateGrpcServiceIntegrationTests : IDisposable
{
    // ── Model paths (resolved from the repo root, same pattern as other suites) ──

    private static readonly string RepoRoot = ResolveRepoRoot();
    private static readonly string M2M100Path = Path.Combine(RepoRoot, "models", "translate", "m2m100_418M");
    private static readonly string GlotlidPath = Path.Combine(RepoRoot, "models", "detect-lang", "glotlid", "model_v3.bin");
    private static readonly string Lid176Path = Path.Combine(RepoRoot, "models", "detect-lang", "fasttext-language-id", "lid.176.bin");

    private static string ResolveRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "translate.slnx")))
                return dir.FullName;
            dir = dir.Parent;
        }
        return ".";
    }

    private static void RequireM2M100()
    {
        if (!Directory.Exists(M2M100Path))
            Assert.Skip($"M2M-100 model not found at '{M2M100Path}'.");
    }

    // ── Service factory with real components ─────────────────────────────────

    private readonly List<IDisposable> _disposables = [];

    private TranslateGrpcService CreateService(ILanguageDetector? detector = null)
    {
        var manager = new ModelSessionManager(
            new Dictionary<string, Func<ITextTranslator>>
            {
                ["m2m100_418M"] = () =>
                {
                    var translator = new M2M100Translator(new M2M100Options
                    {
                        Path = M2M100Path,
                        MaxTokens = 128,
                        TokenizerConfigFile = "added_tokens.json",
                    }, null, null, null);
                    _disposables.Add(translator);
                    return translator;
                },
            },
            allowedModels: [],
            ttl: TimeSpan.FromMinutes(30));
        _disposables.Add(manager);

        return new TranslateGrpcService(
            manager,
            new Lazy<ILanguageDetector>(() => detector ?? new HeuristicLanguageDetector()),
            new Mock<ISpeechRecognizer>().Object,
            new Mock<ISpeechSynthesizer>().Object,
            Options.Create(new TranslationOptions { DefaultModel = "m2m100_418M" }));
    }

    private static ServerCallContext Ctx() => new Mock<ServerCallContext>(MockBehavior.Loose).Object;

    public void Dispose()
    {
        foreach (var d in _disposables) d.Dispose();
    }

    // ── BCP-47 input → real translation ──────────────────────────────────────

    [Fact]
    public async Task TranslateText_Bcp47Codes_TranslatesThroughRealModel()
    {
        RequireM2M100();
        var svc = CreateService();

        var response = await svc.TranslateText(new TranslateTextRequest
        {
            Text = "Привіт, як справи?", SourceLanguage = "uk", TargetLanguage = "en",
        }, Ctx());

        Assert.False(string.IsNullOrWhiteSpace(response.TranslatedText));
        Assert.Contains("hello", response.TranslatedText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TranslateText_Bcp47RegionSubtag_CollapsesAndTranslates()
    {
        RequireM2M100();
        var svc = CreateService();

        // en-US / uk-UA are not in the model vocabulary — the adapter must
        // collapse them to the primary subtags en / uk.
        var response = await svc.TranslateText(new TranslateTextRequest
        {
            Text = "Hello, how are you?", SourceLanguage = "en-US", TargetLanguage = "uk-UA",
        }, Ctx());

        Assert.False(string.IsNullOrWhiteSpace(response.TranslatedText));
    }

    // ── native input → model-native codes pass through ───────────────────────

    [Fact]
    public async Task TranslateText_NativeFormat_AcceptsModelNativeIsoCodes()
    {
        RequireM2M100();
        var svc = CreateService();

        // M2M-100's native codes are ISO 639-1 — with "native" they pass through untouched.
        var response = await svc.TranslateText(new TranslateTextRequest
        {
            Text = "Привіт, як справи?", SourceLanguage = "uk", TargetLanguage = "en",
            LanguageFormat = "native",
        }, Ctx());

        Assert.False(string.IsNullOrWhiteSpace(response.TranslatedText));
    }

    [Fact]
    public async Task TranslateText_Flores200Format_IsRejected()
    {
        RequireM2M100();
        var svc = CreateService();

        var ex = await Assert.ThrowsAsync<RpcException>(() =>
            svc.TranslateText(new TranslateTextRequest
            {
                Text = "hello", SourceLanguage = "eng_Latn", TargetLanguage = "ukr_Cyrl",
                LanguageFormat = "flores200",
            }, Ctx()));

        Assert.Equal(StatusCode.InvalidArgument, ex.StatusCode);
    }

    // ── auto-detect: real detector feeds the real translator ─────────────────
    // This is the critical seam: the detector's native label format must never
    // leak into the translator — normalisation goes through BCP-47.

    [Fact]
    public async Task TranslateText_AutoDetect_GlotLid_DetectorLabelNormalisedForTranslator()
    {
        RequireM2M100();
        if (!File.Exists(GlotlidPath))
            Assert.Skip($"GlotLID model not found at '{GlotlidPath}'.");

        // GlotLID v3 natively emits ISO 639-3 + script ("ukr_Cyrl") — a format the
        // M2M-100 adapter does NOT understand. The service must hand the translator
        // the BCP-47 form ("uk") or the call fails.
        var detector = FastTextLanguageDetector.Load(GlotlidPath, new FastTextLanguageDetectorSettings
        {
            LabelFormat = LanguageCodeFormat.ISO639_3,
            LabelPrefix = "__label__",
        });
        var svc = CreateService(detector);

        var response = await svc.TranslateText(new TranslateTextRequest
        {
            Text = "Сьогодні чудова погода, сонце світить яскраво.",
            SourceLanguage = "auto", TargetLanguage = "en",
        }, Ctx());

        Assert.False(string.IsNullOrWhiteSpace(response.TranslatedText));
        Assert.Equal("uk", response.DetectedLanguage); // BCP-47 in the default format
        Assert.Contains("weather", response.TranslatedText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TranslateText_AutoDetect_GlotLid_NativeFormatReturnsRawLabel()
    {
        RequireM2M100();
        if (!File.Exists(GlotlidPath))
            Assert.Skip($"GlotLID model not found at '{GlotlidPath}'.");

        var detector = FastTextLanguageDetector.Load(GlotlidPath, new FastTextLanguageDetectorSettings
        {
            LabelFormat = LanguageCodeFormat.ISO639_3,
            LabelPrefix = "__label__",
        });
        var svc = CreateService(detector);

        var response = await svc.TranslateText(new TranslateTextRequest
        {
            Text = "Сьогодні чудова погода, сонце світить яскраво.",
            SourceLanguage = "auto", TargetLanguage = "en",
            LanguageFormat = "native",
        }, Ctx());

        // native → the detector's raw GlotLID label, while translation still succeeds
        // because the translator received the BCP-47-normalised code.
        Assert.Equal("ukr_Cyrl", response.DetectedLanguage);
        Assert.False(string.IsNullOrWhiteSpace(response.TranslatedText));
    }

    [Fact]
    public async Task TranslateText_AutoDetect_Lid176_DetectorLabelFeedsTranslator()
    {
        RequireM2M100();
        if (!File.Exists(Lid176Path))
            Assert.Skip($"LID-176 model not found at '{Lid176Path}'.");

        var detector = FastTextLanguageDetector.Load(Lid176Path, new FastTextLanguageDetectorSettings
        {
            LabelFormat = LanguageCodeFormat.ISO639_1,
            LabelPrefix = "__label__",
        });
        var svc = CreateService(detector);

        var response = await svc.TranslateText(new TranslateTextRequest
        {
            Text = "Сегодня прекрасная погода, солнце светит ярко.",
            SourceLanguage = "auto", TargetLanguage = "en",
        }, Ctx());

        Assert.Equal("ru", response.DetectedLanguage);
        Assert.False(string.IsNullOrWhiteSpace(response.TranslatedText));
    }

    [Fact]
    public async Task TranslateText_AutoDetect_HeuristicDetector_WorksEndToEnd()
    {
        RequireM2M100();
        var svc = CreateService(new HeuristicLanguageDetector());

        var response = await svc.TranslateText(new TranslateTextRequest
        {
            Text = "Привіт! Сьогодні гарна погода.", SourceLanguage = "auto", TargetLanguage = "en",
        }, Ctx());

        Assert.Equal("uk", response.DetectedLanguage);
        Assert.False(string.IsNullOrWhiteSpace(response.TranslatedText));
    }

    // ── DetectLanguage: bcp47 vs native against real detectors ────────────────

    [Fact]
    public async Task DetectLanguage_GlotLid_Bcp47AndNativeFormats()
    {
        if (!File.Exists(GlotlidPath))
            Assert.Skip($"GlotLID model not found at '{GlotlidPath}'.");

        var detector = FastTextLanguageDetector.Load(GlotlidPath, new FastTextLanguageDetectorSettings
        {
            LabelFormat = LanguageCodeFormat.ISO639_3,
            LabelPrefix = "__label__",
        });
        var svc = CreateService(detector);

        var bcp47 = await svc.DetectLanguage(
            new DetectLanguageRequest { Text = "Привіт, як справи?" }, Ctx());
        var native = await svc.DetectLanguage(
            new DetectLanguageRequest { Text = "Привіт, як справи?", LanguageFormat = "native" }, Ctx());

        Assert.Equal("uk", bcp47.Language);            // normalised interchange format
        Assert.Equal("ukr_Cyrl", native.Language);     // the model's real raw label
    }

    [Fact]
    public async Task DetectLanguage_Lid176_NativeEqualsIsoLabel()
    {
        if (!File.Exists(Lid176Path))
            Assert.Skip($"LID-176 model not found at '{Lid176Path}'.");

        var detector = FastTextLanguageDetector.Load(Lid176Path, new FastTextLanguageDetectorSettings
        {
            LabelFormat = LanguageCodeFormat.ISO639_1,
            LabelPrefix = "__label__",
        });
        var svc = CreateService(detector);

        var native = await svc.DetectLanguage(
            new DetectLanguageRequest { Text = "Hello, how are you today?", LanguageFormat = "native" }, Ctx());

        Assert.Equal("en", native.Language); // LID-176's raw label is already ISO 639-1
    }
}
