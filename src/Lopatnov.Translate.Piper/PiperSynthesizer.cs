using System.Text;
using Lopatnov.Translate.Core.Abstractions;
using Lopatnov.Translate.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Lopatnov.Translate.Piper;

/// <summary>
/// Text-to-speech synthesizer backed by a local Piper ONNX voice model.
///
/// <para>
/// Pipeline:
/// <list type="number">
///   <item>Phonemise input text via <c>espeak-ng</c> → IPA string.</item>
///   <item>Map IPA characters to integer IDs using the voice's <c>phoneme_id_map</c>.</item>
///   <item>Run ONNX inference → raw PCM float32 samples.</item>
///   <item>Encode as 16-bit PCM WAV and return <see cref="SynthesisResult"/>.</item>
/// </list>
/// </para>
///
/// <para>
/// Loading is lazy: the <see cref="InferenceSession"/> is allocated on the first
/// call to <see cref="SynthesizeAsync"/>. After <see cref="PiperOptions.TtlMinutes"/>
/// of inactivity the session is disposed and memory is released.
/// </para>
/// </summary>
public sealed class PiperSynthesizer : ISpeechSynthesizer, IDisposable
{
    private readonly PiperOptions _options;
    private readonly TimeSpan _ttl;
    private readonly ILogger<PiperSynthesizer>? _logger;
    private readonly SessionOptions? _sessionOptions;

    // Lazy load + TTL eviction state (mirrors WhisperRecognizer pattern)
    private readonly SemaphoreSlim _lock = new(1, 1);
    // Serialises ONNX inference: InferenceSession.Run() is not thread-safe under
    // concurrent load (same issue fixed in M2M100Translator / NllbTranslator).
    private readonly SemaphoreSlim _inferenceLock = new(1, 1);
    private readonly Timer _evictionTimer;
    private InferenceSession? _session;
    private PiperVoiceConfig? _voiceConfig;
    private long _lastUsedTicks;
    private int _activeInferences;
    private bool _disposed;

    public PiperSynthesizer(
        IOptions<PiperOptions> options,
        ILogger<PiperSynthesizer>? logger = null,
        SessionOptions? sessionOptions = null)
    {
        _options = options.Value;
        _ttl = TimeSpan.FromMinutes(_options.TtlMinutes > 0 ? _options.TtlMinutes : 30);
        _logger = logger;
        _sessionOptions = sessionOptions;

        _evictionTimer = new Timer(EvictIfIdle, null,
            dueTime: TimeSpan.FromMinutes(1),
            period: TimeSpan.FromMinutes(1));
    }

    // -------------------------------------------------------------------------
    // ISpeechSynthesizer
    // -------------------------------------------------------------------------

