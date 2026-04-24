using Lopatnov.Translate.Core.Abstractions;
using Lopatnov.Translate.Grpc.Services;
using Lopatnov.Translate.LibreTranslate;
using Lopatnov.Translate.M2M100;
using Lopatnov.Translate.Nllb;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGrpc();
if (builder.Environment.IsDevelopment())
    builder.Services.AddGrpcReflection();

builder.Services.AddOptions<NllbOptions>()
    .Bind(builder.Configuration.GetSection("Models:Nllb"))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddKeyedSingleton<ITextTranslator, NllbTranslator>("nllb", (sp, _) =>
    new NllbTranslator(sp.GetRequiredService<IOptions<NllbOptions>>()));

var m2m100Path = builder.Configuration["Models:M2M100:Path"];
if (!string.IsNullOrWhiteSpace(m2m100Path))
{
    builder.Services.AddOptions<M2M100Options>()
        .Bind(builder.Configuration.GetSection("Models:M2M100"))
        .ValidateDataAnnotations()
        .ValidateOnStart();
    builder.Services.AddKeyedSingleton<ITextTranslator>("m2m100", (sp, _) =>
        new M2M100Translator(sp.GetRequiredService<IOptions<M2M100Options>>()));
}

var libreTranslateUrl = builder.Configuration["LibreTranslate:BaseUrl"];
if (!string.IsNullOrWhiteSpace(libreTranslateUrl))
{
    builder.Services.AddOptions<LibreTranslateOptions>()
        .Bind(builder.Configuration.GetSection("LibreTranslate"))
        .ValidateDataAnnotations()
        .ValidateOnStart();
    builder.Services.AddHttpClient<LibreTranslateClient>((sp, c) =>
        c.BaseAddress = new Uri(sp.GetRequiredService<IOptions<LibreTranslateOptions>>().Value.BaseUrl!));
    builder.Services.AddKeyedScoped<ITextTranslator>("libretranslate", (sp, _) =>
        (ITextTranslator)sp.GetRequiredService<LibreTranslateClient>());
}

var app = builder.Build();

app.MapGrpcService<TranslateGrpcService>();
if (app.Environment.IsDevelopment())
    app.MapGrpcReflectionService();
app.MapGet("/", () => "Lopatnov.Translate gRPC service. Use a gRPC client to connect.");

await app.RunAsync();
