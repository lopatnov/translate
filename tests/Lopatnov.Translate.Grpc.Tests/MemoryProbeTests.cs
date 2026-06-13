using Lopatnov.Translate.Grpc.Memory;

namespace Lopatnov.Translate.Grpc.Tests;

/// <summary>
/// Smoke tests for the real memory probes. They must never throw on any machine —
/// hardware or driver absence degrades to <c>null</c> ("unknown"), never to an exception.
/// </summary>
public sealed class MemoryProbeTests
{
    [Fact]
    public void SystemMemoryProbe_ReportsNonNegativeAvailableBytes()
    {
        long? available = SystemMemoryProbe.GetAvailableBytes();

        // Windows and Linux are both implemented; other platforms may return null.
        if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux())
        {
            Assert.NotNull(available);
            Assert.True(available >= 0, $"expected non-negative available memory, got {available}");
        }
    }

    [Fact]
    public void CudaMemoryProbe_NeverThrows_ReturnsNullOrNonNegative()
    {
        long? free = null;
        var ex = Record.Exception(() => free = new CudaMemoryProbe().GetFreeBytes());

        Assert.Null(ex);
        Assert.True(free is null or >= 0, $"unexpected value {free}");
    }

    [Fact]
    public void DirectMlMemoryProbe_NeverThrows_ReturnsNullOrNonNegative()
    {
        long? free = null;
        var ex = Record.Exception(() => free = new DirectMlMemoryProbe().GetFreeBytes());

        Assert.Null(ex);
        Assert.True(free is null or >= 0, $"unexpected value {free}");

        if (!OperatingSystem.IsWindows())
            Assert.Null(free); // DXGI is Windows-only
    }
}
