using Lopatnov.Translate.Piper;

namespace Lopatnov.Translate.Piper.Tests;

/// <summary>
/// Unit tests for <see cref="PiperVoiceConfig.LoadFrom"/> — pure JSON deserialization,
/// no ONNX model or espeak-ng required.
/// </summary>
public sealed class PiperVoiceConfigTests
{
    // ── Full config JSON ──────────────────────────────────────────────────────

    private const string FullJson = """
        {
            "phoneme_id_map": {
                "a": [1, 2],
                "b": [3]
            },
            "espeak": { "voice": "en-us" },
            "audio": { "sample_rate": 22050 },
            "inference": {
                "noise_scale": 0.667,
                "length_scale": 1.0,
                "noise_w": 0.8
            },
            "phoneme_type": "espeak",
            "num_speakers": 1
        }
        """;

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Writes JSON to a temp file and returns its path.</summary>
    private static string WriteTemp(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".json");
        File.WriteAllText(path, json);
        return path;
    }

    // ── LoadFrom: valid JSON ───────────────────────────────────────────────────

    [Fact]
    public void LoadFrom_ParsesAllFields_FromFullJson()
    {
        var path = WriteTemp(FullJson);
        try
        {
            var config = PiperVoiceConfig.LoadFrom(path);

            Assert.Equal(22050,    config.Audio.SampleRate);
            Assert.Equal("en-us",  config.Espeak.Voice);
            Assert.Equal(0.667f,   config.Inference.NoiseScale, precision: 3);
            Assert.Equal(1.0f,     config.Inference.LengthScale, precision: 3);
            Assert.Equal(0.8f,     config.Inference.NoiseW,      precision: 3);
            Assert.Equal("espeak", config.PhonemeType);
            Assert.Equal(1,        config.NumSpeakers);
            Assert.Equal(2,        config.PhonemeIdMap.Count);
            Assert.Equal([1L, 2L], config.PhonemeIdMap["a"]);
            Assert.Equal([3L],     config.PhonemeIdMap["b"]);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void LoadFrom_UsesDefaults_WhenOptionalSectionsAbsent()
    {
        // Minimal JSON — only the required phoneme_id_map key.
        var path = WriteTemp("""{"phoneme_id_map": {}}""");
        try
        {
            var config = PiperVoiceConfig.LoadFrom(path);

            Assert.Equal(22050,        config.Audio.SampleRate);    // AudioSection default
            Assert.Equal(string.Empty, config.Espeak.Voice);        // EspeakSection default
            Assert.Equal(0.667f,       config.Inference.NoiseScale, precision: 3); // InferenceSection default
            Assert.Equal(1.0f,         config.Inference.LengthScale, precision: 3);
            Assert.Equal(0.8f,         config.Inference.NoiseW,      precision: 3);
            Assert.Equal("espeak",     config.PhonemeType);          // PhonemeType default
            Assert.Equal(1,            config.NumSpeakers);          // NumSpeakers default
            Assert.Empty(config.SpeakerIdMap);
            Assert.Empty(config.PhonemeIdMap);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void LoadFrom_ParsesTextPhonemeType()
    {
        var path = WriteTemp("""{"phoneme_id_map": {"а": [10]}, "phoneme_type": "text"}""");
        try
        {
            var config = PiperVoiceConfig.LoadFrom(path);
            Assert.Equal("text", config.PhonemeType);
            Assert.Equal([10L], config.PhonemeIdMap["а"]);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void LoadFrom_ParsesMultiSpeakerMap()
    {
        const string json = """
            {
                "phoneme_id_map": {},
                "num_speakers": 3,
                "speaker_id_map": {
                    "speaker0": 0,
                    "speaker1": 1,
                    "speaker2": 2
                }
            }
            """;
        var path = WriteTemp(json);
        try
        {
            var config = PiperVoiceConfig.LoadFrom(path);

            Assert.Equal(3, config.NumSpeakers);
            Assert.Equal(3, config.SpeakerIdMap.Count);
            Assert.Equal(0L, config.SpeakerIdMap["speaker0"]);
            Assert.Equal(1L, config.SpeakerIdMap["speaker1"]);
            Assert.Equal(2L, config.SpeakerIdMap["speaker2"]);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void LoadFrom_AllowsTrailingCommas()
    {
        // Piper JSON files often have trailing commas.
        const string json = """
            {
                "phoneme_id_map": {"a": [1,],},
                "phoneme_type": "espeak",
            }
            """;
        var path = WriteTemp(json);
        try
        {
            var config = PiperVoiceConfig.LoadFrom(path);
            Assert.Single(config.PhonemeIdMap);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void LoadFrom_AllowsCommentsInJson()
    {
        const string json = """
            {
                // This is a comment
                "phoneme_id_map": {},
                "phoneme_type": "espeak" /* block comment */
            }
            """;
        var path = WriteTemp(json);
        try
        {
            var config = PiperVoiceConfig.LoadFrom(path);
            Assert.Equal("espeak", config.PhonemeType);
        }
        finally { File.Delete(path); }
    }

    // ── LoadFrom: error paths ─────────────────────────────────────────────────

    [Fact]
    public void LoadFrom_Throws_WhenFileDoesNotExist()
    {
        var nonExistent = Path.Combine(Path.GetTempPath(), "nonexistent-" + Guid.NewGuid() + ".json");
        Assert.Throws<FileNotFoundException>(() => PiperVoiceConfig.LoadFrom(nonExistent));
    }

    [Fact]
    public void LoadFrom_Throws_WhenJsonIsNullLiteral()
    {
        // Deserializing "null" returns null; our code throws InvalidDataException for that.
        var path = WriteTemp("null");
        try
        {
            Assert.Throws<InvalidDataException>(() => PiperVoiceConfig.LoadFrom(path));
        }
        finally { File.Delete(path); }
    }
}
