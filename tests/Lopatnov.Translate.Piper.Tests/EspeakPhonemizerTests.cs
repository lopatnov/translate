using Lopatnov.Translate.Piper;

namespace Lopatnov.Translate.Piper.Tests;

/// <summary>
/// Integration tests that diagnose the full phonemisation pipeline:
///   text → espeak-ng IPA → phoneme_id_map lookup → int64 phoneme IDs.
///
/// All tests in this class require:
///   - espeak-ng installed on PATH
///   - Piper voice model .onnx.json sidecar files on disk
/// Tests are skipped automatically when either dependency is absent.
/// </summary>
public sealed class EspeakPhonemizerTests
{
    // -------------------------------------------------------------------------
    // Model sidecar paths (relative to test output directory)
    // -------------------------------------------------------------------------

    private const string RuslanJsonPath =
        @"..\..\..\..\..\models\text-to-audio\piper-voices\ru_RU\ru_RU-ruslan-medium.onnx.json";

    private const string IrinaJsonPath =
        @"..\..\..\..\..\models\text-to-audio\piper-voices\ru_Irina\ru_RU-irina-medium.onnx.json";

    private const string OleksaJsonPath =
        @"..\..\..\..\..\models\text-to-audio\piper-voices\uk_Oleksa\uk_UA-oleksa-high.onnx.json";

    private const string UkMediumJsonPath =
        @"..\..\..\..\..\models\text-to-audio\piper-voices\uk_UA\uk_UA-ukrainian_tts-medium.onnx.json";

    // -------------------------------------------------------------------------
    // Test sentences
    // -------------------------------------------------------------------------

    private const string RussianText   = "Хорошо что ты пришёл";
    private const string UkrainianText = "Сьогодні чудова погода і хочеться гуляти";

