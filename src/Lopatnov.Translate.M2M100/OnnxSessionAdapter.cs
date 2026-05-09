using System.Diagnostics.CodeAnalysis;
using Microsoft.ML.OnnxRuntime;
using Lopatnov.Translate.M2M100.Abstractions;

namespace Lopatnov.Translate.M2M100;

[ExcludeFromCodeCoverage]
public sealed class OnnxSessionAdapter : IOnnxSession
{
    private readonly InferenceSession _session;

    public OnnxSessionAdapter(string modelPath)
        => _session = new InferenceSession(modelPath);

    public void Run(
        IReadOnlyCollection<NamedOnnxValue> inputs,
        IReadOnlyCollection<string>? outputNames,
        Action<IEnumerable<NamedOnnxValue>> process)
    {
        using var results = outputNames is null
            ? _session.Run(inputs)
            : _session.Run(inputs, outputNames);
        process(results);
    }

    public void Dispose() => _session.Dispose();
}
