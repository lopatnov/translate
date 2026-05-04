using FastText.NetWrapper;
using Lopatnov.Translate.Core.Abstractions;

namespace Lopatnov.Translate.Core.LanguageDetectors;

public sealed class NativeFastTextLanguageDetectorSettings
{
    public string ModelPath { get; set; } = string.Empty;
    public LanguageCodeFormat LabelFormat { get; set; } = LanguageCodeFormat.None;
    public string LabelPrefix { get; set; } = string.Empty;
    public string LabelSuffix { get; set; } = string.Empty;
}

/// <summary>
/// Language detector backed by the native fastText C++ library via FastText.NetWrapper.
/// Requires the native fasttext binary to be present (included in the NuGet package for
/// win-x64 and linux-x64). Designed to run as a singleton.
/// </summary>
public sealed class NativeFastTextLanguageDetector : ILanguageDetector, IDisposable
{
    private readonly FastTextWrapper _fastText;

    private readonly LanguageCodeFormat _nativeFormat;
    private readonly string _labelPrefix;
    private readonly string _labelSuffix;

    public NativeFastTextLanguageDetector(NativeFastTextLanguageDetectorSettings settings)
    {
        _fastText = new FastTextWrapper();
        _nativeFormat = settings.LabelFormat;
        _labelPrefix = settings.LabelPrefix;
        _labelSuffix = settings.LabelSuffix;
        _fastText.LoadModel(settings.ModelPath);
    }

    public LanguageDetectionResult Detect(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(text.Trim()))
            return new LanguageDetectionResult("N/A", LanguageCodeFormat.None, 1f);

        var prediction = _fastText.PredictSingle(text);
        if (string.IsNullOrEmpty(prediction.Label))
            return new LanguageDetectionResult("N/A", LanguageCodeFormat.None, 1f);
        var label = GetLabelWithoutPrefixSuffix(prediction.Label);
        return new LanguageDetectionResult(label, _nativeFormat, prediction.Probability);
    }

    private string GetLabelWithoutPrefixSuffix(string label)
    {
        if (label.StartsWith(_labelPrefix, StringComparison.Ordinal))
            label = label[_labelPrefix.Length..];
        if (!string.IsNullOrEmpty(_labelSuffix) && label.EndsWith(_labelSuffix, StringComparison.Ordinal))
            label = label[..^_labelSuffix.Length];
        return label;
    }

    public void Dispose() => _fastText.Dispose();
}
