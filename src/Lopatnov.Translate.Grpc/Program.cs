using Lopatnov.Translate.Core.Abstractions;
using Lopatnov.Translate.Core.LanguageDetectors;
using Lopatnov.Translate.Grpc;
using Lopatnov.Translate.Grpc.Services;
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
    ModelBootstrap.CreateLanguageDetector(sp, autoDetectName, rawModels, ResolvePath));

builder.Services.AddSingleton<Lazy<ILanguageDetector>>(sp =>
    new Lazy<ILanguageDetector>(sp.GetRequiredService<ILanguageDetector>));

// --- Speech recognizer (Whisper): lazy load + TTL, or NullSpeechRecognizer if not configured ---
builder.Services.AddSingleton<ISpeechRecognizer>(sp =>
    ModelBootstrap.CreateSpeechRecognizer(sp, audioToText, rawModels, ResolvePath));

// --- ModelSessionManager: lazy init + TTL eviction (text translation only) ---
builder.Services.AddSingleton<ModelSessionManager>(sp =>
    ModelBootstrap.BuildSessionManager(sp, rawModels, ResolvePath));

var app = builder.Build();

app.MapGrpcService<TranslateGrpcService>();
if (app.Environment.IsDevelopment())
    app.MapGrpcReflectionService();
app.MapGet("/", () => "Lopatnov.Translate gRPC service. Use a gRPC client to connect.");

await app.RunAsync();
