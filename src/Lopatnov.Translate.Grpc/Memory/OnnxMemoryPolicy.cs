namespace Lopatnov.Translate.Grpc.Memory;

/// <summary>
/// Memory constraints for a single
/// <see cref="OnnxExecutionProviderHelper.BuildSessionOptions(string, OnnxMemoryPolicy, ILogger)"/>
/// call.
/// </summary>
public sealed record OnnxMemoryPolicy
{
    /// <summary>No memory checks and no CUDA arena cap — the pre-memory-awareness behaviour.</summary>
    public static OnnxMemoryPolicy None { get; } = new();

    /// <summary>
    /// Estimated bytes the model needs on its execution device (weight files × overhead
    /// factor). 0 = unknown → all memory checks are skipped.
    /// </summary>
    public long RequiredBytes { get; init; }

    /// <summary>
    /// Hard cap for the CUDA execution provider's memory arena
    /// (ORT <c>gpu_mem_limit</c>), in bytes. 0 = no cap.
    /// </summary>
    public long CudaGpuMemLimitBytes { get; init; }
}
