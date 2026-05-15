namespace Lopatnov.Translate.Piper;

/// <summary>
/// Configuration for a single <see cref="PiperSynthesizer"/> instance.
/// Binds from the Models section in appsettings.json via the entry selected
/// by the caller (looked up through <c>Translation:TextToAudio</c>).
/// </summary>
public sealed class PiperOptions
{
    /// <summary>
    /// Absolute or content-root-relative path to the Piper ONNX voice file
    /// (e.g. "../../models/text-to-audio/piper-voices/en_US/en_US-joe-medium.onnx").
    /// The companion <c>.onnx.json</c> sidecar must exist at <c>{ModelPath}.json</c>.
    /// </summary>
    public string ModelPath { get; set; } = string.Empty;

    /// <summary>
    /// Minutes of inactivity after which the loaded ONNX session is evicted from memory.
    /// Mirrors <c>Translation:ModelTtlMinutes</c> so all models share one TTL setting.
    /// </summary>
    public int TtlMinutes { get; set; } = 30;
}
