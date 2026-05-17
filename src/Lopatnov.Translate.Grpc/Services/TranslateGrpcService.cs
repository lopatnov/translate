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
    private readonly ISpeechSynthesizer _synthesizer;
    private readonly TranslationOptions _translationOptions;

    public TranslateGrpcService(
        ModelSessionManager manager,
        Lazy<ILanguageDetector> detector,
        ISpeechRecognizer recognizer,
        ISpeechSynthesizer synthesizer,
        IOptions<TranslationOptions> translationOptions)
    {
        _manager = manager;
        _detector = detector;
        _recognizer = recognizer;
        _synthesizer = synthesizer;
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
            sourceLanguage = ConvertLanguageCode(sourceLanguage, langFormat, LanguageCodeFormat.Flores200);
        }

        var targetLanguage = ConvertLanguageCode(request.TargetLanguage, langFormat, LanguageCodeFormat.Flores200);

        string translated;
        try
        {
            translated = await lease.Translator.TranslateAsync(
                request.Text,
                sourceLanguage,
                targetLanguage,
                context.CancellationToken);
        }
        catch (ArgumentException ex)
        {
            // Thrown by the tokenizer when a language code (e.g. nno_Latn for
            // Norwegian Nynorsk) is not in the model's vocabulary.
            throw new RpcException(new Status(StatusCode.InvalidArgument, ex.Message));
        }

        var detectedInFormat = detectedFlores != null
            ? ConvertLanguageCode(detectedFlores, LanguageCodeFormat.Flores200, langFormat)
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
            // TtsAvailable reflects whether any Piper voice is configured (not NullSpeechSynthesizer).
            TtsAvailable = _translationOptions.TextToAudio.Count > 0,
        };
        response.AvailableModels.AddRange(_manager.GetAvailableModels());
        response.AvailableVoices.AddRange(_translationOptions.TextToAudio.Keys.Order());
        return Task.FromResult(response);
    }

    public override async Task<TranslateLocalizationResponse> TranslateLocalization(
        TranslateLocalizationRequest request, ServerCallContext context)
    {
        using var lease = ResolveTranslator(request.Model);

        var langFormat = ResolveLanguageFormat(request.LanguageFormat);
        var sourceLanguage = ConvertLanguageCode(request.SourceLanguage, langFormat, LanguageCodeFormat.Flores200);
        var targetLanguage = ConvertLanguageCode(request.TargetLanguage, langFormat, LanguageCodeFormat.Flores200);

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
        return Task.FromResult(new DetectLanguageResponse
        {
            Language    = detection.ToFormat(langFormat),
            Probability = detection.Probability ?? 0f,
        });
    }

    public override async Task<TranscribeAudioResponse> TranscribeAudio(
        TranscribeAudioRequest request, ServerCallContext context)
    {
        var langFormat = ResolveLanguageFormat(request.LanguageFormat);

        // Whisper uses BCP-47 language codes natively (e.g. "en", "ru", "de").
        var inputLanguage = string.IsNullOrWhiteSpace(request.Language) ||
                            request.Language.Equals("auto", StringComparison.OrdinalIgnoreCase)
            ? "auto"
            : ConvertLanguageCode(request.Language, langFormat, LanguageCodeFormat.Bcp47);

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
            ? ConvertLanguageCode(result.DetectedLanguage, LanguageCodeFormat.Bcp47, langFormat)
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

    public override async Task<SynthesizeSpeechResponse> SynthesizeSpeech(
        SynthesizeSpeechRequest request, ServerCallContext context)
    {
        var langFormat = ResolveLanguageFormat(request.LanguageFormat);

        // Piper uses BCP-47 language codes (e.g. "en", "ru", "uk")
        var language = string.IsNullOrWhiteSpace(request.Language)
            ? string.Empty
            : ConvertLanguageCode(request.Language, langFormat, LanguageCodeFormat.Bcp47);

        Core.Models.SynthesisResult result;
        try
        {
            result = await _synthesizer.SynthesizeAsync(
                request.Text,
                language,
                request.Voice,
                request.Speed > 0f ? request.Speed : 1.0f,
                context.CancellationToken);
        }
        catch (NotSupportedException ex)
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            // Covers: espeak-ng not found, espeak-ng non-zero exit, model config issues
            throw new RpcException(new Status(StatusCode.Internal, ex.Message));
        }
        catch (FileNotFoundException ex)
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, ex.Message));
        }

        return new SynthesizeSpeechResponse
        {
            AudioData  = Google.Protobuf.ByteString.CopyFrom(result.AudioData),
            SampleRate = result.SampleRate,
        };
    }

    public override async Task<TranslateAudioResponse> TranslateAudio(
        TranslateAudioRequest request, ServerCallContext context)
    {
        var langFormat = ResolveLanguageFormat(request.LanguageFormat);

        // --- Step 1: Speech → Text (Whisper) ---
        // Whisper expects BCP-47 language codes (e.g. "en", "ru") or "auto".
        var sttLanguage = string.IsNullOrWhiteSpace(request.SourceLanguage) ||
                          request.SourceLanguage.Equals("auto", StringComparison.OrdinalIgnoreCase)
            ? "auto"
            : ConvertLanguageCode(request.SourceLanguage, langFormat, LanguageCodeFormat.Bcp47);

        Core.Models.TranscriptionResult transcription;
        try
        {
            transcription = await _recognizer.TranscribeAsync(
                request.AudioData.ToByteArray(),
                sttLanguage,
                context.CancellationToken);
        }
        catch (NotSupportedException ex)
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, ex.Message));
        }

        if (string.IsNullOrWhiteSpace(transcription.FullText))
            return new TranslateAudioResponse
            {
                Transcription  = string.Empty,
                TranslatedText = string.Empty,
                SampleRate     = 0,
            };

        // --- Step 2: Text → Text translation ---
        // Translators expect FLORES-200 codes internally.
        // Use detected language from Whisper (BCP-47) as source if available.
        var detectedBcp47 = transcription.DetectedLanguage;
        var sourceFlores = !string.IsNullOrEmpty(detectedBcp47)
            ? ConvertLanguageCode(detectedBcp47, LanguageCodeFormat.Bcp47, LanguageCodeFormat.Flores200)
            : ConvertLanguageCode(request.SourceLanguage, langFormat, LanguageCodeFormat.Flores200);

        var targetFlores = ConvertLanguageCode(request.TargetLanguage, langFormat, LanguageCodeFormat.Flores200);

        string translatedText;
        using (var lease = ResolveTranslator(string.Empty)) // default model
        {
            translatedText = await lease.Translator.TranslateAsync(
                transcription.FullText,
                sourceFlores,
                targetFlores,
                context.CancellationToken);
        }

        // --- Step 3: Text → Speech (Piper) ---
        // Piper expects BCP-47 language codes.
        var targetBcp47 = ConvertLanguageCode(request.TargetLanguage, langFormat, LanguageCodeFormat.Bcp47);

        Core.Models.SynthesisResult synthesis;
        try
        {
            synthesis = await _synthesizer.SynthesizeAsync(
                translatedText,
                targetBcp47,
                request.TargetVoice,
                speed: 1.0f,
                context.CancellationToken);
        }
        catch (NotSupportedException ex)
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            throw new RpcException(new Status(StatusCode.Internal, ex.Message));
        }
        catch (FileNotFoundException ex)
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, ex.Message));
        }

        return new TranslateAudioResponse
        {
            TranslatedAudio = Google.Protobuf.ByteString.CopyFrom(synthesis.AudioData),
            Transcription   = transcription.FullText,
            TranslatedText  = translatedText,
            SampleRate      = synthesis.SampleRate,
        };
    }

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

    /// <summary>
    /// Converts a language code between formats, mapping <see cref="ArgumentException"/> to
    /// <see cref="StatusCode.InvalidArgument"/> so callers receive a well-formed gRPC error.
    /// </summary>
    private static string ConvertLanguageCode(string code, LanguageCodeFormat from, LanguageCodeFormat to)
    {
        try
        {
            return LanguageCodeConverter.Convert(code, from, to);
        }
        catch (ArgumentException ex)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, ex.Message));
        }
    }
}
