namespace Lopatnov.Translate.Grpc;

public sealed class TranslationOptions
{
    /// <summary>Restricts which models can be used by name. Empty = all configured translators allowed.</summary>
    public string[] AllowedModels { get; set; } = [];

    /// <summary>Minutes a loaded model stays in memory after its last use before being evicted.</summary>
    public int ModelTtlMinutes { get; set; } = 30;

    /// <summary>Name of the language-detection model entry in Models. Empty = heuristic fallback.</summary>
    public string AutoDetect { get; set; } = string.Empty;

    /// <summary>Default model name when request.model is empty.</summary>
    public string DefaultModel { get; set; } = string.Empty;

    /// <summary>
    /// Name of the Whisper model entry in Models (Type=Whisper) used for speech-to-text.
    /// Empty = STT disabled (TranscribeAudio returns FailedPrecondition).
    /// </summary>
    public string AudioToText { get; set; } = string.Empty;
}
