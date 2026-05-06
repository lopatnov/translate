using Lopatnov.Translate.Core;
using Lopatnov.Translate.Core.Abstractions;
using Lopatnov.Translate.Core.LanguageDetectors;
using Lopatnov.Translate.Grpc;
using Lopatnov.Translate.Grpc.Services;
using Lopatnov.Translate.LibreTranslate;
using Lopatnov.Translate.M2M100;
using Lopatnov.Translate.Nllb;
using Lopatnov.Translate.Whisper;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGrpc(options =>
{
    // Allow audio payloads up to 50 MB (default is 4 MB)
    options.MaxReceiveMessageSize = 50 * 1024 * 1024;
    options.MaxSendMessageSize   = 50 * 1024 * 1024;
});
if (builder.Environment.IsDevelopment())
    builder.Services.AddGrpcReflection();

string ResolvePath(string path) =>
    string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path) ? path :
    Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, path));

// --- Read and validate named model registry ---
var rawModels = builder.Configuration.GetSection("Models").GetChildren()
    .ToDictionary(s => s.Key, s => s.Get<ModelConfig>() ?? new(), StringComparer.OrdinalIgnoreCase);

foreach (var (name, cfg) in rawModels)
{
    if (string.IsNullOrWhiteSpace(cfg.Type))
        throw new InvalidOperationException(
            $"Configuration error: Models:{name}:Type is required.");
    if (!ModelType.IsKnown(cfg.Type))
        throw new InvalidOperationException(
            $"Configuration error: Models:{name}:Type value '{cfg.Type}' is unknown. " +
            "Known: NLLB, M2M100, FastText, LibreTranslate, Whisper.");
}

// --- Validate Translation references ---
var translationSection = builder.Configuration.GetSection("Translation");
var defaultModel  = translationSection["DefaultModel"]  ?? string.Empty;
var audioToText   = translationSection["AudioToText"]   ?? string.Empty;
var allowedModels = translationSection.GetSection("AllowedModels").Get<string[]>() ?? [];

if (!string.IsNullOrWhiteSpace(defaultModel) && !rawModels.ContainsKey(defaultModel))
    throw new InvalidOperationException(
        $"Configuration error: Translation:DefaultModel '{defaultModel}' is not defined in Models.");

var badAllowed = Array.Find(allowedModels, m => !rawModels.ContainsKey(m));
if (badAllowed != null)
    throw new InvalidOperationException(
        $"Configuration error: Translation:AllowedModels contains '{badAllowed}' which is not defined in Models.");

if (!string.IsNullOrWhiteSpace(audioToText))
{
    if (!rawModels.TryGetValue(audioToText, out var attCfg))
        throw new InvalidOperationException(
            $"Configuration error: Translation:AudioToText '{audioToText}' is not defined in Models.");
    if (!attCfg.Type.Equals(ModelType.Whisper, StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException(
            $"Configuration error: Translation:AudioToText '{audioToText}' must have Type=Whisper " +
            $"(found '{attCfg.Type}').");
}

// --- Register named HttpClients for LibreTranslate entries ---
foreach (var (name, cfg) in rawModels.Where(kv =>
    kv.Value.Type.Equals(ModelType.LibreTranslate, StringComparison.OrdinalIgnoreCase) &&
    !string.IsNullOrWhiteSpace(kv.Value.BaseUrl)))
{
    var capturedUrl = cfg.BaseUrl;
    builder.Services.AddHttpClient(name, c => c.BaseAddress = new Uri(capturedUrl));
}

// --- Translation options ---
builder.Services.AddOptions<TranslationOptions>()
    .Bind(builder.Configuration.GetSection("Translation"))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// --- Language detector: from Translation:AutoDetect, heuristic fallback ---
var autoDetectName = builder.Configuration["Translation:AutoDetect"] ?? string.Empty;
builder.Services.AddSingleton<ILanguageDetector>(sp =>
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
          .LogError("Translation:AutoDetect references unknown model '{Name}' — using heuristic language detector", autoDetectName);
        return new HeuristicLanguageDetector();
    }

    var modelPath = ResolvePath(cfg.Path);
    var log = sp.GetRequiredService<ILogger<FastTextLanguageDetector>>();

    if (string.IsNullOrWhiteSpace(modelPath) || !File.Exists(modelPath))
    {
        log.LogWarning("LangDetect model '{Name}' not found at {Path} — using heuristic language detector",
            autoDetectName, modelPath);
        return new HeuristicLanguageDetector();
    }

#pragma warning disable CA1873 // arguments are cheap local variables — false positive
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
        log.LogError(ex, "Failed to load LangDetect '{Name}' — using heuristic language detector", autoDetectName);
        return new HeuristicLanguageDetector();
    }
});

