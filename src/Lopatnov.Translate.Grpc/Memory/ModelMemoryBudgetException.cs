namespace Lopatnov.Translate.Grpc.Memory;

/// <summary>
/// Thrown when a model is denied loading because its estimated memory footprint exceeds
/// the free memory of the target device (GPU VRAM for an explicitly requested execution
/// provider, or system RAM at the load-admission gate). The condition is transient —
/// TTL eviction of idle models frees memory — so the gRPC layer maps it to
/// <c>ResourceExhausted</c>, which clients may retry later.
/// </summary>
public sealed class ModelMemoryBudgetException(string message) : InvalidOperationException(message);
