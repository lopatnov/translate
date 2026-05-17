using System.Collections.Concurrent;

namespace Lopatnov.Translate.Grpc;

/// <summary>
/// Singleton that tracks in-flight redirect request IDs to detect routing cycles.
///
/// When a Redirect model forwards a request it registers the request ID before
/// the outgoing gRPC call and removes it in a <c>finally</c> block.  If a later
/// request arrives at this server carrying an ID that is still registered here,
/// the request has looped back — a cycle is reported.
///
/// The ID is propagated via the <c>x-redirect-id</c> gRPC metadata header so
/// cycles spanning multiple hops (A → B → C → A) are also detected when the
/// request returns to the originating server.
/// </summary>
public sealed class RedirectCycleDetector
{
    private readonly ConcurrentDictionary<string, byte> _pending = new(StringComparer.Ordinal);

    /// <summary>Returns <c>true</c> if <paramref name="requestId"/> is currently in-flight.</summary>
    public bool IsActive(string requestId) => _pending.ContainsKey(requestId);

    /// <summary>
    /// Registers <paramref name="requestId"/> as in-flight.
    /// Returns <c>true</c> if the ID was new (normal case),
    /// <c>false</c> if it was already registered (should not happen under normal operation).
    /// </summary>
    public bool TryRegister(string requestId) => _pending.TryAdd(requestId, 0);

    /// <summary>Removes <paramref name="requestId"/> from the in-flight set.</summary>
    public void Complete(string requestId) => _pending.TryRemove(requestId, out _);
}
