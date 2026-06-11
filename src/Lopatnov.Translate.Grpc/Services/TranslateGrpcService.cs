using Grpc.Core;
using Lopatnov.Translate.Core;
using Lopatnov.Translate.Core.Abstractions;
using Lopatnov.Translate.Core.LanguageDetectors;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace Lopatnov.Translate.Grpc.Services;

/// <summary>
/// gRPC front-end for the translation cascade.
/// <para>
/// Language codes: the API accepts <c>language_format</c> of <c>"bcp47"</c> (default) or
/// <c>"native"</c>. BCP-47 is the system-wide interchange format — requests pass codes
/// straight to the engines, and each model adapter converts BCP-47 to its native codes
/// internally. With <c>"native"</c> the caller supplies codes the target engine understands
/// natively (e.g. FLORES-200 for NLLB) and detection results return the detector's raw label.
/// </para>
/// </summary>
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
        LanguageDetectionResult? detection = null;

        if (string.IsNullOrWhiteSpace(sourceLanguage) ||
            sourceLanguage.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            detection = _detector.Value.Detect(request.Text);
            sourceLanguage = detection.Bcp47;
        }

        string translated;
        try
        {
            translated = await lease.Translator.TranslateAsync(
                request.Text,
                sourceLanguage,
                request.TargetLanguage,
                context.CancellationToken);
        }
        catch (ArgumentException ex)
        {
            // Thrown by the model adapter when a language code is not in its vocabulary.
            throw new RpcException(new Status(StatusCode.InvalidArgument, ex.Message));
        }

        return new TranslateTextResponse
        {
            TranslatedText = translated,
            DetectedLanguage = detection?.ToFormat(langFormat) ?? string.Empty,
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

        ResolveLanguageFormat(request.LanguageFormat); // validate even though codes pass through

        try
        {
            // Use CancellationToken.None so that the full JSON file is always
            // translated to completion, regardless of any client-side deadline.
            // Individual per-string inference already respects a server-side
            // TRANSLATE_TIMEOUT_MS if configured.
            var (json, count) = await JsonLocalizationTranslator.TranslateAsync(
                request.Json,
                lease.Translator,
                request.SourceLanguage,
                request.TargetLanguage,
                string.IsNullOrWhiteSpace(request.ExistingTranslation) ? null : request.ExistingTranslation,
                string.IsNullOrWhiteSpace(request.Context) ? null : request.Context,
                CancellationToken.None);

            return new TranslateLocalizationResponse { Json = json, StringsTranslated = count };
        }
        catch (JsonException ex)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, $"Invalid JSON in request: {ex.Message}"));
        }
        catch (ArgumentException ex)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, ex.Message));
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
        ResolveLanguageFormat(request.LanguageFormat); // validate; Whisper's native codes are BCP-47

        // Whisper uses BCP-47 language codes natively (e.g. "en", "ru").
        var inputLanguage = string.IsNullOrWhiteSpace(request.Language) ||
                            request.Language.Equals("auto", StringComparison.OrdinalIgnoreCase)
            ? "auto"
            : request.Language;

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

        var response = new TranscribeAudioResponse
        {
            DetectedLanguage = string.IsNullOrEmpty(result.DetectedLanguage) ? string.Empty : result.DetectedLanguage,
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
        ResolveLanguageFormat(request.LanguageFormat); // validate; Piper voices are keyed by BCP-47

        // Piper uses BCP-47 language codes (e.g. "en", "ru", "uk").
        // When language is empty or "auto", detect it from the input text.
        var language = string.IsNullOrWhiteSpace(request.Language) ||
                       request.Language.Equals("auto", StringComparison.OrdinalIgnoreCase)
            ? _detector.Value.Detect(request.Text).Bcp47
            : request.Language;

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
        // The cascade crosses three engines (Whisper → translator → Piper); BCP-47 is the
        // only format every stage accepts, so "native" here simply means "no conversion".
        ResolveLanguageFormat(request.LanguageFormat);

        // --- Step 1: Speech → Text (Whisper) ---
        var sttLanguage = string.IsNullOrWhiteSpace(request.SourceLanguage) ||
                          request.SourceLanguage.Equals("auto", StringComparison.OrdinalIgnoreCase)
            ? "auto"
            : request.SourceLanguage;

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
        // Prefer the language Whisper detected (BCP-47); fall back to the explicit
        // request field. Guard against "auto" slipping through as a literal language
        // code if Whisper returns an empty DetectedLanguage and the caller specified "auto".
        string sourceLanguage;
        if (!string.IsNullOrEmpty(transcription.DetectedLanguage))
        {
            sourceLanguage = transcription.DetectedLanguage;
        }
        else if (!string.IsNullOrEmpty(request.SourceLanguage) &&
                 !request.SourceLanguage.Equals("auto", StringComparison.OrdinalIgnoreCase))
        {
            sourceLanguage = request.SourceLanguage;
        }
        else
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition,
                "Could not determine source language: Whisper did not detect a language " +
                "and source_language was not specified. Provide an explicit source_language."));
        }

        string translatedText;
        using (var lease = ResolveTranslator(request.Model))
        {
            try
            {
                translatedText = await lease.Translator.TranslateAsync(
                    transcription.FullText,
                    sourceLanguage,
                    request.TargetLanguage,
                    context.CancellationToken);
            }
            catch (ArgumentException ex)
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument, ex.Message));
            }
        }

        // --- Step 3: Text → Speech (Piper) ---
        Core.Models.SynthesisResult synthesis;
        try
        {
            synthesis = await _synthesizer.SynthesizeAsync(
                translatedText,
                request.TargetLanguage,
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
    /// Validates the raw language_format string from the request.
    /// The API accepts only <c>"bcp47"</c> (default when empty) and <c>"native"</c>;
    /// anything else — including formerly supported <c>"flores200"</c> — returns
    /// <see cref="StatusCode.InvalidArgument"/>.
    /// </summary>
    private static LanguageCodeFormat ResolveLanguageFormat(string? raw) =>
        (raw ?? string.Empty).ToLowerInvariant() switch
        {
            "" or "bcp47" or "bcp-47" => LanguageCodeFormat.Bcp47,
            "native"                  => LanguageCodeFormat.Native,
            var other => throw new RpcException(new Status(StatusCode.InvalidArgument,
                $"Unsupported language_format: '{other}'. Supported values: 'bcp47' (default), 'native'.")),
        };
}
