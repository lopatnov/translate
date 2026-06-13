using Lopatnov.Translate.Grpc.Memory;
using Microsoft.Extensions.Logging;

namespace Lopatnov.Translate.Grpc.Tests;

/// <summary>
/// Unit tests for the memory-aware policy of <see cref="OnnxExecutionProviderHelper"/>,
/// using fake GPU probes so no GPU hardware is required:
/// auto → CPU fallback on a known shortfall, explicit GPU → fail fast.
/// </summary>
public sealed class OnnxExecutionProviderHelperMemoryTests
{
    private const long OneGb = 1L << 30;

    private sealed class FakeGpuProbe(long? freeBytes) : IGpuMemoryProbe
    {
        public int Calls { get; private set; }

        public long? GetFreeBytes()
        {
            Calls++;
            return freeBytes;
        }
    }

    private sealed class CapturingLogger : ILogger
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }

    private static OnnxMemoryPolicy Needs(long bytes) => new() { RequiredBytes = bytes };

    // ── Explicit GPU providers: fail fast on a known shortfall ───────────────

    [Fact]
    public void ExplicitDirectMl_InsufficientFreeMemory_FailsFast()
    {
        var probe = new FakeGpuProbe(OneGb);

        var ex = Assert.Throws<ModelMemoryBudgetException>(() =>
            OnnxExecutionProviderHelper.BuildSessionOptions(
                "directml", Needs(10 * OneGb), probe, probe, logger: null));

        Assert.Contains("DirectML", ex.Message);
        Assert.Contains("10240", ex.Message); // required MB
        Assert.Contains("1024", ex.Message);  // free MB
    }

    [Fact]
    public void ExplicitCuda_InsufficientFreeMemory_FailsFast()
    {
        var probe = new FakeGpuProbe(OneGb);

        var ex = Assert.Throws<ModelMemoryBudgetException>(() =>
            OnnxExecutionProviderHelper.BuildSessionOptions(
                "cuda", Needs(10 * OneGb), probe, probe, logger: null));

        Assert.Contains("CUDA", ex.Message);
    }

    [Fact]
    public void ExplicitGpu_FreeMemoryUnknown_DoesNotThrow()
    {
        var probe = new FakeGpuProbe(null);

        var ex = Record.Exception(() =>
        {
            using var opts = OnnxExecutionProviderHelper.BuildSessionOptions(
                "directml", Needs(10 * OneGb), probe, probe, logger: null);
            Assert.NotNull(opts);
        });

        Assert.Null(ex);
    }

    [Fact]
    public void ExplicitGpu_SufficientFreeMemory_DoesNotThrow()
    {
        var probe = new FakeGpuProbe(10 * OneGb);

        // Provider append may still fail on a GPU-less machine — that falls back to CPU
        // with a warning, exactly as before; the memory check itself must pass silently.
        var ex = Record.Exception(() =>
        {
            using var opts = OnnxExecutionProviderHelper.BuildSessionOptions(
                "cuda", Needs(OneGb), probe, probe, logger: null);
            Assert.NotNull(opts);
        });

        Assert.Null(ex);
    }

    // ── Auto: skip the GPU candidate and fall through to CPU ─────────────────

    [Fact]
    public void Auto_InsufficientFreeMemory_FallsBackToCpu_WithWarning()
    {
        var probe = new FakeGpuProbe(OneGb);
        var logger = new CapturingLogger();

        using var opts = OnnxExecutionProviderHelper.BuildSessionOptions(
            "auto", Needs(10 * OneGb), probe, probe, logger);

        Assert.NotNull(opts);
        Assert.Contains(logger.Entries, e =>
            e.Level == LogLevel.Warning && e.Message.Contains("falling back to CPU"));
        Assert.Contains(logger.Entries, e =>
            e.Level == LogLevel.Information && e.Message.Contains("auto-selected: CPU"));
    }

    [Fact]
    public void Auto_UnknownFootprint_NeverProbes()
    {
        var probe = new FakeGpuProbe(OneGb);

        using var opts = OnnxExecutionProviderHelper.BuildSessionOptions(
            "auto", OnnxMemoryPolicy.None, probe, probe, logger: null);

        Assert.NotNull(opts);
        Assert.Equal(0, probe.Calls);
    }

    // ── CPU: memory policy never applies ─────────────────────────────────────

    [Fact]
    public void Cpu_WithHugeFootprint_NeverProbes_NeverThrows()
    {
        var probe = new FakeGpuProbe(OneGb);

        using var opts = OnnxExecutionProviderHelper.BuildSessionOptions(
            "cpu", Needs(1000 * OneGb), probe, probe, logger: null);

        Assert.NotNull(opts);
        Assert.Equal(0, probe.Calls);
    }
}
