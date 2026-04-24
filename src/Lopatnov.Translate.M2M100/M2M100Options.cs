namespace Lopatnov.Translate.M2M100;

public sealed class M2M100Options
{
    public string Path { get; set; } = "./models/m2m100";
    public string EncoderFile { get; set; } = "encoder_model.onnx";
    public string DecoderFile { get; set; } = "decoder_model.onnx";
    public string TokenizerFile { get; set; } = "sentencepiece.bpe.model";
    public string TokenizerConfigFile { get; set; } = "tokenizer.json";
    public int MaxTokens { get; set; } = 512;
    // M2M-100 HuggingFace vocab has 4 special tokens (BOS/PAD/EOS/UNK) before SP vocab.
    // Verify against your model's tokenizer.json if translation output looks garbled.
    public int SentencePieceOffset { get; set; } = 4;
}
