using Lopatnov.Translate.Core.Abstractions;
using Lopatnov.Translate.Grpc.Services;
using Microsoft.Extensions.Options;

namespace Lopatnov.Translate.Grpc;

/// <summary>
/// Background service that pre-warms configured ML models at startup by running
/// minimal synthetic requests through each model's full inference pipeline.
///
/// <para>
/// Runs concurrently with request serving — the gRPC service is available immediately,
/// but requests arriving before warm-up completes may still experience the cold-start
/// latency for their specific model.
/// </para>
///
/// <para>
/// Configure via <c>appsettings.json</c>:
/// <code>
/// "WarmUp": { "Models": [ "m2m100_418M", "whisper-small", "piper-en-US" ] }
/// </code>
/// Model names must match keys in the <c>Models</c> section.
/// </para>
/// </summary>
internal sealed class WarmUpHostedService : BackgroundService
{
    private readonly IConfiguration _config;
    private readonly ModelSessionManager _manager;
    private readonly ISpeechRecognizer _recognizer;
    private readonly ISpeechSynthesizer _synthesizer;
    private readonly TranslationOptions _translationOpts;
    private readonly ILogger<WarmUpHostedService> _logger;

    public WarmUpHostedService(
        IConfiguration config,
        ModelSessionManager manager,
        ISpeechRecognizer recognizer,
        ISpeechSynthesizer synthesizer,
        IOptions<TranslationOptions> translationOpts,
        ILogger<WarmUpHostedService> logger)
    {
        _config          = config;
        _manager         = manager;
        _recognizer      = recognizer;
        _synthesizer     = synthesizer;
        _translationOpts = translationOpts.Value;
        _logger          = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var models = _translationOpts.WarmUp;
        if (models.Length == 0)
            return;

        var modelNames = string.Join(", ", models);
#pragma warning disable CA1873 // modelNames already computed above — evaluation not deferred
        _logger.LogInformation(
            "WarmUp: starting pre-load of {Count} model(s): {Models}",
            models.Length,
            modelNames);
#pragma warning restore CA1873

        foreach (var name in models)
        {
            if (stoppingToken.IsCancellationRequested)
                break;

            await WarmUpOneAsync(name, stoppingToken);
        }

        _logger.LogInformation("WarmUp: all models pre-loaded.");
    }

    private async Task WarmUpOneAsync(string name, CancellationToken ct)
    {
        var modelType = _config[$"Models:{name}:Type"];
        if (string.IsNullOrWhiteSpace(modelType))
        {
            _logger.LogWarning("WarmUp: model '{Name}' not found in Models config — skipping", name);
            return;
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            await DispatchWarmUpAsync(name, modelType, ct);
            sw.Stop();
#pragma warning disable CA1873 // ElapsedMilliseconds is a cheap property on a stopped Stopwatch
            _logger.LogInformation("WarmUp: {Name} ({Type}) ready in {Ms} ms", name, modelType, sw.ElapsedMilliseconds);
#pragma warning restore CA1873
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            sw.Stop();
            _logger.LogWarning(ex, "WarmUp: {Name} ({Type}) failed after {Ms} ms — model will load on first real request",
                name, modelType, sw.ElapsedMilliseconds);
        }
    }

    private Task DispatchWarmUpAsync(string name, string modelType, CancellationToken ct)
    {
        return modelType.ToUpperInvariant() switch
        {
            "NLLB" or "M2M100" or "LIBRETRANSLATE" => WarmUpTextTranslatorAsync(name, ct),
            "WHISPER" => WarmUpWhisperAsync(name, ct),
            "PIPER"   => WarmUpPiperAsync(name, ct),
            // FastText and unknown types: no inference warm-up needed
            _ => Task.CompletedTask,
        };
    }

    // -------------------------------------------------------------------------
    // Per-type warm-up
    // -------------------------------------------------------------------------

    private async Task WarmUpTextTranslatorAsync(string name, CancellationToken ct)
    {
        ITextTranslator translator;
        try
        {
            translator = _manager.Get(name);
        }
        catch (KeyNotFoundException ex)
        {
            _logger.LogWarning(ex,
                "WarmUp: model '{Name}' is configured but not in the allowed list — skipping", name);
            return;
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex,
                "WarmUp: model '{Name}' is not in Translation:AllowedModels — skipping", name);
            return;
        }

