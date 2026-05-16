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
    /// Preferred Whisper.net runtime backend. Controls <c>RuntimeOptions.RuntimeLibraryOrder</c>.
    /// Leave empty or set to "auto" (default) to let Whisper.net probe in order:
    ///   Cuda → Cuda12 → Vulkan → CoreML → OpenVino → Cpu
    /// Valid explicit values:
    ///   auto    — probe all installed runtimes, pick best (recommended)
    ///   cpu     — CPU only (Whisper.net.Runtime)
    ///   cuda    — NVIDIA GPU (Whisper.net.Runtime.Cuda), falls back to CPU
    ///   vulkan  — Vulkan GPU, incl. Intel Arc (Whisper.net.Runtime.Vulkan), falls back to CPU
    ///   coreml  — Apple Silicon (Whisper.net.Runtime.CoreML), falls back to CPU
    /// Unknown values throw <see cref="ArgumentException"/> at model load time.
    /// </summary>
    public string Backend { get; set; } = string.Empty;
}
