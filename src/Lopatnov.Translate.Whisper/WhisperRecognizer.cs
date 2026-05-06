using Lopatnov.Translate.Core.Abstractions;
using Lopatnov.Translate.Core.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using Whisper.net;

namespace Lopatnov.Translate.Whisper;

/// <summary>
/// Speech-to-text recognizer backed by a local Whisper ggml model via Whisper.net.
///
/// Loading is lazy: the WhisperFactory (model weights) is only allocated on the first
/// call to <see cref="TranscribeAsync"/>. After <see cref="WhisperOptions.TtlMinutes"/>
/// of inactivity the factory is disposed and memory is released; the next call reloads it.
/// This mirrors the ModelSessionManager TTL pattern used for translation models.
/// </summary>
public sealed class WhisperRecognizer : ISpeechRecognizer, IDisposable
{
    private readonly WhisperOptions _options;
    private readonly TimeSpan _ttl;
    private readonly ILogger<WhisperRecognizer>? _logger;

    // Lazy load + TTL eviction state
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly Timer _evictionTimer;
    private WhisperFactory? _factory;
    private long _lastUsedTicks;                  // Volatile.Read/Write via Interlocked
    private int _activeInferences;               // Interlocked counter; prevents eviction while busy
    private bool _disposed;

    public WhisperRecognizer(
        IOptions<WhisperOptions> options,
        ILogger<WhisperRecognizer>? logger = null)
    {
        _options = options.Value;
        _ttl     = TimeSpan.FromMinutes(_options.TtlMinutes > 0 ? _options.TtlMinutes : 30);
        _logger  = logger;

        // Check eviction once per minute; actual eviction only after _ttl of inactivity.
        _evictionTimer = new Timer(EvictIfIdle, null,
            dueTime: TimeSpan.FromMinutes(1),
            period:  TimeSpan.FromMinutes(1));
    }

    // -------------------------------------------------------------------------
    // ISpeechRecognizer
    // -------------------------------------------------------------------------

    /// <inheritdoc />
    public async Task<TranscriptionResult> TranscribeAsync(
        byte[] audioData,
        string language = "auto",
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (string.IsNullOrWhiteSpace(_options.ModelPath))
            throw new InvalidOperationException(
                "Whisper model path is not configured. " +
                "Set Translation:AudioToText and the corresponding Models entry in appsettings.json.");

        // Track active inferences so the eviction timer won't dispose the factory mid-run.
        Interlocked.Increment(ref _activeInferences);
        try
        {
            // Ensure factory is loaded (lazy init under lock).
            var factory = await GetOrCreateFactoryAsync(cancellationToken);

            var samples = ResampleToWhisperFormat(audioData);

            var builderCfg = factory.CreateBuilder();
            if (!string.IsNullOrWhiteSpace(language) &&
                !language.Equals("auto", StringComparison.OrdinalIgnoreCase))
            {
                builderCfg = builderCfg.WithLanguage(language);
            }

            using var processor = builderCfg.Build();

            var segments        = new List<TranscriptionSegment>();
            string? detectedLang = null;

            await foreach (var seg in processor.ProcessAsync(samples, cancellationToken))
            {
                segments.Add(new TranscriptionSegment(
                    seg.Text.Trim(),
                    (float)seg.Start.TotalSeconds,
                    (float)seg.End.TotalSeconds));

                detectedLang ??= seg.Language;
            }

            // Update last-used timestamp after a successful inference.
            Interlocked.Exchange(ref _lastUsedTicks, DateTime.UtcNow.Ticks);

            var fullText = string.Join(" ", segments.Select(s => s.Text));
            return new TranscriptionResult(segments, detectedLang ?? string.Empty, fullText);
        }
        finally
        {
            Interlocked.Decrement(ref _activeInferences);
        }
    }

    // -------------------------------------------------------------------------
    // Audio preprocessing
    // -------------------------------------------------------------------------

