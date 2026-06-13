namespace Lopatnov.Translate.Grpc.Memory;

/// <summary>
/// Queries how much device memory is still allocatable on the GPU a given ONNX
/// execution provider would use.
/// </summary>
public interface IGpuMemoryProbe
{
    /// <summary>
    /// Free (allocatable) device memory in bytes, or <c>null</c> when it cannot be
    /// determined (no device, driver API unavailable, unsupported OS).
    /// Callers must treat <c>null</c> as "unknown" and proceed optimistically.
    /// </summary>
    long? GetFreeBytes();
}
