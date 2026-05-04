using FastText.NetWrapper;
using Lopatnov.Translate.Core.Abstractions;

namespace Lopatnov.Translate.Core.LanguageDetectors;

/// <summary>
/// Language detector backed by the native fastText C++ library via FastText.NetWrapper.
/// Requires the native fasttext binary to be present (included in the NuGet package for
/// win-x64 and linux-x64). Designed to run as a singleton.
/// </summary>
public sealed class NativeFastTextLanguageDetector : ILanguageDetector, IDisposable
{
    private const string LabelPrefix = "__label__";

    private readonly FastTextWrapper _fastText;

    public NativeFastTextLanguageDetector(string modelPath)
    {
        _fastText = new FastTextWrapper();
        _fastText.LoadModel(modelPath);
    }

    public LanguageDetectionResult Detect(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new LanguageDetectionResult(Language.EnglishLatin, "flores200");

        var prediction = _fastText.PredictSingle(text);
        if (string.IsNullOrEmpty(prediction.Label))
            return new LanguageDetectionResult(Language.EnglishLatin, "flores200");

        var label = prediction.Label.StartsWith(LabelPrefix, StringComparison.Ordinal)
            ? prediction.Label[LabelPrefix.Length..]
            : prediction.Label;

        var flores = LanguageCodeConverter.IsoLabelToFlores200(label);
        return new LanguageDetectionResult(flores, "flores200");
    }

    public void Dispose() => _fastText.Dispose();
}
