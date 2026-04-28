using Grpc.Core;
using Lopatnov.Translate.Core;
using Lopatnov.Translate.Core.Abstractions;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace Lopatnov.Translate.Grpc.Services;

public sealed class TranslateGrpcService : TranslateService.TranslateServiceBase
{
    private readonly ModelSessionManager _manager;
    private readonly Lazy<ILanguageDetector> _detector;
    private readonly string _defaultModel;

    public TranslateGrpcService(
        ModelSessionManager manager,
        Lazy<ILanguageDetector> detector,
        IOptions<TranslationOptions> translationOptions)
    {
        _manager = manager;
        _detector = detector;
        _defaultModel = translationOptions.Value.DefaultModel;
    }

    public override async Task<TranslateTextResponse> TranslateText(
        TranslateTextRequest request, ServerCallContext context)
    {
        var (translator, providerKey) = ResolveTranslator(request.Model);

        var sourceLanguage = request.SourceLanguage;
        string? detectedLanguage = null;

        if (string.IsNullOrWhiteSpace(sourceLanguage) ||
            sourceLanguage.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            detectedLanguage = _detector.Value.Detect(request.Text);
            sourceLanguage = detectedLanguage;
        }

        var translated = await translator.TranslateAsync(
            request.Text,
            sourceLanguage,
            request.TargetLanguage,
            context.CancellationToken);

        return new TranslateTextResponse
        {
            TranslatedText = translated,
            DetectedLanguage = detectedLanguage ?? string.Empty,
            ModelUsed = providerKey,
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
        response.AvailableModels.AddRange(_manager.GetAvailableModels());
        return Task.FromResult(response);
    }

    public override async Task<TranslateLocalizationResponse> TranslateLocalization(
        TranslateLocalizationRequest request, ServerCallContext context)
    {
        var (translator, _) = ResolveTranslator(request.Model);

        try
        {
            var (json, count) = await JsonLocalizationTranslator.TranslateAsync(
                request.Json,
                translator,
                request.SourceLanguage,
                request.TargetLanguage,
                string.IsNullOrWhiteSpace(request.ExistingTranslation) ? null : request.ExistingTranslation,
                string.IsNullOrWhiteSpace(request.Context) ? null : request.Context,
                context.CancellationToken);

            return new TranslateLocalizationResponse { Json = json, StringsTranslated = count };
        }
        catch (JsonException ex)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, $"Invalid JSON in request: {ex.Message}"));
        }
    }

    public override Task<DetectLanguageResponse> DetectLanguage(
        DetectLanguageRequest request, ServerCallContext context)
    {
        var language = _detector.Value.Detect(request.Text);
        return Task.FromResult(new DetectLanguageResponse { Language = language });
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
        var key = string.IsNullOrWhiteSpace(provider) ? _defaultModel : provider.Trim();
        try
        {
            return (_manager.Get(key), key);
        }
        catch (KeyNotFoundException)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, $"Unknown provider: '{key}'"));
        }
        catch (UnauthorizedAccessException)
        {
            throw new RpcException(new Status(StatusCode.PermissionDenied, $"Provider '{key}' is not allowed."));
        }
    }
}
