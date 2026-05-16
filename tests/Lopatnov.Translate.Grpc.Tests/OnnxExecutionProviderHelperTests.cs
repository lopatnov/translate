namespace Lopatnov.Translate.Grpc.Tests;

/// <summary>
/// Unit tests for <see cref="OnnxExecutionProviderHelper"/>.
/// All tests run without GPU hardware — the helper must never throw,
/// gracefully falling back to CPU when an EP is unavailable.
/// </summary>
public sealed class OnnxExecutionProviderHelperTests
{
    [Theory]
    [InlineData("cpu")]
    [InlineData("CPU")]
    [InlineData("Cpu")]
    public void BuildSessionOptions_CpuVariants_ReturnsOptionsWithoutThrowing(string provider)
    {
        var opts = OnnxExecutionProviderHelper.BuildSessionOptions(provider);
        Assert.NotNull(opts);
        opts.Dispose();
    }

    [Theory]
    [InlineData("directml")]
    [InlineData("DirectML")]
    [InlineData("DIRECTML")]
    public void BuildSessionOptions_DirectMl_DoesNotThrow(string provider)
    {
        // On a CPU-only runtime the EP registration may be a no-op or produce a warning.
        // The helper must catch any OnnxRuntimeException / DllNotFoundException and fall back.
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

    [Theory]
    [InlineData("unknown")]
    [InlineData("tensorrt")]
    [InlineData("")]
    [InlineData("   ")]
    public void BuildSessionOptions_UnknownProvider_FallsBackToCpuWithoutThrowing(string provider)
    {
        var ex = Record.Exception(() =>
        {
            using var opts = OnnxExecutionProviderHelper.BuildSessionOptions(provider);
        });
        Assert.Null(ex);
    }

    [Fact]
    public void BuildSessionOptions_ReturnsSessionOptions_NotNull()
    {
        using var opts = OnnxExecutionProviderHelper.BuildSessionOptions("cpu");
        Assert.NotNull(opts);
    }
}
