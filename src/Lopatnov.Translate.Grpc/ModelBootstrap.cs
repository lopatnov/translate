using Lopatnov.Translate.Core.Abstractions;
using Lopatnov.Translate.Core.LanguageDetectors;
using Lopatnov.Translate.Grpc.Services;
using Lopatnov.Translate.LibreTranslate;
using Lopatnov.Translate.M2M100;
using Lopatnov.Translate.Nllb;
using Lopatnov.Translate.Piper;
using Lopatnov.Translate.Whisper;
using Microsoft.Extensions.Options;
using SessionOptions = Microsoft.ML.OnnxRuntime.SessionOptions;

namespace Lopatnov.Translate.Grpc;

/// <summary>
/// Encapsulates factory methods for building ML model registrations.
/// Extracted from Program.cs to keep the top-level file simple and reduce cognitive complexity.
/// </summary>
internal static class ModelBootstrap
{
    internal static string ResolvePath(string path, string contentRootPath) =>
        string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) ? path :
        Path.GetFullPath(Path.Combine(contentRootPath, path));

    internal static ILanguageDetector CreateLanguageDetector(
        IServiceProvider sp,
        string autoDetectName,
        IReadOnlyDictionary<string, ModelConfig> rawModels,
        Func<string, string> resolvePath)
    {
        if (string.IsNullOrWhiteSpace(autoDetectName))
        {
            sp.GetRequiredService<ILogger<Program>>()
              .LogInformation("Translation:AutoDetect is empty — using heuristic language detector");
            return new HeuristicLanguageDetector();
        }

        if (!rawModels.TryGetValue(autoDetectName, out var cfg))
        {
            sp.GetRequiredService<ILogger<Program>>()
              .LogError("Translation:AutoDetect references unknown model '{Name}' — using heuristic language detector",
                  autoDetectName);
            return new HeuristicLanguageDetector();
        }

        var modelPath = resolvePath(cfg.Path);
        var log = sp.GetRequiredService<ILogger<FastTextLanguageDetector>>();

        if (string.IsNullOrWhiteSpace(modelPath) || !File.Exists(modelPath))
        {
            log.LogWarning("LangDetect model '{Name}' not found at {Path} — using heuristic language detector",
                autoDetectName, modelPath);
            return new HeuristicLanguageDetector();
        }

#pragma warning disable CA1873 // arguments are cheap local variables
        log.LogInformation("Loading LangDetect '{Name}' ({Type}) from {Path}",
            autoDetectName, cfg.Type, modelPath);
#pragma warning restore CA1873
        try
        {
            return FastTextLanguageDetector.Load(modelPath, new FastTextLanguageDetectorSettings
            {
                LabelFormat = cfg.LabelFormat?.ToLanguageCodeFormat() ?? LanguageCodeFormat.Flores200,
                LabelPrefix = cfg.LabelPrefix ?? "__label__",
                LabelSuffix = cfg.LabelSuffix ?? string.Empty,
            });
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Failed to load LangDetect '{Name}' — using heuristic language detector",
                autoDetectName);
            return new HeuristicLanguageDetector();
        }
    }

    internal static ISpeechRecognizer CreateSpeechRecognizer(
        IServiceProvider sp,
        string audioToText,
        IReadOnlyDictionary<string, ModelConfig> rawModels,
        Func<string, string> resolvePath)
    {
        if (string.IsNullOrWhiteSpace(audioToText))
        {
            sp.GetRequiredService<ILogger<Program>>()
              .LogInformation("Translation:AudioToText is empty — STT disabled (NullSpeechRecognizer)");
            return new NullSpeechRecognizer();
        }

        if (!rawModels.TryGetValue(audioToText, out var wCfg))
        {
            sp.GetRequiredService<ILogger<Program>>()
              .LogError("Translation:AudioToText references unknown model '{Name}' — STT disabled (NullSpeechRecognizer)",
                  audioToText);
            return new NullSpeechRecognizer();
        }

        var translOpts = sp.GetRequiredService<IOptions<TranslationOptions>>().Value;
        var log        = sp.GetRequiredService<ILogger<WhisperRecognizer>>();
        var modelPath  = resolvePath(wCfg.Path);

#pragma warning disable CA1873 // arguments are cheap local variables
        log.LogInformation(
            "Whisper STT '{Key}': GPU auto-select active (Cuda → Vulkan → CoreML → Cpu). " +
            "Override via Models:[key]:ExecutionProvider in appsettings.json. " +
            "Loading lazily from {Path}.",
            audioToText, modelPath);
#pragma warning restore CA1873

        return new WhisperRecognizer(
            Options.Create(new WhisperOptions
            {
                ModelPath  = modelPath,
                TtlMinutes = translOpts.ModelTtlMinutes,
                Backend    = wCfg.ExecutionProvider,
            }),
            log);
    }

    internal static ISpeechSynthesizer CreateSpeechSynthesizer(
        IServiceProvider sp,
        IReadOnlyDictionary<string, string> textToAudio,
        IReadOnlyDictionary<string, ModelConfig> rawModels,
        Func<string, string> resolvePath)
    {
        if (textToAudio.Count == 0)
        {
            sp.GetRequiredService<ILogger<Program>>()
              .LogInformation("Translation:TextToAudio is empty — TTS disabled (NullSpeechSynthesizer)");
            return new NullSpeechSynthesizer();
        }

        var translOpts = sp.GetRequiredService<IOptions<TranslationOptions>>().Value;
        var log = sp.GetRequiredService<ILogger<PiperSynthesizer>>();

        var voices = new Dictionary<string, PiperSynthesizer>(StringComparer.OrdinalIgnoreCase);

        foreach (var (lang, modelKey) in textToAudio)
        {
            if (!rawModels.TryGetValue(modelKey, out var pCfg))
            {
                log.LogError(
                    "Translation:TextToAudio[{Lang}] references unknown model '{Key}' — skipping",
                    lang, modelKey);
                continue;
            }

            if (!pCfg.Type.Equals(ModelType.Piper, StringComparison.OrdinalIgnoreCase))
            {
                log.LogError(
                    "Translation:TextToAudio[{Lang}] model '{Key}' must have Type=Piper " +
                    "(found '{Type}') — skipping",
                    lang, modelKey, pCfg.Type);
                continue;
            }

            var modelPath = resolvePath(pCfg.Path);

#pragma warning disable CA1873
            log.LogInformation(
                "Registering Piper TTS voice for '{Lang}' (model '{Key}') — will load lazily from {Path}",
                lang, modelKey, modelPath);
#pragma warning restore CA1873

            SessionOptions so = OnnxExecutionProviderHelper.BuildSessionOptions(pCfg.ExecutionProvider, log);
            try
            {
                voices[lang] = new PiperSynthesizer(
                    Options.Create(new PiperOptions
                    {
                        ModelPath  = modelPath,
                        TtlMinutes = translOpts.ModelTtlMinutes,
                    }),
                    log, so);
            }
            catch
            {
                so.Dispose();
                throw;
            }
        }

        if (voices.Count == 0)
        {
            log.LogWarning(
                "No valid Piper voice entries found in Translation:TextToAudio — TTS disabled");
            return new NullSpeechSynthesizer();
        }

        return new MultiVoiceSynthesizer(voices);
    }

    internal static ModelSessionManager BuildSessionManager(
        IServiceProvider sp,
        IReadOnlyDictionary<string, ModelConfig> rawModels,
        Func<string, string> resolvePath)
    {
        var factories = new Dictionary<string, Func<ITextTranslator>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, cfg) in rawModels)
            RegisterFactory(factories, name, cfg, sp, resolvePath);

        var opts = sp.GetRequiredService<IOptions<TranslationOptions>>().Value;
        return new ModelSessionManager(factories, opts.AllowedModels, TimeSpan.FromMinutes(opts.ModelTtlMinutes));
    }

    private static void RegisterFactory(
        Dictionary<string, Func<ITextTranslator>> factories,
        string name, ModelConfig cfg, IServiceProvider sp,
        Func<string, string> resolvePath)
    {
        // Whisper, FastText, and Piper are not ITextTranslator — registered elsewhere.
        if (cfg.Type.Equals(ModelType.Whisper,  StringComparison.OrdinalIgnoreCase) ||
            cfg.Type.Equals(ModelType.FastText, StringComparison.OrdinalIgnoreCase) ||
            cfg.Type.Equals(ModelType.Piper,    StringComparison.OrdinalIgnoreCase))
            return;

        var epLogger = sp.GetService<ILoggerFactory>()?.CreateLogger(nameof(ModelBootstrap));

        if (cfg.Type.Equals(ModelType.NLLB, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(cfg.Path))
        {
            var c = cfg; var n = name;
            factories[n] = () =>
            {
                SessionOptions so = OnnxExecutionProviderHelper.BuildSessionOptions(c.ExecutionProvider, epLogger);
                return new NllbTranslator(new NllbOptions
                {
                    Path = resolvePath(c.Path), EncoderFile = c.EncoderFile, DecoderFile = c.DecoderFile,
                    TokenizerFile = c.TokenizerFile, TokenizerConfigFile = c.TokenizerConfigFile,
                    MaxTokens = c.MaxTokens, BeamSize = c.BeamSize,
                }, null, null, null, so);
            };
        }
        else if (cfg.Type.Equals(ModelType.M2M100, StringComparison.OrdinalIgnoreCase)
                 && !string.IsNullOrWhiteSpace(cfg.Path))
        {
            var c = cfg; var n = name;
            factories[n] = () =>
            {
                SessionOptions so = OnnxExecutionProviderHelper.BuildSessionOptions(c.ExecutionProvider, epLogger);
                return new M2M100Translator(new M2M100Options
                {
                    Path = resolvePath(c.Path), EncoderFile = c.EncoderFile, DecoderFile = c.DecoderFile,
                    TokenizerFile = c.TokenizerFile, TokenizerConfigFile = c.TokenizerConfigFile,
                    MaxTokens = c.MaxTokens, VocabFile = c.VocabFile,
                }, null, null, null, so);
            };
        }
        else if (cfg.Type.Equals(ModelType.LibreTranslate, StringComparison.OrdinalIgnoreCase)
                 && !string.IsNullOrWhiteSpace(cfg.BaseUrl))
        {
            var c = cfg; var n = name;
            var httpFac = sp.GetRequiredService<IHttpClientFactory>();
            factories[n] = () => new LibreTranslateClient(
                httpFac.CreateClient(n),
                Options.Create(new LibreTranslateOptions { BaseUrl = c.BaseUrl, ApiKey = c.ApiKey }));
        }
        else if (cfg.Type.Equals(ModelType.Redirect, StringComparison.OrdinalIgnoreCase)
                 && !string.IsNullOrWhiteSpace(cfg.RedirectUrl))
        {
            var c = cfg; var n = name;
            var detector = sp.GetRequiredService<RedirectCycleDetector>();
            var httpAcc  = sp.GetRequiredService<IHttpContextAccessor>();
            factories[n] = () => new GrpcRedirectTranslator(
                c.RedirectUrl,
                string.IsNullOrWhiteSpace(c.RedirectName) ? n : c.RedirectName,
                detector,
                httpAcc);
        }
    }
}
