using Lopatnov.Translate.Core;
using Lopatnov.Translate.Core.Abstractions;
using Lopatnov.Translate.Grpc;
using Lopatnov.Translate.Grpc.Services;
using Lopatnov.Translate.LibreTranslate;
using Lopatnov.Translate.M2M100;
using Lopatnov.Translate.Nllb;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGrpc();
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
            "Known: NLLB, M2M100, FastText, LibreTranslate.");
}

// --- Validate Translation references ---
var translationSection = builder.Configuration.GetSection("Translation");
var defaultModel = translationSection["DefaultModel"] ?? string.Empty;
var allowedModels = translationSection.GetSection("AllowedModels").Get<string[]>() ?? [];

if (!string.IsNullOrWhiteSpace(defaultModel) && !rawModels.ContainsKey(defaultModel))
    throw new InvalidOperationException(
        $"Configuration error: Translation:DefaultModel '{defaultModel}' is not defined in Models.");

foreach (var modelName in allowedModels)
{
    if (!rawModels.ContainsKey(modelName))
        throw new InvalidOperationException(
            $"Configuration error: Translation:AllowedModels contains '{modelName}' which is not defined in Models.");
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

// --- Language detector: from Translation:AutoDetect, null fallback otherwise ---
var autoDetectName = builder.Configuration["Translation:AutoDetect"] ?? string.Empty;
builder.Services.AddSingleton<ILanguageDetector>(sp =>
{
    var log = sp.GetRequiredService<ILogger<FastTextLanguageDetector>>();
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
    if (string.IsNullOrWhiteSpace(modelPath) || !File.Exists(modelPath))
    {
        log.LogWarning("LangDetect model '{Name}' not found at {Path} — using heuristic language detector",
            autoDetectName, modelPath);
        return new HeuristicLanguageDetector();
    }
    log.LogInformation("Loading LangDetect '{Name}' ({Type}) from {Path}",
        autoDetectName, cfg.Type, modelPath);
    try
    {
        return FastTextLanguageDetector.Load(modelPath);
    }
    catch (Exception ex)
    {
        log.LogError(ex, "Failed to load LangDetect '{Name}' — using heuristic language detector", autoDetectName);
        return new HeuristicLanguageDetector();
    }
});

// --- ModelSessionManager: lazy init + TTL eviction ---
builder.Services.AddSingleton<ModelSessionManager>(sp =>
{
    var factories = new Dictionary<string, Func<ITextTranslator>>(StringComparer.OrdinalIgnoreCase);

    foreach (var (name, cfg) in rawModels)
    {
        var c = cfg;
        var n = name;

        if (c.Type.Equals(ModelType.NLLB, StringComparison.OrdinalIgnoreCase))
        {
            factories[n] = () => new NllbTranslator(Options.Create(new NllbOptions
            {
                Path = ResolvePath(c.Path),
                EncoderFile = c.EncoderFile,
                DecoderFile = c.DecoderFile,
                TokenizerFile = c.TokenizerFile,
                TokenizerConfigFile = c.TokenizerConfigFile,
                MaxTokens = c.MaxTokens,
                BeamSize = c.BeamSize,
            }));
        }
        else if (c.Type.Equals(ModelType.M2M100, StringComparison.OrdinalIgnoreCase) &&
                 !string.IsNullOrWhiteSpace(c.Path))
        {
            factories[n] = () => new M2M100Translator(Options.Create(new M2M100Options
            {
                Path = ResolvePath(c.Path),
                EncoderFile = c.EncoderFile,
                DecoderFile = c.DecoderFile,
                TokenizerFile = c.TokenizerFile,
                TokenizerConfigFile = c.TokenizerConfigFile,
                MaxTokens = c.MaxTokens,
                VocabFile = c.VocabFile,
            }));
        }
        else if (c.Type.Equals(ModelType.LibreTranslate, StringComparison.OrdinalIgnoreCase) &&
                 !string.IsNullOrWhiteSpace(c.BaseUrl))
        {
            var httpFac = sp.GetRequiredService<IHttpClientFactory>();
            factories[n] = () => new LibreTranslateClient(
                httpFac.CreateClient(n),
                Options.Create(new LibreTranslateOptions { BaseUrl = c.BaseUrl, ApiKey = c.ApiKey }));
        }
    }

    var opts = sp.GetRequiredService<IOptions<TranslationOptions>>().Value;
    return new ModelSessionManager(factories, opts.AllowedModels, TimeSpan.FromMinutes(opts.ModelTtlMinutes));
});

var app = builder.Build();

app.MapGrpcService<TranslateGrpcService>();
if (app.Environment.IsDevelopment())
    app.MapGrpcReflectionService();
app.MapGet("/", () => "Lopatnov.Translate gRPC service. Use a gRPC client to connect.");

await app.RunAsync();
