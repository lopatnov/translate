using Lopatnov.Translate.Grpc.Memory;
using Lopatnov.Translate.Grpc.Services;
using Microsoft.Extensions.Options;
using Moq;

namespace Lopatnov.Translate.Grpc.Tests;

/// <summary>
/// Exercises the memory wiring of <see cref="ModelBootstrap.BuildSessionManager"/>:
/// footprint estimation and the admission gate run inside the NLLB/M2M100 factories.
/// Uses tiny fake weight files — the model itself cannot load, but a footprint of a few
/// hundred bytes must always be admitted, never rejected by the gate.
/// </summary>
public sealed class ModelBootstrapMemoryTests : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("bootstrap-mem-tests-");

    public void Dispose() => _dir.Delete(recursive: true);

    private void CreateFile(string name, int bytes) =>
        File.WriteAllBytes(Path.Combine(_dir.FullName, name), new byte[bytes]);

    private static IServiceProvider ServiceProviderWith(TranslationOptions options)
    {
        // Loose mock: every unconfigured GetService returns null, which BuildSessionManager
        // tolerates for optional services (ILoggerFactory).
        var sp = new Mock<IServiceProvider>(MockBehavior.Loose);
        sp.Setup(s => s.GetService(typeof(IOptions<TranslationOptions>)))
          .Returns(Options.Create(options));
        return sp.Object;
    }

    private ModelSessionManager BuildManager(string type, TranslationOptions options)
    {
        var rawModels = new Dictionary<string, ModelConfig>
        {
            ["model-under-test"] = new()
            {
                Type = type,
                Path = _dir.FullName,
                ExecutionProvider = "cpu", // deterministic: no GPU probing in this test
            },
        };
        return ModelBootstrap.BuildSessionManager(ServiceProviderWith(options), rawModels, p => p);
    }

    private void AssertTinyModelIsAdmitted(string type, TranslationOptions options)
    {
        CreateFile("encoder_model.onnx", 64);
        CreateFile("encoder_model.onnx_data", 128); // external-data companion is picked up
        CreateFile("decoder_model.onnx", 64);

        using var manager = BuildManager(type, options);

        // The fake weight files cannot load as a real ONNX model, so the factory is
        // expected to fail — but with a model-load error, never a gate rejection.
        var ex = Record.Exception(() => manager.Get("model-under-test"));
        Assert.False(ex is ModelMemoryBudgetException,
            $"a ~256-byte footprint must be admitted, got: {ex?.Message}");
    }

    [Fact]
    public void NllbFactory_TinyModel_IsAdmittedThroughGate() =>
        AssertTinyModelIsAdmitted(ModelType.NLLB, new TranslationOptions());

    [Fact]
    public void M2M100Factory_TinyModel_IsAdmittedThroughGate() =>
        AssertTinyModelIsAdmitted(ModelType.M2M100, new TranslationOptions());

    [Fact]
    public void DisabledMemoryPolicy_BypassesEstimationAndGate() =>
        AssertTinyModelIsAdmitted(ModelType.NLLB, new TranslationOptions
        {
            MemoryPolicy = new ModelMemoryPolicyOptions { Enabled = false },
        });
}
