using Lopatnov.Translate.Core.Abstractions;
using Lopatnov.Translate.Core.LanguageDetectors;
using Lopatnov.Translate.Grpc.Services;
using Lopatnov.Translate.LibreTranslate;
using Lopatnov.Translate.M2M100;
using Lopatnov.Translate.Nllb;
using Lopatnov.Translate.Whisper;
using Microsoft.Extensions.Options;

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
        if (string.IsNullOrWhiteSpace(audioToText) ||
            !rawModels.TryGetValue(audioToText, out var wCfg))
        {
            sp.GetRequiredService<ILogger<Program>>()
              .LogInformation("Translation:AudioToText is empty — STT disabled (NullSpeechRecognizer)");
            return new NullSpeechRecognizer();
        }

        var translOpts = sp.GetRequiredService<IOptions<TranslationOptions>>().Value;
        var log        = sp.GetRequiredService<ILogger<WhisperRecognizer>>();
        var modelPath  = resolvePath(wCfg.Path);

#pragma warning disable CA1873 // arguments are cheap local variables
        log.LogInformation("Registering Whisper STT model '{Key}' — will load lazily from {Path}",
            audioToText, modelPath);
#pragma warning restore CA1873

        return new WhisperRecognizer(
            Options.Create(new WhisperOptions
            {
                ModelPath  = modelPath,
                TtlMinutes = translOpts.ModelTtlMinutes,
            }),
            log);
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
        // Whisper and FastText are not ITextTranslator — registered elsewhere.
        if (cfg.Type.Equals(ModelType.Whisper,  StringComparison.OrdinalIgnoreCase) ||
            cfg.Type.Equals(ModelType.FastText, StringComparison.OrdinalIgnoreCase))
            return;

        if (cfg.Type.Equals(ModelType.NLLB, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(cfg.Path))
        {
            var c = cfg; var n = name;
            factories[n] = () => new NllbTranslator(Options.Create(new NllbOptions
            {
                Path = resolvePath(c.Path), EncoderFile = c.EncoderFile, DecoderFile = c.DecoderFile,
                TokenizerFile = c.TokenizerFile, TokenizerConfigFile = c.TokenizerConfigFile,
                MaxTokens = c.MaxTokens, BeamSize = c.BeamSize,
            }));
        }
        else if (cfg.Type.Equals(ModelType.M2M100, StringComparison.OrdinalIgnoreCase)
                 && !string.IsNullOrWhiteSpace(cfg.Path))
        {
            var c = cfg; var n = name;
            factories[n] = () => new M2M100Translator(Options.Create(new M2M100Options
            {
                Path = resolvePath(c.Path), EncoderFile = c.EncoderFile, DecoderFile = c.DecoderFile,
                TokenizerFile = c.TokenizerFile, TokenizerConfigFile = c.TokenizerConfigFile,
                MaxTokens = c.MaxTokens, VocabFile = c.VocabFile,
            }));
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
    }
}
