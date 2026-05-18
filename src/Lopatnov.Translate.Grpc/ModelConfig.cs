namespace Lopatnov.Translate.Grpc;

public static class ModelType
{
    public const string NLLB = "NLLB";
    public const string M2M100 = "M2M100";
    public const string FastText = "FastText";
    public const string LibreTranslate = "LibreTranslate";

    public const string Whisper = "Whisper";
    public const string Piper = "Piper";
    public const string Redirect = "Redirect";

    private static readonly HashSet<string> KnownTypes =
        new(StringComparer.OrdinalIgnoreCase) { NLLB, M2M100, FastText, LibreTranslate, Whisper, Piper, Redirect };

    public static bool IsKnown(string type) => KnownTypes.Contains(type);
}

public sealed class ModelConfig
{
    public string Type { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string LabelFormat { get; set; } = string.Empty;
    public string LabelPrefix { get; set; } = string.Empty;
    public string LabelSuffix { get; set; } = string.Empty;
    public string EncoderFile { get; set; } = "encoder_model.onnx";
    public string DecoderFile { get; set; } = "decoder_model.onnx";
    public string TokenizerFile { get; set; } = "sentencepiece.bpe.model";
    public string TokenizerConfigFile { get; set; } = string.Empty;
    public int MaxTokens { get; set; } = 512;
    public int BeamSize { get; set; } = 1;
    public string VocabFile { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>gRPC endpoint of the remote service (e.g. "http://192.168.1.100:5100").
    /// Only used when Type = "Redirect".</summary>
    public string RedirectUrl { get; set; } = string.Empty;

    /// <summary>Model name to request on the remote service.
    /// Empty = use the same key as the local model entry.
    /// Only used when Type = "Redirect".</summary>
    public string RedirectName { get; set; } = string.Empty;

    /// <summary>
    /// ONNX execution provider for GPU/CPU selection.
    /// Supported values: "cpu", "directml" (Windows DX12 — Intel/AMD/NVIDIA), "cuda" (NVIDIA).
    /// Empty or absent (default) — uses the ONNX Runtime default (CPU).
    /// Throws <see cref="ArgumentException"/> for unrecognised values.
    /// Requires the matching Microsoft.ML.OnnxRuntime.* NuGet package when using GPU.
    /// Not applicable to FastText, LibreTranslate, or Whisper model types.
    /// </summary>
    public string ExecutionProvider { get; set; } = string.Empty;
}
