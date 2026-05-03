using System.Collections.Concurrent;
using Lopatnov.Translate.Core.Abstractions;

namespace Lopatnov.Translate.Grpc.Services;

/// <summary>
/// Lazily initialises text-translation providers on first use and evicts them after a
/// configurable idle TTL to reclaim ONNX model memory.
///
/// Thread safety notes:
///   - ConcurrentDictionary + Lazy&lt;T&gt; (ExecutionAndPublication) guarantee a single model
///     load per provider, even under concurrent first-request bursts.
///   - Eviction uses TryRemove(KeyValuePair) so only the exact entry that was found idle
///     is removed; a racing GetOrAdd that re-added the key is left untouched.
///   - Rent() returns a TranslatorLease that holds a reference count on the entry. The
///     underlying model is only disposed once all active leases are released AND the entry
///     has been evicted. This prevents ObjectDisposedException in in-flight requests.
/// </summary>
public sealed class ModelSessionManager : IDisposable
{
    /// <summary>
    /// Scoped handle to a translator. Dispose when the request is complete to release
    /// the reference so TTL-eviction can reclaim memory.
    /// </summary>
    public sealed class TranslatorLease : IDisposable
    {
        private readonly Action _release;
        private int _disposed;

        public ITextTranslator Translator { get; }
        public string Key { get; }

        internal TranslatorLease(Action release, ITextTranslator translator, string key)
        {
            _release = release;
            Translator = translator;
            Key = key;
        }

        public void Dispose()
        {
            if (Interlocked.CompareExchange(ref _disposed, 1, 0) == 0)
                _release();
        }
    }

    private sealed class Entry(Func<ITextTranslator> factory)
    {
        private readonly Lazy<ITextTranslator> _lazy =
            new(factory, LazyThreadSafetyMode.ExecutionAndPublication);
        private readonly object _refLock = new();
        private int _refCount;
        private int _disposeFlag;

        public volatile bool Disposed;
        private long _lastUsedTicks = DateTimeOffset.UtcNow.UtcTicks;

        public DateTimeOffset LastUsed
        {
            get => new(Interlocked.Read(ref _lastUsedTicks), TimeSpan.Zero);
            set => Interlocked.Exchange(ref _lastUsedTicks, value.UtcTicks);
        }

        public ITextTranslator Translator => _lazy.Value;

        // Returns false if the entry has already been evicted.
        public bool TryAcquire()
        {
            lock (_refLock)
            {
                if (Disposed) return false;
                _refCount++;
                return true;
            }
        }

        public void Release()
        {
            bool shouldDispose;
            lock (_refLock)
            {
                _refCount--;
                shouldDispose = _refCount == 0 && Disposed;
            }
            if (shouldDispose) DisposeOnce();
        }

        // Called by the TTL eviction timer — defers dispose if refs are active.
        public void EvictAndDispose()
        {
            bool shouldDispose;
            lock (_refLock)
            {
                Disposed = true;
                shouldDispose = _refCount == 0;
            }
            if (shouldDispose) DisposeOnce();
        }

        // Called by manager.Dispose() — immediate, regardless of active refs.
        public void ForceDispose()
        {
            Disposed = true;
            DisposeOnce();
        }

        private void DisposeOnce()
        {
            if (Interlocked.CompareExchange(ref _disposeFlag, 1, 0) == 0 &&
                _lazy.IsValueCreated && _lazy.Value is IDisposable d)
                d.Dispose();
        }
    }

    private readonly IReadOnlyDictionary<string, Func<ITextTranslator>> _factories;
    private readonly HashSet<string> _configured; // all names from config, for error messages
    private readonly ConcurrentDictionary<string, Entry> _sessions =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly TimeSpan _ttl;
    private readonly Timer _evictionTimer;

    public ModelSessionManager(
        IReadOnlyDictionary<string, Func<ITextTranslator>> factories,
        IEnumerable<string> allowedModels,
        TimeSpan ttl)
    {
        _configured = new HashSet<string>(factories.Keys, StringComparer.OrdinalIgnoreCase);

        var allowed = new HashSet<string>(allowedModels, StringComparer.OrdinalIgnoreCase);
        _factories = new Dictionary<string, Func<ITextTranslator>>(
            allowed.Count == 0
                ? factories
                : factories.Where(kv => allowed.Contains(kv.Key))
                           .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);

        _ttl = ttl;
        var interval = ttl < TimeSpan.FromMinutes(1) ? TimeSpan.FromMinutes(1) : ttl;
        _evictionTimer = new Timer(_ => Evict(), null, interval, interval);
    }

    /// <summary>
    /// Returns (or lazily creates) the translator for the given provider key.
    /// No reference counting — use <see cref="Rent"/> in request handlers instead.
    /// </summary>
    /// <exception cref="KeyNotFoundException">Provider is not configured.</exception>
    /// <exception cref="UnauthorizedAccessException">Provider is configured but not in the allowlist.</exception>
    public ITextTranslator Get(string key)
    {
        if (!_configured.Contains(key))
            throw new KeyNotFoundException($"Provider '{key}' is not configured.");
        if (!_factories.ContainsKey(key))
            throw new UnauthorizedAccessException($"Provider '{key}' is not in the allowed list.");

        while (true)
        {
            var entry = _sessions.GetOrAdd(key, k => new Entry(_factories[k]));
            entry.LastUsed = DateTimeOffset.UtcNow;

            var translator = entry.Translator; // blocks until model is loaded (Lazy<T>)
            if (!entry.Disposed)
                return translator;

            // Race: eviction fired between GetOrAdd and .Value. Remove and retry.
            _sessions.TryRemove(KeyValuePair.Create(key, entry));
        }
    }

    /// <summary>
    /// Returns a ref-counted lease for the translator. Dispose the lease when the
    /// request completes so TTL-eviction can safely reclaim model memory.
    /// </summary>
    /// <exception cref="KeyNotFoundException">Provider is not configured.</exception>
    /// <exception cref="UnauthorizedAccessException">Provider is configured but not in the allowlist.</exception>
    public TranslatorLease Rent(string key)
    {
        if (!_configured.Contains(key))
            throw new KeyNotFoundException($"Provider '{key}' is not configured.");
        if (!_factories.ContainsKey(key))
            throw new UnauthorizedAccessException($"Provider '{key}' is not in the allowed list.");

        while (true)
        {
            var entry = _sessions.GetOrAdd(key, k => new Entry(_factories[k]));
            entry.LastUsed = DateTimeOffset.UtcNow;

            if (entry.TryAcquire())
            {
                var translator = entry.Translator; // may block on first load
                return new TranslatorLease(entry.Release, translator, key);
            }

            // Entry was evicted between GetOrAdd and TryAcquire; remove and retry.
            _sessions.TryRemove(KeyValuePair.Create(key, entry));
        }
    }

    /// <summary>
    /// Returns the keys of models that are allowed (or all configured, if no allowlist).
    /// </summary>
    public IReadOnlyList<string> GetAvailableModels() => _factories.Keys.ToArray();

    private void Evict()
    {
        var cutoff = DateTimeOffset.UtcNow - _ttl;
        foreach (var (key, entry) in _sessions)
        {
            if (entry.LastUsed < cutoff && _sessions.TryRemove(KeyValuePair.Create(key, entry)))
                entry.EvictAndDispose();
        }
    }

    public void Dispose()
    {
        _evictionTimer.Dispose();
        foreach (var (_, entry) in _sessions)
            entry.ForceDispose();
        _sessions.Clear();
    }
}
