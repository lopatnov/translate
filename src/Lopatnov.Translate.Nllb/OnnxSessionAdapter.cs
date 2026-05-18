using System.Diagnostics.CodeAnalysis;
using Microsoft.ML.OnnxRuntime;
using Lopatnov.Translate.Nllb.Abstractions;

namespace Lopatnov.Translate.Nllb;

[ExcludeFromCodeCoverage]
public sealed class OnnxSessionAdapter : IOnnxSession
{
    private readonly InferenceSession _session;

    public OnnxSessionAdapter(string modelPath, SessionOptions? sessionOptions = null)
        => _session = sessionOptions is not null
            ? new InferenceSession(modelPath, sessionOptions)
            : new InferenceSession(modelPath);

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
