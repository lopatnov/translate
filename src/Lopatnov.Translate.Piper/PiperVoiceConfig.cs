using System.Text.Json;
using System.Text.Json.Serialization;

namespace Lopatnov.Translate.Piper;

/// <summary>
/// Deserialised form of the <c>.onnx.json</c> sidecar that Piper ships alongside every voice model.
/// Only the fields consumed by <see cref="PiperSynthesizer"/> are mapped; the rest are silently ignored.
/// </summary>
internal sealed class PiperVoiceConfig
{
    [JsonPropertyName("audio")]
    public AudioSection Audio { get; set; } = new();

    [JsonPropertyName("espeak")]
    public EspeakSection Espeak { get; set; } = new();

    [JsonPropertyName("inference")]
    public InferenceSection Inference { get; set; } = new();

    /// <summary>
    /// Phonemisation strategy: <c>"espeak"</c> (default) converts text to IPA via espeak-ng;
    /// <c>"text"</c> maps raw text characters directly using <see cref="PhonemeIdMap"/>.
    /// Ukrainian multi-speaker models typically use <c>"text"</c>.
    /// </summary>
    [JsonPropertyName("phoneme_type")]
    public string PhonemeType { get; set; } = "espeak";

    [JsonPropertyName("num_speakers")]
    public int NumSpeakers { get; set; } = 1;

    [JsonPropertyName("speaker_id_map")]
    public Dictionary<string, long> SpeakerIdMap { get; set; } = [];

    /// <summary>
    /// Maps IPA phoneme strings (typically single Unicode code points) to sequences of
    /// integer phoneme IDs as expected by the ONNX model's <c>input</c> tensor.
    /// </summary>
    [JsonPropertyName("phoneme_id_map")]
    public Dictionary<string, long[]> PhonemeIdMap { get; set; } = [];

    // -------------------------------------------------------------------------

    internal sealed class AudioSection
    {
        [JsonPropertyName("sample_rate")]
        public int SampleRate { get; set; } = 22050;
    }

    internal sealed class EspeakSection
    {
        /// <summary>espeak-ng voice name (e.g. "en-us", "ru", "uk").</summary>
        [JsonPropertyName("voice")]
        public string Voice { get; set; } = string.Empty;
    }

    internal sealed class InferenceSection
    {
        [JsonPropertyName("noise_scale")]
        public float NoiseScale { get; set; } = 0.667f;

        [JsonPropertyName("length_scale")]
        public float LengthScale { get; set; } = 1.0f;

        [JsonPropertyName("noise_w")]
        public float NoiseW { get; set; } = 0.8f;
    }

    // -------------------------------------------------------------------------

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        // phoneme_id_map contains duplicate keys (same Unicode code point in different
        // normalization forms). Allow duplicates; the last value wins.
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    internal static PiperVoiceConfig LoadFrom(string jsonPath)
    {
        using var stream = File.OpenRead(jsonPath);
        // Use AllowDuplicateProperties in .NET 10 if available; otherwise rely on last-wins semantics.
        return JsonSerializer.Deserialize<PiperVoiceConfig>(stream, _jsonOptions)
            ?? throw new InvalidDataException($"Failed to parse Piper voice config: {jsonPath}");
    }
}
