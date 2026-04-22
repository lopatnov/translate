using Microsoft.ML.OnnxRuntime;

namespace Lopatnov.Translate.Nllb.Abstractions;

public interface IOnnxSession : IDisposable
{
    // process() is invoked while native tensors are still alive; adapter disposes after it returns.
    // Callers must not store references to the tensors beyond the callback scope.
    void Run(
        IReadOnlyCollection<NamedOnnxValue> inputs,
        IReadOnlyCollection<string>? outputNames,
        Action<IEnumerable<NamedOnnxValue>> process);
}
