using Grpc.Core;
using Lopatnov.Translate.Core.Abstractions;

namespace Lopatnov.Translate.Grpc.Services;

public sealed class TranslateGrpcService : TranslateService.TranslateServiceBase
{
    private readonly IServiceProvider _services;

    public TranslateGrpcService(IServiceProvider services)
        => _services = services;

    public override async Task<TranslateTextResponse> TranslateText(
        TranslateTextRequest request, ServerCallContext context)
    {
        var providerKey = string.IsNullOrWhiteSpace(request.Provider) ? "nllb" : request.Provider;
        var translator = _services.GetRequiredKeyedService<ITextTranslator>(providerKey);

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
            "eng_Latn", "ukr_Cyrl", "rus_Cyrl", "deu_Latn",
            "fra_Latn", "spa_Latn", "pol_Latn", "zho_Hans",
        ]);
        return Task.FromResult(response);
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
}
