namespace Lopatnov.Translate.Nllb;

public sealed class NllbOptions
{
    public string Path { get; set; } = "./models/nllb";
    public string EncoderFile { get; set; } = "encoder_model_quantized.onnx";
    public string DecoderFile { get; set; } = "decoder_model_quantized.onnx";
    public string TokenizerFile { get; set; } = "sentencepiece.bpe.model";
    public string TokenizerConfigFile { get; set; } = "tokenizer.json";
    public int MaxTokens { get; set; } = 512;
    public int BeamSize { get; set; } = 1;
}
