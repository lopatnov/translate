using System.Collections.Frozen;
using Lopatnov.Translate.Core.Abstractions;

namespace Lopatnov.Translate.Core.LanguageDetectors;

/// <summary>
/// Lightweight language detector based on Unicode block analysis and Latin diacritic scoring.
/// Detects 22 languages reliably without requiring an ML model.
/// Script-based detection (Cyrillic, CJK, Hangul, Arabic, Greek, Hebrew, Devanagari, Thai, Kana)
/// is near-perfect. Latin disambiguation uses language-exclusive diacritics.
/// Falls back to English for ambiguous Latin-script text.
/// </summary>
public sealed class HeuristicLanguageDetector : ILanguageDetector
{
    private const int SampleLength = 300;

    private struct LatinScores
    {
        public int De, Fr, Es, Pl, Pt, It, Ro, Sv, Cs, Tr, Hu;

        public readonly int Max()
        {
            int a = Math.Max(Math.Max(De, Fr), Math.Max(Es, Pl));
            int b = Math.Max(Math.Max(Pt, It), Math.Max(Ro, Sv));
            return Math.Max(Math.Max(a, b), Math.Max(Cs, Math.Max(Tr, Hu)));
        }
    }

    // Per-character Latin score increments stored in a frozen lookup table.
    private readonly record struct ScoreDelta(
        sbyte De = 0, sbyte Fr = 0, sbyte Es = 0, sbyte Pl = 0, sbyte Pt = 0,
        sbyte It = 0, sbyte Ro = 0, sbyte Sv = 0, sbyte Cs = 0, sbyte Tr = 0, sbyte Hu = 0)
    {
        public readonly void Apply(ref LatinScores s)
        {
            s.De += De; s.Fr += Fr; s.Es += Es; s.Pl += Pl; s.Pt += Pt;
            s.It += It; s.Ro += Ro; s.Sv += Sv; s.Cs += Cs; s.Tr += Tr; s.Hu += Hu;
        }
    }

    private static readonly FrozenDictionary<char, ScoreDelta> _latinScoreTable =
        BuildLatinScoreTable();

    // Script counts from a single pass — bundles all counters to keep DetectCode simple.
    private readonly struct ScriptCounts(
        int cyrillic, int latin, int cjk, int arabic,
        int hiragana, int katakana, int hangul,
        int devanagari, int thai, int greek, int hebrew,
        int ukSpec, LatinScores ls)
    {
        public readonly int Cyrillic = cyrillic, Latin = latin, Cjk = cjk, Arabic = arabic;
        public readonly int Hiragana = hiragana, Katakana = katakana, Hangul = hangul;
        public readonly int Devanagari = devanagari, Thai = thai, Greek = greek, Hebrew = hebrew;
        public readonly int UkSpec = ukSpec;
        public readonly LatinScores Ls = ls;
    }

    public LanguageDetectionResult Detect(string text)
    {
        if (string.IsNullOrWhiteSpace(text?.Trim()))
            return new LanguageDetectionResult(Language.EnglishLatin, LanguageCodeFormat.Flores200, 1f);

        var sample = text.Length > SampleLength ? text.AsSpan(0, SampleLength) : text.AsSpan();
        return new LanguageDetectionResult(DetectCode(sample), LanguageCodeFormat.Flores200);
    }

    private static string DetectCode(ReadOnlySpan<char> sample)
    {
        var sc = CountScripts(sample);
        int kana = sc.Hiragana + sc.Katakana;

        if (sc.Cyrillic > 0 && sc.Cyrillic >= sc.Latin && sc.Cyrillic >= sc.Cjk && sc.Cyrillic >= sc.Arabic)
            return sc.UkSpec > 0 ? Language.UkrainianCyrillic : Language.RussianCyrillic;

        if (sc.Hangul > 0) return Language.KoreanHangul;
        if (sc.Greek > 0 && sc.Greek > sc.Latin / 3) return Language.GreekGrek;
        if (sc.Hebrew > 0) return Language.HebrewHebr;

        if (sc.Cjk > sc.Latin) return kana > 0 ? Language.JapaneseJpan : Language.ChineseSimplified;
        if (kana > sc.Latin && kana > sc.Cjk) return Language.JapaneseJpan;
        if (sc.Arabic > sc.Latin) return Language.ArabicArab;
        if (sc.Devanagari > sc.Latin) return Language.HindiDevanagari;
        if (sc.Thai > sc.Latin) return Language.ThaiThai;

        return SelectLatinLanguage(in sc.Ls);
    }

