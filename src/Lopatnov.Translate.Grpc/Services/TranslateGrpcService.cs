using Grpc.Core;
using Lopatnov.Translate.Core;
using Lopatnov.Translate.Core.Abstractions;
using Lopatnov.Translate.Core.LanguageDetectors;
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
        using var lease = ResolveTranslator(request.Model);

        var langFormat = request.LanguageFormat;
        var sourceLanguage = request.SourceLanguage;
        string? detectedFlores = null;

        if (string.IsNullOrWhiteSpace(sourceLanguage) ||
            sourceLanguage.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            var detection = _detector.Value.Detect(request.Text);
            detectedFlores = detection.Flores200;
            sourceLanguage = detectedFlores;
        }
        else
        {
            sourceLanguage = LanguageCodeConverter.Convert(sourceLanguage, langFormat, "flores200");
        }

        var targetLanguage = LanguageCodeConverter.Convert(request.TargetLanguage, langFormat, "flores200");

        var translated = await lease.Translator.TranslateAsync(
            request.Text,
            sourceLanguage,
            targetLanguage,
            context.CancellationToken);

        var detectedInFormat = detectedFlores != null
            ? LanguageCodeConverter.Convert(detectedFlores, "flores200", langFormat)
            : string.Empty;

        return new TranslateTextResponse
        {
            TranslatedText = translated,
            DetectedLanguage = detectedInFormat,
            ModelUsed = lease.Key,
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
        using var lease = ResolveTranslator(request.Model);

        var langFormat = request.LanguageFormat;
        var sourceLanguage = LanguageCodeConverter.Convert(request.SourceLanguage, langFormat, "flores200");
        var targetLanguage = LanguageCodeConverter.Convert(request.TargetLanguage, langFormat, "flores200");

        try
        {
            var (json, count) = await JsonLocalizationTranslator.TranslateAsync(
                request.Json,
                lease.Translator,
                sourceLanguage,
                targetLanguage,
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
        var detection = _detector.Value.Detect(request.Text);
        var language = detection.ToFormat(request.LanguageFormat);
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

    private ModelSessionManager.TranslatorLease ResolveTranslator(string? provider)
    {
        var key = string.IsNullOrWhiteSpace(provider) ? _defaultModel : provider.Trim();
        try
        {
            return _manager.Rent(key);
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
