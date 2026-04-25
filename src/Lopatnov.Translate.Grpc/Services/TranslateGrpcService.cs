using Grpc.Core;
using Lopatnov.Translate.Core;
using Lopatnov.Translate.Core.Abstractions;
using System.Text.Json;

namespace Lopatnov.Translate.Grpc.Services;

public sealed class TranslateGrpcService : TranslateService.TranslateServiceBase
{
    private const string DefaultProvider = "nllb";
    private readonly ModelSessionManager _manager;
    private readonly ILanguageDetector _detector;

    public TranslateGrpcService(ModelSessionManager manager, ILanguageDetector detector)
    {
        _manager = manager;
        _detector = detector;
    }

    public override async Task<TranslateTextResponse> TranslateText(
        TranslateTextRequest request, ServerCallContext context)
    {
        var (translator, providerKey) = ResolveTranslator(request.Provider);

        var sourceLanguage = request.SourceLanguage;
        string? detectedLanguage = null;

        if (string.IsNullOrWhiteSpace(sourceLanguage) ||
            sourceLanguage.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            detectedLanguage = _detector.Detect(request.Text);
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
        response.AvailableProviders.AddRange(_manager.GetAvailableProviders());
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
        var key = string.IsNullOrWhiteSpace(provider) ? DefaultProvider : provider.Trim();
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
