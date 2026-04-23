using Grpc.Core;
using Lopatnov.Translate.Core;
using Lopatnov.Translate.Core.Abstractions;
using System.Text.Json;

namespace Lopatnov.Translate.Grpc.Services;

public sealed class TranslateGrpcService : TranslateService.TranslateServiceBase
{
    private const string DefaultProvider = "nllb";
    private readonly IServiceProvider _services;

    public TranslateGrpcService(IServiceProvider services)
        => _services = services;

    public override async Task<TranslateTextResponse> TranslateText(
        TranslateTextRequest request, ServerCallContext context)
    {
        var (translator, providerKey) = ResolveTranslator(request.Provider);

        var translated = await translator.TranslateAsync(
            request.Text,
            request.SourceLanguage,
            request.TargetLanguage,
            context.CancellationToken);

        return new TranslateTextResponse
        {
            TranslatedText = translated,
            ProviderUsed = providerKey,
        };
    }

    public override Task<GetCapabilitiesResponse> GetCapabilities(
        GetCapabilitiesRequest request, ServerCallContext context)
    {
        var response = new GetCapabilitiesResponse
        {
            SttAvailable = false,
            TtsAvailable = false,
        };
        response.AvailableProviders.AddRange(["nllb", "libretranslate"]);
        response.SupportedLanguages.AddRange([
            Language.EnglishLatin,    Language.UkrainianCyrillic, Language.RussianCyrillic, Language.GermanLatin,
            Language.FrenchLatin,     Language.SpanishLatin,      Language.PolishLatin,      Language.ChineseSimplified,
            Language.JapaneseJpan,   Language.ArabicArab,
        ]);
        return Task.FromResult(response);
    }

    public override async Task<TranslateLocalizationResponse> TranslateLocalization(
        TranslateLocalizationRequest request, ServerCallContext context)
    {
        var (translator, _) = ResolveTranslator(request.Provider);

        try
        {
            var (json, count) = await JsonLocalizationTranslator.TranslateAsync(
                request.Json,
                translator,
                request.SourceLanguage,
                request.TargetLanguage,
                context.CancellationToken,
                string.IsNullOrWhiteSpace(request.ExistingTranslation) ? null : request.ExistingTranslation,
                string.IsNullOrWhiteSpace(request.Context) ? null : request.Context);

            return new TranslateLocalizationResponse { Json = json, StringsTranslated = count };
        }
        catch (JsonException ex)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, $"Invalid JSON in request: {ex.Message}"));
        }
    }

    public override Task<TranscribeAudioResponse> TranscribeAudio(
        TranscribeAudioRequest request, ServerCallContext context)
        => throw new RpcException(new Status(StatusCode.Unimplemented, "Phase 2"));

    public override Task<SynthesizeSpeechResponse> SynthesizeSpeech(
        SynthesizeSpeechRequest request, ServerCallContext context)
        => throw new RpcException(new Status(StatusCode.Unimplemented, "Phase 3"));

    public override Task<TranslateAudioResponse> TranslateAudio(
        TranslateAudioRequest request, ServerCallContext context)
        => throw new RpcException(new Status(StatusCode.Unimplemented, "Phase 4"));

    private (ITextTranslator Translator, string ProviderKey) ResolveTranslator(string? provider)
    {
        var providerKey = string.IsNullOrWhiteSpace(provider) ? DefaultProvider : provider.Trim();
        var translator = _services.GetKeyedService<ITextTranslator>(providerKey)
            ?? throw new RpcException(new Status(StatusCode.InvalidArgument, $"Unknown provider: '{providerKey}'"));
        return (translator, providerKey);
    }
}
