using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Lopatnov.Translate.Nllb.Abstractions;

namespace Lopatnov.Translate.Nllb;

public sealed class OnnxSessionAdapter : IOnnxSession
{
    private readonly InferenceSession _session;

    public OnnxSessionAdapter(string modelPath)
        => _session = new InferenceSession(modelPath);

    public IReadOnlyList<NamedOnnxValue> Run(IReadOnlyCollection<NamedOnnxValue> inputs)
    {
        using var results = _session.Run(inputs);
        return results.Select(CopyResult).ToList();
    }

    public void Dispose() => _session.Dispose();

    private static NamedOnnxValue CopyResult(DisposableNamedOnnxValue source)
    {
        var tensor = source.AsTensor<float>();
        var copy = new DenseTensor<float>(tensor.ToArray(), tensor.Dimensions.ToArray());
        return NamedOnnxValue.CreateFromTensor(source.Name, copy);
    }
}
