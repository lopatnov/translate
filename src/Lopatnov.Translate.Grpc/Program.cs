using Lopatnov.Translate.Core.Abstractions;
using Lopatnov.Translate.Grpc.Services;
using Lopatnov.Translate.LibreTranslate;
using Lopatnov.Translate.Nllb;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddGrpc();
if (builder.Environment.IsDevelopment())
    builder.Services.AddGrpcReflection();

builder.Services.Configure<NllbOptions>(builder.Configuration.GetSection("Models:Nllb"));
builder.Services.Configure<LibreTranslateOptions>(builder.Configuration.GetSection("LibreTranslate"));

builder.Services.AddKeyedSingleton<ITextTranslator, NllbTranslator>("nllb", (sp, _) =>
    new NllbTranslator(sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<NllbOptions>>()));

builder.Services.AddHttpClient<LibreTranslateClient>();
builder.Services.AddKeyedSingleton<ITextTranslator>("libretranslate", (sp, _) =>
    (ITextTranslator)sp.GetRequiredService<LibreTranslateClient>());

var app = builder.Build();

app.MapGrpcService<TranslateGrpcService>();
if (app.Environment.IsDevelopment())
    app.MapGrpcReflectionService();
app.MapGet("/", () => "Lopatnov.Translate gRPC service. Use a gRPC client to connect.");

app.Run();