builder.Services.AddSingleton<Lazy<ILanguageDetector>>(sp =>
    new Lazy<ILanguageDetector>(sp.GetRequiredService<ILanguageDetector>));

// --- Speech recognizer (Whisper): lazy load + TTL, or NullSpeechRecognizer if not configured ---
builder.Services.AddSingleton<ISpeechRecognizer>(sp =>
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
    var modelPath  = ResolvePath(wCfg.Path);

#pragma warning disable CA1873 // arguments are cheap local variables — false positive
    log.LogInformation(
        "Registering Whisper STT model '{Key}' — will load lazily from {Path}", audioToText, modelPath);
#pragma warning restore CA1873

    return new WhisperRecognizer(
        Options.Create(new WhisperOptions
        {
            ModelPath  = modelPath,
            TtlMinutes = translOpts.ModelTtlMinutes,
        }),
        log);
});

// --- ModelSessionManager: lazy init + TTL eviction (text translation only) ---
builder.Services.AddSingleton<ModelSessionManager>(BuildSessionManager);

ModelSessionManager BuildSessionManager(IServiceProvider sp)
{
    var factories = new Dictionary<string, Func<ITextTranslator>>(StringComparer.OrdinalIgnoreCase);
    foreach (var (name, cfg) in rawModels)
        RegisterFactory(factories, name, cfg, sp);
    var opts = sp.GetRequiredService<IOptions<TranslationOptions>>().Value;
    return new ModelSessionManager(factories, opts.AllowedModels, TimeSpan.FromMinutes(opts.ModelTtlMinutes));
}

void RegisterFactory(Dictionary<string, Func<ITextTranslator>> factories, string name, ModelConfig cfg, IServiceProvider sp)
{
    var c = cfg; var n = name;

    // Whisper models are speech-to-text, not text translators — skip.
    if (c.Type.Equals(ModelType.Whisper, StringComparison.OrdinalIgnoreCase))
        return;

    if (c.Type.Equals(ModelType.NLLB, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(c.Path))
    {
        factories[n] = () => new NllbTranslator(Options.Create(new NllbOptions
        {
            Path = ResolvePath(c.Path), EncoderFile = c.EncoderFile, DecoderFile = c.DecoderFile,
            TokenizerFile = c.TokenizerFile, TokenizerConfigFile = c.TokenizerConfigFile,
            MaxTokens = c.MaxTokens, BeamSize = c.BeamSize,
        }));
    }
    else if (c.Type.Equals(ModelType.M2M100, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(c.Path))
    {
        factories[n] = () => new M2M100Translator(Options.Create(new M2M100Options
        {
            Path = ResolvePath(c.Path), EncoderFile = c.EncoderFile, DecoderFile = c.DecoderFile,
            TokenizerFile = c.TokenizerFile, TokenizerConfigFile = c.TokenizerConfigFile,
            MaxTokens = c.MaxTokens, VocabFile = c.VocabFile,
        }));
    }
    else if (c.Type.Equals(ModelType.LibreTranslate, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(c.BaseUrl))
    {
        var httpFac = sp.GetRequiredService<IHttpClientFactory>();
        factories[n] = () => new LibreTranslateClient(
            httpFac.CreateClient(n),
            Options.Create(new LibreTranslateOptions { BaseUrl = c.BaseUrl, ApiKey = c.ApiKey }));
    }
    // FastText models are language detectors — registered separately above, skip here.
}

var app = builder.Build();

app.MapGrpcService<TranslateGrpcService>();
if (app.Environment.IsDevelopment())
    app.MapGrpcReflectionService();
app.MapGet("/", () => "Lopatnov.Translate gRPC service. Use a gRPC client to connect.");

await app.RunAsync();
