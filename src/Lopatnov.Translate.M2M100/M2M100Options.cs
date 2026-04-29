namespace Lopatnov.Translate.M2M100;

public sealed class M2M100Options
{
    public string Path { get; set; } = "./models/m2m100";
    public string EncoderFile { get; set; } = "encoder_model.onnx";
    public string DecoderFile { get; set; } = "decoder_model.onnx";
    public string TokenizerFile { get; set; } = "sentencepiece.bpe.model";
    // facebook/m2m100_1.2B repo has added_tokens.json (not tokenizer.json).
    // optimum-cli may generate tokenizer.json on export — both formats are supported.
    public string TokenizerConfigFile { get; set; } = "added_tokens.json";
    // vocab.json maps BPE piece strings to HuggingFace token IDs (downloaded alongside the model).
    public string VocabFile { get; set; } = "vocab.json";
    public int MaxTokens { get; set; } = 512;
}