        // Minimal translation: single space, English → French (FLORES-200 codes).
        // This exercises the full pipeline: tokenisation → encoder → decoder → detokenisation.
        _ = await translator.TranslateAsync(" ", "eng_Latn", "fra_Latn", ct);
    }

    private async Task WarmUpWhisperAsync(string name, CancellationToken ct)
    {
        // Only warm up if this model is the configured STT model.
        if (!name.Equals(_translationOpts.AudioToText, StringComparison.OrdinalIgnoreCase))
        {
#pragma warning disable CA1873 // name is a cheap local string
            _logger.LogDebug(
                "WarmUp: Whisper '{Name}' is not the active AudioToText model — skipping", name);
#pragma warning restore CA1873
            return;
        }

        // 0.1 s of silence at 16 kHz, 16-bit mono PCM.
        var silenceWav = MakeSilenceWav(sampleRate: 16_000, durationMs: 100);
        _ = await _recognizer.TranscribeAsync(silenceWav, "auto", ct);
    }

    private async Task WarmUpPiperAsync(string name, CancellationToken ct)
    {
        // Find which language key maps to this Piper model.
        var lang = _translationOpts.TextToAudio
            .FirstOrDefault(kv => kv.Value.Equals(name, StringComparison.OrdinalIgnoreCase))
            .Key;

        if (string.IsNullOrEmpty(lang))
        {
#pragma warning disable CA1873 // name is a cheap local string
            _logger.LogDebug(
                "WarmUp: Piper '{Name}' is not referenced in Translation:TextToAudio — skipping", name);
#pragma warning restore CA1873
            return;
        }

        // Single space: espeak-ng + ONNX inference exercises the full pipeline.
        _ = await _synthesizer.SynthesizeAsync(" ", lang, cancellationToken: ct);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Generates a minimal valid WAV file containing silence (zero-valued PCM samples).
    /// Used to warm up the Whisper recognizer without requiring an external audio file.
    /// </summary>
    internal static byte[] MakeSilenceWav(int sampleRate = 16_000, int durationMs = 100)
    {
        int samples   = sampleRate * durationMs / 1000;
        int dataBytes = samples * 2;               // 16-bit = 2 bytes/sample
        var buf       = new byte[44 + dataBytes];   // 44-byte RIFF header + PCM data

        // All PCM bytes are already zero (silence) — only write the RIFF header.
        // RIFF chunk
        buf[0]  = (byte)'R'; buf[1]  = (byte)'I'; buf[2]  = (byte)'F'; buf[3]  = (byte)'F';
        WriteInt32LE(buf,  4, 36 + dataBytes);     // ChunkSize
        buf[8]  = (byte)'W'; buf[9]  = (byte)'A'; buf[10] = (byte)'V'; buf[11] = (byte)'E';

        // fmt sub-chunk
        buf[12] = (byte)'f'; buf[13] = (byte)'m'; buf[14] = (byte)'t'; buf[15] = (byte)' ';
        WriteInt32LE(buf, 16, 16);                  // SubChunk1Size (PCM)
        WriteInt16LE(buf, 20, 1);                   // AudioFormat: PCM
        WriteInt16LE(buf, 22, 1);                   // NumChannels: mono
        WriteInt32LE(buf, 24, sampleRate);           // SampleRate
        WriteInt32LE(buf, 28, sampleRate * 2);       // ByteRate
        WriteInt16LE(buf, 32, 2);                   // BlockAlign
        WriteInt16LE(buf, 34, 16);                  // BitsPerSample

        // data sub-chunk
        buf[36] = (byte)'d'; buf[37] = (byte)'a'; buf[38] = (byte)'t'; buf[39] = (byte)'a';
        WriteInt32LE(buf, 40, dataBytes);            // SubChunk2Size

        return buf;
    }

    private static void WriteInt32LE(byte[] buf, int offset, int value)
    {
        buf[offset]     = (byte)value;
        buf[offset + 1] = (byte)(value >> 8);
        buf[offset + 2] = (byte)(value >> 16);
        buf[offset + 3] = (byte)(value >> 24);
    }

    private static void WriteInt16LE(byte[] buf, int offset, short value)
    {
        buf[offset]     = (byte)value;
        buf[offset + 1] = (byte)(value >> 8);
    }
}
