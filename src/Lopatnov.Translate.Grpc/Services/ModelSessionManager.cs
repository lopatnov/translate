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
///   - Best-effort TTL: a translator instance may be disposed while a long translation is
///     in-flight if the TTL fires during the request. Set ModelTtlMinutes to a value well
///     above your longest expected translation time (default: 30 min).
/// </summary>
public sealed class ModelSessionManager : IDisposable
{
    private sealed class Entry(Func<ITextTranslator> factory)
    {
        private readonly Lazy<ITextTranslator> _lazy =
            new(factory, LazyThreadSafetyMode.ExecutionAndPublication);

        public volatile bool Disposed;
        private long _lastUsedTicks = DateTimeOffset.UtcNow.UtcTicks;

        public DateTimeOffset LastUsed
        {
            get => new(Interlocked.Read(ref _lastUsedTicks), TimeSpan.Zero);
            set => Interlocked.Exchange(ref _lastUsedTicks, value.UtcTicks);
        }

        public ITextTranslator Translator => _lazy.Value;
        public bool IsValueCreated => _lazy.IsValueCreated;

        public void EvictAndDispose()
        {
            Disposed = true;
            if (_lazy.IsValueCreated && _lazy.Value is IDisposable d)
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
            entry.EvictAndDispose();
        _sessions.Clear();
    }
}
