using Lopatnov.Translate.Grpc.Memory;

namespace Lopatnov.Translate.Grpc.Tests;

/// <summary>
/// Unit tests for <see cref="ModelFootprintEstimator"/> — file-size based footprint
/// estimation including optimum-style external-data companion files.
/// </summary>
public sealed class ModelFootprintEstimatorTests : IDisposable
{
    private readonly DirectoryInfo _dir = Directory.CreateTempSubdirectory("footprint-tests-");

    public void Dispose() => _dir.Delete(recursive: true);

    private string CreateFile(string name, int bytes)
    {
        var path = Path.Combine(_dir.FullName, name);
        File.WriteAllBytes(path, new byte[bytes]);
        return path;
    }

    [Fact]
    public void MissingFiles_ContributeZero()
    {
        var missing = Path.Combine(_dir.FullName, "does-not-exist.onnx");
        Assert.Equal(0, ModelFootprintEstimator.EstimateFileBytes([missing]));
    }

    [Fact]
    public void EmptyAndWhitespacePaths_AreIgnored()
    {
        Assert.Equal(0, ModelFootprintEstimator.EstimateFileBytes(["", "   "]));
    }

    [Fact]
    public void SumsAllModelFiles()
    {
        var encoder = CreateFile("encoder_model.onnx", 100);
        var decoder = CreateFile("decoder_model.onnx", 50);

        Assert.Equal(150, ModelFootprintEstimator.EstimateFileBytes([encoder, decoder]));
    }

    [Fact]
    public void IncludesExternalDataCompanions()
    {
        // optimum exports >2 GB models with weights in sibling files named after the model.
        var model = CreateFile("encoder_model.onnx", 10);
        CreateFile("encoder_model.onnx_data", 1000);
        CreateFile("encoder_model.onnx.data", 2000);
        CreateFile("unrelated.bin", 500); // must NOT be counted

        Assert.Equal(3010, ModelFootprintEstimator.EstimateFileBytes([model]));
    }

    [Fact]
    public void DoesNotDoubleCount_RepeatedOrOverlappingPaths()
    {
        var model = CreateFile("model.onnx", 10);
        var companion = CreateFile("model.onnx_data", 1000);

        // The same file twice, and a companion that is also discovered automatically.
        Assert.Equal(1010, ModelFootprintEstimator.EstimateFileBytes([model, model, companion]));
    }

    [Theory]
    [InlineData(0, 2.0, 0)]      // unknown stays unknown
    [InlineData(-5, 2.0, 0)]
    [InlineData(100, 2.0, 200)]
    [InlineData(100, 0.5, 100)]  // factor below 1.0 is clamped — a session is never smaller than its weights
    public void ApplyOverhead_ScalesAndClamps(long fileBytes, double factor, long expected)
    {
        Assert.Equal(expected, ModelFootprintEstimator.ApplyOverhead(fileBytes, factor));
    }
}
