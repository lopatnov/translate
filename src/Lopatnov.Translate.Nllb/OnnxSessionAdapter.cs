using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Lopatnov.Translate.Nllb.Abstractions;

namespace Lopatnov.Translate.Nllb;

public sealed class OnnxSessionAdapter : IOnnxSession
{
    private readonly InferenceSession _session;

    public OnnxSessionAdapter(string modelPath)
        => _session = new InferenceSession(modelPath);

    public IReadOnlyList<NamedOnnxValue> Run(
        IReadOnlyCollection<NamedOnnxValue> inputs,
        IReadOnlyCollection<string>? outputNames = null)
    {
        // Pass outputNames to the runtime so it can skip computing unused outputs.
        // Copy only the requested tensors to avoid allocating the full output set on
        // every decoder step (the hot path needs only "logits", not all outputs).
        using var results = outputNames is null
            ? _session.Run(inputs)
            : _session.Run(inputs, outputNames);

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
