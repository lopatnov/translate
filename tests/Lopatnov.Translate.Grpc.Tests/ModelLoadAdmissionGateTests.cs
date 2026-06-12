using Lopatnov.Translate.Grpc.Memory;

namespace Lopatnov.Translate.Grpc.Tests;

/// <summary>
/// Unit tests for <see cref="ModelLoadAdmissionGate"/> — system-RAM admission control
/// in front of model factory invocation.
/// </summary>
public sealed class ModelLoadAdmissionGateTests
{
    private const long OneGb = 1L << 30;

    [Fact]
    public void UnknownFootprint_BypassesProbe_AndLoads()
    {
        int probeCalls = 0;
        var gate = new ModelLoadAdmissionGate(() => { probeCalls++; return OneGb; });

        var result = gate.Run("model", requiredBytes: 0, () => 42);

        Assert.Equal(42, result);
        Assert.Equal(0, probeCalls);
    }

    [Fact]
    public void SufficientMemory_RunsLoadAndReturnsResult()
    {
        var gate = new ModelLoadAdmissionGate(() => 10 * OneGb);

        var result = gate.Run("model", requiredBytes: OneGb, () => "loaded");

        Assert.Equal("loaded", result);
    }

    [Fact]
    public void InsufficientMemory_Throws_AndNeverInvokesLoad()
    {
        var gate = new ModelLoadAdmissionGate(() => OneGb);
        bool loaded = false;

        var ex = Assert.Throws<ModelMemoryBudgetException>(() =>
            gate.Run("m2m100_1.2B", requiredBytes: 10 * OneGb, () => loaded = true));

        Assert.False(loaded);
        Assert.Contains("m2m100_1.2B", ex.Message);
        Assert.Contains("10240", ex.Message); // required MB
        Assert.Contains("1024", ex.Message);  // available MB
    }

    [Fact]
    public void UnknownAvailability_AdmitsOptimistically()
    {
        var gate = new ModelLoadAdmissionGate(() => null);

        Assert.True(gate.Run("model", requiredBytes: 100 * OneGb, () => true));
    }

    [Fact]
    public void ThrowingProbe_AdmitsOptimistically()
    {
        var gate = new ModelLoadAdmissionGate(() => throw new InvalidOperationException("probe broken"));

        Assert.True(gate.Run("model", requiredBytes: 100 * OneGb, () => true));
    }
}
