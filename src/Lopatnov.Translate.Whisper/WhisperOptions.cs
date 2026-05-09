namespace Lopatnov.Translate.Whisper;

/// <summary>
/// Configuration for the WhisperRecognizer.
/// Binds from the Models section in appsettings.json via the entry selected by Translation:AudioToText.
/// </summary>
public sealed class WhisperOptions
{
    /// <summary>
    /// Absolute or content-root-relative path to the ggml model file
    /// (e.g. "../../models/audio-to-text/whisper.cpp/ggml-small.bin").
    /// </summary>
    public string ModelPath { get; set; } = string.Empty;

    /// <summary>
    /// Minutes of inactivity after which the loaded WhisperFactory is evicted from memory.
    /// Mirrors Translation:ModelTtlMinutes so all models share one TTL setting.
    /// </summary>
    public int TtlMinutes { get; set; } = 30;
}
