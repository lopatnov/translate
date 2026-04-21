using Microsoft.ML.OnnxRuntime;

namespace Lopatnov.Translate.Nllb.Abstractions;

public interface IOnnxSession : IDisposable
{
    IReadOnlyList<NamedOnnxValue> Run(IReadOnlyCollection<NamedOnnxValue> inputs);
}