    /// <summary>
    /// Decodes <paramref name="audioData"/> (WAV bytes) and resamples to
    /// 16 kHz mono PCM float32 as required by Whisper.
    /// </summary>
    internal static float[] ResampleToWhisperFormat(byte[] audioData)
    {
        using var ms     = new MemoryStream(audioData);
        using var reader = new WaveFileReader(ms);

        ISampleProvider provider = reader.ToSampleProvider();

        // Downmix to mono if needed.
        // StereoToMonoSampleProvider only accepts exactly 2-channel input,
        // so multichannel (>2) audio is first routed through MultiplexingSampleProvider
        // to extract the first two channels before the stereo→mono step.
        if (reader.WaveFormat.Channels > 2)
        {
            var stereo = new MultiplexingSampleProvider(new[] { provider }, 2);
            stereo.ConnectInputToOutput(0, 0);
            stereo.ConnectInputToOutput(1, 1);
            provider = new StereoToMonoSampleProvider(stereo);
        }
        else if (reader.WaveFormat.Channels == 2)
        {
            provider = new StereoToMonoSampleProvider(provider);
        }

        // Resample to 16 kHz if needed (WdlResamplingSampleProvider is pure managed / cross-platform)
        if (reader.WaveFormat.SampleRate != 16_000)
            provider = new WdlResamplingSampleProvider(provider, 16_000);

        // Pre-allocate based on estimated output length: mono frames * (16 kHz / src rate).
        // SampleCount is total interleaved samples (frames × channels), so divide by Channels
        // to get the frame count before the stereo→mono step.
        const int bufferSize = 4096;
        int estimatedSamples = (int)(reader.SampleCount / reader.WaveFormat.Channels
                                     * (16_000.0 / reader.WaveFormat.SampleRate))
                               + bufferSize; // safety margin
        var resultBuffer = new float[estimatedSamples];
        int totalWritten = 0;
        var readBuffer = new float[bufferSize];
        int read;
        while ((read = provider.Read(readBuffer, 0, bufferSize)) > 0)
        {
            if (totalWritten + read > resultBuffer.Length)
                Array.Resize(ref resultBuffer, Math.Max(resultBuffer.Length * 2, totalWritten + read));
            readBuffer.AsSpan(0, read).CopyTo(resultBuffer.AsSpan(totalWritten));
            totalWritten += read;
        }

        return resultBuffer[..totalWritten];
    }

    // -------------------------------------------------------------------------
    // Lazy load + TTL eviction
    // -------------------------------------------------------------------------

    private async Task<WhisperFactory> GetOrCreateFactoryAsync(CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            // Double-check after acquiring the lock: Dispose() sets _disposed=true
            // before taking the lock, so this prevents using a factory that was
            // disposed between our ObjectDisposedException check and this point.
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_factory is null)
            {
                if (_logger is { } log)
                    log.LogInformation("Loading Whisper model from {Path}", _options.ModelPath);
                _factory = CreateFactory();
            }

            Interlocked.Exchange(ref _lastUsedTicks, DateTime.UtcNow.Ticks);
            return _factory;
        }
        finally
        {
            _lock.Release();
        }
    }

    private WhisperFactory CreateFactory()
    {
        if (!File.Exists(_options.ModelPath))
            throw new FileNotFoundException(
                $"Whisper model not found at '{_options.ModelPath}'. " +
                "Run scripts/download-whisper.ps1 to fetch it.",
                _options.ModelPath);

        return WhisperFactory.FromPath(_options.ModelPath);
    }

    /// <summary>Timer callback: dispose the factory if idle longer than TTL.</summary>
    private void EvictIfIdle(object? state)
    {
        // Fast path: skip if busy or already disposed.
        if (_disposed || Volatile.Read(ref _activeInferences) > 0)
            return;

        // Try to acquire the lock without blocking the timer thread.
        if (!_lock.Wait(0))
            return;

        try
        {
            if (_factory is null || Volatile.Read(ref _activeInferences) > 0)
                return;

            var lastUsed = new DateTime(Interlocked.Read(ref _lastUsedTicks), DateTimeKind.Utc);
            if (DateTime.UtcNow - lastUsed < _ttl)
                return;

            if (_logger is { } log)
                log.LogInformation("Evicting idle Whisper model (unused for {Ttl})", _ttl);

            _factory.Dispose();
            _factory = null;
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

        _lock.Wait();
        try
        {
            _factory?.Dispose();
            _factory = null;
        }
        finally
        {
            _lock.Release();
        }

        _lock.Dispose();
    }
}