    private static ScriptCounts CountScripts(ReadOnlySpan<char> sample)
    {
        int cyrillic = 0, latin = 0, cjk = 0, arabic = 0;
        int hiragana = 0, katakana = 0, hangul = 0;
        int devanagari = 0, thai = 0, greek = 0, hebrew = 0;
        int ukSpec = 0;
        LatinScores ls = default;

        foreach (var c in sample)
        {
            if (c is >= 'Ѐ' and <= 'ӿ')
            {
                cyrillic++;
                if (IsUkrainianSpecific(c)) ukSpec++;
            }
            else if (c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z'))
                latin++;
            else if (c is (>= 'À' and <= 'ɏ') or 'ı')
            {
                latin++;
                if (_latinScoreTable.TryGetValue(c, out var delta))
                    delta.Apply(ref ls);
            }
            else if (c is >= '一' and <= '鿿') cjk++;
            else if (c is >= '぀' and <= 'ゟ') hiragana++;
            else if (c is >= '゠' and <= 'ヿ') katakana++;
            else if (c is >= '؀' and <= 'ۿ') arabic++;
            else if (c is >= 'ऀ' and <= 'ॿ') devanagari++;
            else if (c is >= '฀' and <= '๿') thai++;
            else if (c is (>= '가' and <= '힯') or (>= 'ᄀ' and <= 'ᇿ')) hangul++;
            else if (c is >= 'Ͱ' and <= 'Ͽ') greek++;
            else if (c is >= 'א' and <= 'ת') hebrew++;
        }

        return new ScriptCounts(cyrillic, latin, cjk, arabic, hiragana, katakana, hangul,
            devanagari, thai, greek, hebrew, ukSpec, ls);
    }

    private static string SelectLatinLanguage(in LatinScores s)
    {
        int max = s.Max();
        if (max == 0) return Language.EnglishLatin;

        // Ordered by exclusivity of their decisive markers.
        if (s.Hu == max) return Language.HungarianLatin;  // ő ű — double acute, unique
        if (s.Ro == max) return Language.RomanianLatin;   // ă ș ț — unique
        if (s.Cs == max) return Language.CzechLatin;      // ě ř — unique
        if (s.Pt == max) return Language.PortugueseLatin; // ã õ — unique
        if (s.Tr == max) return Language.TurkishLatin;    // ş ğ — unique
        if (s.Sv == max) return Language.SwedishLatin;    // å — unique
        if (s.Pl == max) return Language.PolishLatin;     // ą ę — unique
        if (s.De == max) return Language.GermanLatin;     // ß ä ö ü
        if (s.Fr == max) return Language.FrenchLatin;
        if (s.It == max) return Language.ItalianLatin;    // ì ò (weak)
        if (s.Es == max) return Language.SpanishLatin;    // ñ
        return Language.EnglishLatin;
    }

    private static bool IsUkrainianSpecific(char c) =>
        c is 'і' or 'І' or 'ї' or 'Ї' or 'є' or 'Є' or 'ґ' or 'Ґ';

    private static FrozenDictionary<char, ScoreDelta> BuildLatinScoreTable()
    {
        var t = new Dictionary<char, ScoreDelta>();

        void Set(char c, ScoreDelta d) => t[c] = d;
        void Each(string chars, ScoreDelta d) { foreach (var c in chars) t[c] = d; }

        // German
        Set('ß', new(De: 5));
        Each("äÄöÖ", new(De: 2, Sv: 1));   // also Swedish/Finnish
        Each("üÜ",   new(De: 2, Tr: 1));    // also Turkish

        // French
        Each("œŒ",   new(Fr: 5));
        Each("êÊôÔ", new(Fr: 2));
        Each("ëËïÏ", new(Fr: 2));
        Each("ûÛ",   new(Fr: 2));
        Each("âÂ",   new(Fr: 1, Ro: 1));    // also Romanian
        Each("çÇ",   new(Fr: 2, Pt: 1, Tr: 1)); // also PT/TR
        Each("éÉ",   new(Fr: 1, Pt: 1));
        Each("èÈ",   new(Fr: 1, It: 2));    // stronger Italian signal
        Each("àÀ",   new(Fr: 1, It: 1, Pt: 1));

        // Spanish
        Each("ñÑ",       new(Es: 5));
        Each("áÁóÓíÍúÚ", new(Es: 1, Pt: 1, Hu: 1)); // shared vowels; Czech uses these but not distinctively

        // Portuguese
        Each("ãÃõÕ", new(Pt: 5));

        // Italian
        Each("ìÌ", new(It: 3));
        Each("òÒ", new(It: 3));
        Each("ùÙ", new(It: 2, Fr: 1));

        // Polish
        Each("ąĄęĘ",       new(Pl: 5));
        Each("śŚźŹżŻćĆńŃ", new(Pl: 3));

        // Romanian
        Each("ăĂ",   new(Ro: 5));
        Each("șȘțȚ", new(Ro: 5)); // comma-below (correct form)
        Each("ţŢ",   new(Ro: 4)); // cedilla-t (legacy Romanian)
        Each("îÎ",   new(Ro: 2, Fr: 1));

        // Swedish / Nordic
        Each("åÅ",   new(Sv: 5));
        Each("øØæÆ", new(Sv: 3)); // Norwegian / Danish

        // Czech / Slovak
        Each("ěĚ",      new(Cs: 5));
        Each("řŘ",      new(Cs: 5));
        Each("šŠčČžŽ", new(Cs: 2, Tr: 1)); // caron consonants
        Each("ľĽŕŔĺĹ", new(Cs: 3));         // Slovak

        // Turkish
        Each("şŞ", new(Tr: 5));
        Each("ğĞ", new(Tr: 5));
        Set('ı',   new(Tr: 3)); // dotless i

        // Hungarian
        Each("őŐűŰ", new(Hu: 5)); // double acute — unique

        return t.ToFrozenDictionary();
    }
}
