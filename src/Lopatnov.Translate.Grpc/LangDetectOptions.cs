namespace Lopatnov.Translate.Grpc;

public sealed class LangDetectOptions
{
    /// <summary>
    /// Path to lid.176.ftz. Empty = use HeuristicLanguageDetector as fallback.
    /// </summary>
    public string Path { get; set; } = "./models/langdetect/lid.176.ftz";
}