    // =========================================================================
    // Stage 1 — EspeakPhonemizer: does espeak-ng produce valid IPA at all?
    // =========================================================================

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Stage1_Espeak_Russian_ProducesNonEmptyIPA()
    {
        var ipa = await EspeakPhonemizer.PhonemizeAsync(RussianText, "ru");

        Assert.False(string.IsNullOrWhiteSpace(ipa),
            "espeak-ng returned empty IPA for Russian text — is espeak-ng installed?");

        // "(en)" prefix means espeak fell back to English — symptom of encoding bug
        Assert.False(ipa.Contains("(en)"),
            $"espeak-ng produced English fallback '(en)' — stdin encoding bug still active? IPA: {ipa}");

        // ʃ (U+0283) is always present in "хорошо/что/пришёл"
        Assert.True(ipa.Contains("ʃ"),
            $"Expected Russian IPA 'ʃ' (ш/щ) but got: {ipa}");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Stage1_Espeak_Ukrainian_ProducesNonEmptyIPA()
    {
        var ipa = await EspeakPhonemizer.PhonemizeAsync(UkrainianText, "uk");

        Assert.False(string.IsNullOrWhiteSpace(ipa),
            "espeak-ng returned empty IPA for Ukrainian text.");

        Assert.False(ipa.Contains("(en)"),
            $"espeak-ng produced English fallback '(en)' — encoding bug. IPA: {ipa}");

        Assert.True(ipa.Any(c => c > 127),
            $"Expected non-ASCII IPA characters in Ukrainian output. Got: {ipa}");
    }

    // =========================================================================
    // Stage 2 — Phoneme map coverage: are all IPA chars in the model's map?
    // =========================================================================

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Stage2_Coverage_Russian_RuslanMap_AllIPACharsFound()
    {
        if (!File.Exists(RuslanJsonPath)) Assert.Skip(            $"Ruslan sidecar not found at: {Path.GetFullPath(RuslanJsonPath)}");

        var config = PiperVoiceConfig.LoadFrom(Path.GetFullPath(RuslanJsonPath));
        var ipa    = await EspeakPhonemizer.PhonemizeAsync(RussianText, config.Espeak.Voice);

        var missing = ipa
            .Where(c => c is not ('\r' or '\n') && !config.PhonemeIdMap.ContainsKey(c.ToString()))
            .Distinct().OrderBy(c => c).ToList();

        Assert.True(missing.Count == 0,
            $"IPA chars NOT in Ruslan map: [{string.Join(", ", missing.Select(c => $"'{c}' U+{(int)c:X4}"))}]\n" +
            $"Full IPA: {ipa}  |  Map size: {config.PhonemeIdMap.Count}");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Stage2_Coverage_Russian_IrinaMap_AllIPACharsFound()
    {
        if (!File.Exists(IrinaJsonPath)) Assert.Skip(            $"Irina sidecar not found at: {Path.GetFullPath(IrinaJsonPath)}");

        var config = PiperVoiceConfig.LoadFrom(Path.GetFullPath(IrinaJsonPath));
        var ipa    = await EspeakPhonemizer.PhonemizeAsync(RussianText, config.Espeak.Voice);

        var missing = ipa
            .Where(c => c is not ('\r' or '\n') && !config.PhonemeIdMap.ContainsKey(c.ToString()))
            .Distinct().OrderBy(c => c).ToList();

        Assert.True(missing.Count == 0,
            $"IPA chars NOT in Irina map: [{string.Join(", ", missing.Select(c => $"'{c}' U+{(int)c:X4}"))}]\n" +
            $"Full IPA: {ipa}");
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Stage2_Coverage_Ukrainian_OleksaMap_AllIPACharsFound()
    {
        if (!File.Exists(OleksaJsonPath)) Assert.Skip(            $"Oleksa sidecar not found at: {Path.GetFullPath(OleksaJsonPath)}");

        var config = PiperVoiceConfig.LoadFrom(Path.GetFullPath(OleksaJsonPath));
        var ipa    = await EspeakPhonemizer.PhonemizeAsync(UkrainianText, config.Espeak.Voice);

        var missing = ipa
            .Where(c => c is not ('\r' or '\n') && !config.PhonemeIdMap.ContainsKey(c.ToString()))
            .Distinct().OrderBy(c => c).ToList();

        Assert.True(missing.Count == 0,
            $"IPA chars NOT in Oleksa map: [{string.Join(", ", missing.Select(c => $"'{c}' U+{(int)c:X4}"))}]\n" +
            $"Full IPA: {ipa}  |  Map size: {config.PhonemeIdMap.Count}");
    }

    // =========================================================================
    // Stage 3 — BuildPhonemeIds: do we get a non-trivial ID sequence?
    // =========================================================================

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Stage3_PhonemeIds_Russian_Ruslan_ProducesReasonableCount()
    {
        if (!File.Exists(RuslanJsonPath)) Assert.Skip(            $"Ruslan sidecar not found at: {Path.GetFullPath(RuslanJsonPath)}");

        var config = PiperVoiceConfig.LoadFrom(Path.GetFullPath(RuslanJsonPath));
        var ipa    = await EspeakPhonemizer.PhonemizeAsync(RussianText, config.Espeak.Voice);
        var ids    = PiperSynthesizer.BuildPhonemeIds(ipa, config.PhonemeIdMap);

        // "Хорошо что ты пришёл" → ~20 phonemes × 2 (phoneme + PAD) + BOS + EOS ≥ 42
        Assert.True(ids.Length > 20,
            $"Expected >20 IDs for '{RussianText}', got {ids.Length}. IPA: '{ipa}'");

        Assert.Equal(1L, ids[0]);    // BOS ("^" → 1 per piper1-gpl reference)
        Assert.Equal(2L, ids[^1]);   // EOS
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task Stage3_PhonemeIds_Ukrainian_Oleksa_ProducesReasonableCount()
    {
        if (!File.Exists(OleksaJsonPath)) Assert.Skip(            $"Oleksa sidecar not found at: {Path.GetFullPath(OleksaJsonPath)}");

        var config = PiperVoiceConfig.LoadFrom(Path.GetFullPath(OleksaJsonPath));
        var ipa    = await EspeakPhonemizer.PhonemizeAsync(UkrainianText, config.Espeak.Voice);
        var ids    = PiperSynthesizer.BuildPhonemeIds(ipa, config.PhonemeIdMap);

        Assert.True(ids.Length > 20,
            $"Expected >20 IDs for '{UkrainianText}', got {ids.Length}. IPA: '{ipa}'");

        Assert.Equal(1L, ids[0]);    // BOS ("^" → 1 per piper1-gpl reference)
        Assert.Equal(2L, ids[^1]);   // EOS
    }

    // =========================================================================
    // Stage 4 — phoneme_type=text path (uk_UA-ukrainian_tts-medium)
    // =========================================================================

    [Fact]
    [Trait("Category", "Integration")]
    public void Stage4_PhonemeIdsFromText_Ukrainian_MapsKnownChars()
    {
        if (!File.Exists(UkMediumJsonPath)) Assert.Skip(            $"UK-medium sidecar not found at: {Path.GetFullPath(UkMediumJsonPath)}");

        var config = PiperVoiceConfig.LoadFrom(Path.GetFullPath(UkMediumJsonPath));

        // Tricky Ukrainian chars: ї (U+0457), є (U+0454), і (U+0456)
        const string text = "їжак і єнот";
        var ids = PiperSynthesizer.BuildPhonemeIdsFromText(text, config.PhonemeIdMap);

        // BOS + at least 5 chars×2 + EOS
        Assert.True(ids.Length > 5,
            $"Expected >5 IDs for '{text}', got {ids.Length} " +
            "(BOS+EOS only = 2 → Unicode NFC/NFD mismatch or chars missing from map).");

        Assert.Equal(1L, ids[0]);    // BOS ("^" → 1 per piper1-gpl reference)
        Assert.Equal(2L, ids[^1]);   // EOS
    }
}
