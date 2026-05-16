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

    /// <summary>
    /// Whisper.net inference backend. One of: "cpu" (default), "cuda", "coreml", "openvino".
    /// The active backend is determined entirely by the installed Whisper.net.Runtime.* NuGet package —
    /// no code change is needed. This field is used only for logging at startup.
    ///   cpu      → Whisper.net.Runtime (default, all platforms)
    ///   cuda     → Whisper.net.Runtime.Cuda (NVIDIA GPU)
    ///   coreml   → Whisper.net.Runtime.CoreML (Apple Silicon)
    ///   openvino → Whisper.net.Runtime.OpenVino (Intel NPU/GPU)
    /// </summary>
    public string Backend { get; set; } = "cpu";
}
