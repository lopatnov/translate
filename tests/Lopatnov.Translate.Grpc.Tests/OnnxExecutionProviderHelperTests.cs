namespace Lopatnov.Translate.Grpc.Tests;

/// <summary>
/// Unit tests for <see cref="OnnxExecutionProviderHelper"/>.
/// All tests run without GPU hardware — the helper must never throw,
/// auto-detecting or falling back to CPU gracefully.
/// </summary>
public sealed class OnnxExecutionProviderHelperTests
{
    // ── Auto / empty ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("auto")]
    [InlineData("Auto")]
    [InlineData("AUTO")]
    public void BuildSessionOptions_AutoOrEmpty_ReturnsOptionsWithoutThrowing(string provider)
    {
        // Auto-detect tries GPU and falls back to CPU — must never throw.
        var ex = Record.Exception(() =>
        {
            using var opts = OnnxExecutionProviderHelper.BuildSessionOptions(provider);
            Assert.NotNull(opts);
        });
        Assert.Null(ex);
    }

    // ── Explicit CPU ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData("cpu")]
    [InlineData("CPU")]
    [InlineData("Cpu")]
    public void BuildSessionOptions_ExplicitCpu_ReturnsOptionsWithoutThrowing(string provider)
    {
        using var opts = OnnxExecutionProviderHelper.BuildSessionOptions(provider);
        Assert.NotNull(opts);
    }

    // ── Explicit GPU providers — may not be available, must not throw ────────

    [Theory]
    [InlineData("directml")]
    [InlineData("DirectML")]
    [InlineData("DIRECTML")]
    public void BuildSessionOptions_DirectMl_DoesNotThrow(string provider)
    {
        // On a CPU-only or Linux build DirectML is unavailable — warns and falls back.
        var ex = Record.Exception(() =>
        {
            using var opts = OnnxExecutionProviderHelper.BuildSessionOptions(provider);
        });
        Assert.Null(ex);
    }

    [Theory]
    [InlineData("cuda")]
    [InlineData("CUDA")]
    public void BuildSessionOptions_Cuda_DoesNotThrow(string provider)
    {
        var ex = Record.Exception(() =>
        {
            using var opts = OnnxExecutionProviderHelper.BuildSessionOptions(provider);
        });
        Assert.Null(ex);
    }

    // ── Unknown values → ArgumentException ──────────────────────────────────

    [Theory]
    [InlineData("tensorrt")]
    [InlineData("openvino")]
    [InlineData("coreml")]
    [InlineData("unknown")]
    public void BuildSessionOptions_UnknownProvider_ThrowsArgumentException(string provider)
    {
        var ex = Assert.Throws<ArgumentException>(() =>
            OnnxExecutionProviderHelper.BuildSessionOptions(provider));

        Assert.Contains("Valid values: auto, cpu, directml, cuda", ex.Message);
        Assert.Contains(provider, ex.Message);
    }
}