    /// <inheritdoc />
    public async Task<SynthesisResult> SynthesizeAsync(
        string text,
        string language,
        string voice = "",
        float speed = 1.0f,
        CancellationToken cancellationToken = default)
    {
        // Increment BEFORE the disposed check so Dispose() can spin-wait on
        // _activeInferences == 0 and be certain no new inferences will start
        // after it sets _disposed = true.
        Interlocked.Increment(ref _activeInferences);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (string.IsNullOrWhiteSpace(_options.ModelPath))
                throw new InvalidOperationException(
                    "Piper model path is not configured. " +
                    "Set Translation:TextToAudio and the corresponding Models entry in appsettings.json.");

            var (session, config) = await GetOrCreateSessionAsync(cancellationToken);

            // Step 1: Phonemise text → phoneme IDs.
            // "espeak": convert text to IPA via espeak-ng, then look up IPA chars in the map.
            // "text":   map raw text characters directly (e.g. Cyrillic for uk_UA voices).
            long[] phonemeIds;
            if (config.PhonemeType.Equals("text", StringComparison.OrdinalIgnoreCase))
            {
                phonemeIds = BuildPhonemeIdsFromText(text, config.PhonemeIdMap);
            }
            else
            {
                var ipaText = await EspeakPhonemizer.PhonemizeAsync(
                    text, config.Espeak.Voice, cancellationToken);
                phonemeIds = BuildPhonemeIds(ipaText, config.PhonemeIdMap);
            }

            // Step 3: Resolve speaker ID (0 for single-speaker voices)
            var speakerId = ResolveSpeakerId(voice, config);

            // Step 4: Run ONNX inference — serialised via _inferenceLock because
            // InferenceSession.Run() is not safe for concurrent invocations.
            var lengthScale = speed > 0f ? 1.0f / speed : 1.0f; // speed=2 → shorter → divide
            float[] pcmSamples;
            await _inferenceLock.WaitAsync(cancellationToken);
            try
            {
                pcmSamples = RunInference(session, phonemeIds, speakerId, config.Inference, lengthScale);
            }
            finally
            {
                _inferenceLock.Release();
            }

            // Step 5: Encode WAV
            var wavBytes = EncodeWav(pcmSamples, config.Audio.SampleRate);

            Interlocked.Exchange(ref _lastUsedTicks, DateTime.UtcNow.Ticks);

            return new SynthesisResult(wavBytes, config.Audio.SampleRate);
        }
        finally
        {
            Interlocked.Decrement(ref _activeInferences);
        }
    }

    // -------------------------------------------------------------------------
    // Phoneme ID builder
    // -------------------------------------------------------------------------

    /// <summary>
    /// Converts raw IPA output from espeak-ng into a flat array of phoneme IDs.
    /// BOS (<c>_</c> = 0) prepended, EOS (<c>$</c> = 2) appended.
    /// </summary>
    internal static long[] BuildPhonemeIds(
        string ipaText,
        IReadOnlyDictionary<string, long[]> phonemeIdMap)
    {
        // Piper convention (from piper1-gpl/src/piper/const.py + phoneme_ids.py):
        //   PAD = "_"  → ID 0   (word-break / padding separator)
        //   BOS = "^"  → ID 1   (beginning of sentence)
        //   EOS = "$"  → ID 2   (end of sentence)
        //   " "        → ID 3   (space between words — NOT the separator!)
        // Reference sequence: [BOS, PAD, phoneme, PAD, phoneme, PAD, ..., EOS]
        // The PAD immediately after BOS is required by the trained model.
        var bosIds = phonemeIdMap.TryGetValue("^", out var b) ? b : [1L];
        var eosIds = phonemeIdMap.TryGetValue("$", out var e) ? e : [2L];
        var padIds = phonemeIdMap.TryGetValue("_", out var p) ? p : [0L];

        // NFD-normalise IPA output: the Piper reference phonemizer applies
        // unicodedata.normalize("NFD", ...) before splitting into codepoints,
        // separating combining marks (e.g. ː̪) into individual characters so
        // they can be looked up separately in the phoneme_id_map.
        ipaText = ipaText.Normalize(NormalizationForm.FormD);

        // Greedy longest-match — try multi-char keys before single-char ones
        // to handle combined tokens from espeak-ng 1.52.
        var sortedKeys = phonemeIdMap.Keys
            .OrderByDescending(k => k.Length)
            .ToList();

        var ids = new List<long>();
        ids.AddRange(bosIds);   // BOS = "^"
        ids.AddRange(padIds);   // PAD after BOS (required by piper reference)

        int i = 0;
        while (i < ipaText.Length)
        {
            bool matched = false;
            foreach (var key in sortedKeys)
            {
                if (i + key.Length <= ipaText.Length &&
                    ipaText.AsSpan(i, key.Length).SequenceEqual(key.AsSpan()))
                {
                    ids.AddRange(phonemeIdMap[key]);
                    ids.AddRange(padIds);   // PAD after each phoneme token
                    i += key.Length;
                    matched = true;
                    break;
                }
            }
            if (!matched)
                i++; // skip unknown char
        }

        ids.AddRange(eosIds);   // EOS = "$"
        return [.. ids];
    }

    /// <summary>
    /// Builds phoneme IDs for voices with <c>"phoneme_type": "text"</c> (e.g. Ukrainian).
    /// Maps raw text characters directly to IDs using the voice's <c>phoneme_id_map</c>,
    /// bypassing espeak-ng entirely. Input is lowercased to match the map's case convention.
    /// BOS (<c>_</c> = 0) prepended, EOS (<c>$</c> = 2) appended.
    /// </summary>
    internal static long[] BuildPhonemeIdsFromText(
        string text,
        IReadOnlyDictionary<string, long[]> phonemeIdMap)
    {
        // Same BOS/PAD/EOS convention as BuildPhonemeIds (piper reference).
        var bosIds = phonemeIdMap.TryGetValue("^", out var b) ? b : [1L];
        var eosIds = phonemeIdMap.TryGetValue("$", out var e) ? e : [2L];
        var padIds = phonemeIdMap.TryGetValue("_", out var p) ? p : [0L];

        var ids = new List<long>();
        ids.AddRange(bosIds);
        ids.AddRange(padIds);   // PAD after BOS

        // NFD + lowercase: decompose combining chars, match map's lowercase keys.
        foreach (var ch in text.Normalize(NormalizationForm.FormD).ToLowerInvariant())
        {
            var key = ch.ToString();
            if (phonemeIdMap.TryGetValue(key, out var mapped))
            {
                ids.AddRange(mapped);
                ids.AddRange(padIds);
            }
            // Unknown chars silently skipped
        }

        ids.AddRange(eosIds);
        return [.. ids];
    }

    // -------------------------------------------------------------------------
    // Speaker resolution
    // -------------------------------------------------------------------------

    private static long ResolveSpeakerId(string voice, PiperVoiceConfig config)
    {
        if (config.NumSpeakers <= 1)
            return 0L;

        if (!string.IsNullOrWhiteSpace(voice) &&
            config.SpeakerIdMap.TryGetValue(voice, out var sid))
            return sid;

        // Default: first speaker (ID 0)
        return 0L;
    }

    // -------------------------------------------------------------------------
    // ONNX inference
    // -------------------------------------------------------------------------

    private static float[] RunInference(
        InferenceSession session,
        long[] phonemeIds,
        long speakerId,
        PiperVoiceConfig.InferenceSection inf,
        float lengthScaleOverride)
    {
        int n = phonemeIds.Length;

        // input: int64[1, N]
        var inputTensor = new DenseTensor<long>(phonemeIds, new int[] { 1, n });

        // input_lengths: int64[1]
        var lengthsTensor = new DenseTensor<long>(new long[] { (long)n }, new int[] { 1 });

        // scales: float32[3]  — [noise_scale, length_scale, noise_w]
        var scalesTensor = new DenseTensor<float>(
            new float[] { inf.NoiseScale, lengthScaleOverride * inf.LengthScale, inf.NoiseW },
            new int[] { 3 });

        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input", inputTensor),
            NamedOnnxValue.CreateFromTensor("input_lengths", lengthsTensor),
            NamedOnnxValue.CreateFromTensor("scales", scalesTensor),
        };

        // sid: int64[1]  — only for multi-speaker models
        var sessionInputNames = session.InputMetadata.Keys.ToHashSet(StringComparer.Ordinal);
        if (sessionInputNames.Contains("sid"))
        {
            var sidTensor = new DenseTensor<long>(new long[] { speakerId }, new int[] { 1 });
            inputs.Add(NamedOnnxValue.CreateFromTensor("sid", sidTensor));
        }

        using var results = session.Run(inputs);
        var outputValue = results.First(r => r.Name == "output");
        var rawTensor = outputValue.AsTensor<float>();

        // Shape: [1, 1, samples] — flatten to 1-D
        return rawTensor.ToArray();
    }

    // -------------------------------------------------------------------------
    // WAV encoding
    // -------------------------------------------------------------------------

    /// <summary>
    /// Converts raw PCM float32 samples to a 16-bit mono PCM WAV byte array.
    /// Writes the WAV header and sample data directly to avoid NAudio stream-disposal
    /// issues that arise when chaining WaveFileWriter with an intermediate MemoryStream.
    /// </summary>
    internal static byte[] EncodeWav(float[] pcmSamples, int sampleRate)
    {
        // Normalize float32 output to full int16 range.
        // Piper ONNX models output values in roughly [-0.2, +0.2]; without normalisation
        // the resulting audio is ~20% of maximum loudness. Peak normalisation brings the
        // loudest sample to 98% of full scale (leaving a small headroom to avoid clipping).
        const float headroom = 0.98f;
        float peak = 0f;
        for (int i = 0; i < pcmSamples.Length; i++)
        {
            var abs = Math.Abs(pcmSamples[i]);
            if (abs > peak) peak = abs;
        }
        float scale = peak > 1e-6f ? headroom / peak : 1f; // avoid div-by-zero on silence

        // Convert normalised float32 → int16 PCM
        var pcmShorts = new short[pcmSamples.Length];
        for (int i = 0; i < pcmSamples.Length; i++)
        {
            var normalised = Math.Clamp(pcmSamples[i] * scale, -1.0f, 1.0f);
            pcmShorts[i] = (short)(normalised * short.MaxValue);
        }

        int dataBytes = pcmShorts.Length * 2; // 2 bytes per int16 sample
        const int headerSize = 44;

        var buffer = new byte[headerSize + dataBytes];
        using var ms = new MemoryStream(buffer);
        using var bw = new BinaryWriter(ms, System.Text.Encoding.ASCII, leaveOpen: true);

        // RIFF chunk descriptor
        bw.Write((byte)'R'); bw.Write((byte)'I'); bw.Write((byte)'F'); bw.Write((byte)'F');
        bw.Write(36 + dataBytes);          // ChunkSize = 36 + SubChunk2Size
        bw.Write((byte)'W'); bw.Write((byte)'A'); bw.Write((byte)'V'); bw.Write((byte)'E');

        // fmt sub-chunk
        bw.Write((byte)'f'); bw.Write((byte)'m'); bw.Write((byte)'t'); bw.Write((byte)' ');
        bw.Write(16);                       // SubChunk1Size (16 for PCM)
        bw.Write((short)1);                 // AudioFormat: PCM = 1
        bw.Write((short)1);                 // NumChannels: mono = 1
        bw.Write(sampleRate);               // SampleRate
        bw.Write(sampleRate * 2);           // ByteRate = SampleRate × NumChannels × BitsPerSample/8
        bw.Write((short)2);                 // BlockAlign = NumChannels × BitsPerSample/8
        bw.Write((short)16);                // BitsPerSample

        // data sub-chunk
        bw.Write((byte)'d'); bw.Write((byte)'a'); bw.Write((byte)'t'); bw.Write((byte)'a');
        bw.Write(dataBytes);                // SubChunk2Size

        // PCM samples (little-endian int16)
        foreach (var sample in pcmShorts)
            bw.Write(sample);

        return buffer;
    }

    // -------------------------------------------------------------------------
    // Lazy load + TTL eviction
    // -------------------------------------------------------------------------

    private async Task<(InferenceSession session, PiperVoiceConfig config)> GetOrCreateSessionAsync(
        CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_session is null || _voiceConfig is null)
            {
                var modelPath = _options.ModelPath;
                var configPath = modelPath + ".json";

                if (!File.Exists(modelPath))
                    throw new FileNotFoundException(
                        $"Piper voice model not found at '{modelPath}'.", modelPath);

                if (!File.Exists(configPath))
                    throw new FileNotFoundException(
                        $"Piper voice config not found at '{configPath}'.", configPath);

#pragma warning disable CA1873
                _logger?.LogInformation(
                    "Loading Piper voice model from {Path}", modelPath);
#pragma warning restore CA1873

                _voiceConfig = PiperVoiceConfig.LoadFrom(configPath);
                _session = _sessionOptions is not null
                    ? new InferenceSession(modelPath, _sessionOptions)
                    : new InferenceSession(modelPath);
            }

            Interlocked.Exchange(ref _lastUsedTicks, DateTime.UtcNow.Ticks);
            return (_session, _voiceConfig);
        }
        finally
        {
            _lock.Release();
        }
    }

    private void EvictIfIdle(object? state)
    {
        if (_disposed || Volatile.Read(ref _activeInferences) > 0)
            return;

        if (!_lock.Wait(0))
            return;

        try
        {
            if (_session is null || Volatile.Read(ref _activeInferences) > 0)
                return;

            var lastUsed = new DateTime(Interlocked.Read(ref _lastUsedTicks), DateTimeKind.Utc);
            if (DateTime.UtcNow - lastUsed < _ttl)
                return;

#pragma warning disable CA1873
            _logger?.LogInformation("Evicting idle Piper voice (unused for {Ttl})", _ttl);
#pragma warning restore CA1873

            _session.Dispose();
            _session = null;
            _voiceConfig = null;
        }
        finally
        {
            _lock.Release();
        }
    }

    // -------------------------------------------------------------------------
    // IDisposable
    // -------------------------------------------------------------------------

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _evictionTimer.Dispose();

        // Spin until all in-flight SynthesizeAsync calls have decremented _activeInferences.
        // Because _activeInferences is incremented BEFORE the _disposed check, setting
        // _disposed = true above prevents new calls from proceeding past their ObjectDisposedException
        // check; the SpinWait here drains any that had already incremented the counter.
        var sw = new SpinWait();
        while (Volatile.Read(ref _activeInferences) > 0)
            sw.SpinOnce();

        // Acquire _inferenceLock to ensure RunInference has fully exited before we
        // dispose the session (RunInference holds this lock while calling session.Run).
        _inferenceLock.Wait();
        _inferenceLock.Release();
        _inferenceLock.Dispose();

        _lock.Wait();
        try
        {
            _session?.Dispose();
            _session = null;
        }
        finally
        {
            _lock.Release();
        }

        _lock.Dispose();
    }
}
