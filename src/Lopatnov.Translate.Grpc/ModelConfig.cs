namespace Lopatnov.Translate.Grpc;

public static class ModelType
{
    public const string NLLB = "NLLB";
    public const string M2M100 = "M2M100";
    public const string FastText = "FastText";
    public const string LibreTranslate = "LibreTranslate";

    private static readonly HashSet<string> KnownTypes =
        new(StringComparer.OrdinalIgnoreCase) { NLLB, M2M100, FastText, LibreTranslate };

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
}
