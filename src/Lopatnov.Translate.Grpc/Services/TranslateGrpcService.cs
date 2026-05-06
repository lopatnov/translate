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
    private readonly ISpeechRecognizer _recognizer;
    private readonly TranslationOptions _translationOptions;

    public TranslateGrpcService(
        ModelSessionManager manager,
        Lazy<ILanguageDetector> detector,
        ISpeechRecognizer recognizer,
        IOptions<TranslationOptions> translationOptions)
    {
        _manager = manager;
        _detector = detector;
        _recognizer = recognizer;
        _translationOptions = translationOptions.Value;
    }

    public override async Task<TranslateTextResponse> TranslateText(
        TranslateTextRequest request, ServerCallContext context)
    {
        using var lease = ResolveTranslator(request.Model);

        var langFormat = ResolveLanguageFormat(request.LanguageFormat);
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
            sourceLanguage = LanguageCodeConverter.Convert(sourceLanguage, langFormat, LanguageCodeFormat.Flores200);
        }

        var targetLanguage = LanguageCodeConverter.Convert(request.TargetLanguage, langFormat, LanguageCodeFormat.Flores200);

        var translated = await lease.Translator.TranslateAsync(
            request.Text,
            sourceLanguage,
            targetLanguage,
            context.CancellationToken);

        var detectedInFormat = detectedFlores != null
            ? LanguageCodeConverter.Convert(detectedFlores, LanguageCodeFormat.Flores200, langFormat)
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
            // SttAvailable reflects whether a Whisper model is configured (not NullSpeechRecognizer).
            SttAvailable = !string.IsNullOrEmpty(_translationOptions.AudioToText),
            TtsAvailable = false,
        };
        response.AvailableModels.AddRange(_manager.GetAvailableModels());
        return Task.FromResult(response);
    }

    public override async Task<TranslateLocalizationResponse> TranslateLocalization(
        TranslateLocalizationRequest request, ServerCallContext context)
    {
        using var lease = ResolveTranslator(request.Model);

        var langFormat = ResolveLanguageFormat(request.LanguageFormat);
        var sourceLanguage = LanguageCodeConverter.Convert(request.SourceLanguage, langFormat, LanguageCodeFormat.Flores200);
        var targetLanguage = LanguageCodeConverter.Convert(request.TargetLanguage, langFormat, LanguageCodeFormat.Flores200);

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
        var langFormat = ResolveLanguageFormat(request.LanguageFormat);
        var language = detection.ToFormat(langFormat);
        return Task.FromResult(new DetectLanguageResponse { Language = language });
    }

    public override async Task<TranscribeAudioResponse> TranscribeAudio(
        TranscribeAudioRequest request, ServerCallContext context)
    {
        var langFormat = ResolveLanguageFormat(request.LanguageFormat);

        // Whisper uses BCP-47 language codes natively (e.g. "en", "ru", "de").
        var inputLanguage = string.IsNullOrWhiteSpace(request.Language) ||
                            request.Language.Equals("auto", StringComparison.OrdinalIgnoreCase)
            ? "auto"
            : LanguageCodeConverter.Convert(request.Language, langFormat, LanguageCodeFormat.Bcp47);

        Core.Models.TranscriptionResult result;
        try
        {
            result = await _recognizer.TranscribeAsync(
                request.AudioData.ToByteArray(),
                inputLanguage,
                context.CancellationToken);
        }
        catch (NotSupportedException ex)
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, ex.Message));
        }

        var detectedInFormat = !string.IsNullOrEmpty(result.DetectedLanguage)
            ? LanguageCodeConverter.Convert(result.DetectedLanguage, LanguageCodeFormat.Bcp47, langFormat)
            : string.Empty;

        var response = new TranscribeAudioResponse
        {
            DetectedLanguage = detectedInFormat,
            FullText         = result.FullText,
        };
        response.Segments.AddRange(result.Segments.Select(s => new TranscriptionSegment
        {
            Text      = s.Text,
            StartTime = s.StartTime,
            EndTime   = s.EndTime,
        }));
        return response;
    }

    public override Task<SynthesizeSpeechResponse> SynthesizeSpeech(
        SynthesizeSpeechRequest request, ServerCallContext context)
        => throw new RpcException(new Status(StatusCode.Unimplemented, "Phase 3"));

    public override Task<TranslateAudioResponse> TranslateAudio(
        TranslateAudioRequest request, ServerCallContext context)
        => throw new RpcException(new Status(StatusCode.Unimplemented, "Phase 4"));

    private ModelSessionManager.TranslatorLease ResolveTranslator(string? provider)
    {
        var key = string.IsNullOrWhiteSpace(provider) ? _translationOptions.DefaultModel : provider.Trim();
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

    /// <summary>
    /// Converts a raw language_format string from the request to <see cref="LanguageCodeFormat"/>.
    /// Returns <see cref="LanguageCodeFormat.Bcp47"/> when the field is empty.
    /// Throws <see cref="RpcException"/> with <see cref="StatusCode.InvalidArgument"/> for unknown values
    /// so the caller gets a well-formed gRPC error instead of an unhandled server exception.
    /// </summary>
    private static LanguageCodeFormat ResolveLanguageFormat(string? raw)
    {
        try
        {
            return (raw ?? string.Empty).ToLanguageCodeFormat();
        }
        catch (ArgumentException ex)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, ex.Message));
        }
    }
}
