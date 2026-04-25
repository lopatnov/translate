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

// --- Provider allowlist + TTL ---
builder.Services.AddOptions<AllowedProvidersOptions>()
    .Bind(builder.Configuration.GetSection("Translation"));

// --- NLLB (always registered) ---
builder.Services.AddOptions<NllbOptions>()
    .Bind(builder.Configuration.GetSection("Models:Nllb"))
    .ValidateDataAnnotations()
    .ValidateOnStart();

// --- M2M-100 (always registered; factory is skipped when Path is blank) ---
builder.Services.AddOptions<M2M100Options>()
    .Bind(builder.Configuration.GetSection("Models:M2M100"));

// --- LibreTranslate (registered only if BaseUrl is configured) ---
var libreTranslateUrl = builder.Configuration["LibreTranslate:BaseUrl"];
if (!string.IsNullOrWhiteSpace(libreTranslateUrl))
{
    builder.Services.AddOptions<LibreTranslateOptions>()
        .Bind(builder.Configuration.GetSection("LibreTranslate"))
        .ValidateDataAnnotations()
        .ValidateOnStart();
    builder.Services.AddHttpClient<LibreTranslateClient>((sp, c) =>
        c.BaseAddress = new Uri(sp.GetRequiredService<IOptions<LibreTranslateOptions>>().Value.BaseUrl!));
}

// --- ModelSessionManager: lazy init + TTL eviction ---
builder.Services.AddSingleton<ModelSessionManager>(sp =>
{
    var factories = new Dictionary<string, Func<ITextTranslator>>(StringComparer.OrdinalIgnoreCase);

    factories["nllb"] = () => new NllbTranslator(sp.GetRequiredService<IOptions<NllbOptions>>());

    var m2m100Path = sp.GetRequiredService<IOptions<M2M100Options>>().Value.Path;
    if (!string.IsNullOrWhiteSpace(m2m100Path))
        factories["m2m100"] = () => new M2M100Translator(sp.GetRequiredService<IOptions<M2M100Options>>());

    if (!string.IsNullOrWhiteSpace(libreTranslateUrl))
    {
        var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
        var libreOpts = sp.GetRequiredService<IOptions<LibreTranslateOptions>>();
        factories["libretranslate"] = () =>
            new LibreTranslateClient(
                httpClientFactory.CreateClient(nameof(LibreTranslateClient)), libreOpts);
    }

    var opts = sp.GetRequiredService<IOptions<AllowedProvidersOptions>>().Value;
    return new ModelSessionManager(factories, opts.AllowedProviders, TimeSpan.FromMinutes(opts.ModelTtlMinutes));
});

var app = builder.Build();

app.MapGrpcService<TranslateGrpcService>();
if (app.Environment.IsDevelopment())
    app.MapGrpcReflectionService();
app.MapGet("/", () => "Lopatnov.Translate gRPC service. Use a gRPC client to connect.");

await app.RunAsync();
