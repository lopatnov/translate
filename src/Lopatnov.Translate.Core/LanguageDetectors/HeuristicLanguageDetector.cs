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

    public LanguageDetectionResult Detect(string text)
    {
        if (string.IsNullOrWhiteSpace(text?.Trim()))
            return new LanguageDetectionResult("N/A", LanguageCodeFormat.None, 1f);

        var sample = text.Length > SampleLength ? text.AsSpan(0, SampleLength) : text.AsSpan();
        return new LanguageDetectionResult(DetectCode(sample), LanguageCodeFormat.Flores200);
    }

    private static string DetectCode(ReadOnlySpan<char> sample)
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
            else if (c is (>= 'À' and <= 'ɏ') or 'ı') // Latin Extended + Turkish dotless-i
            {
                latin++;
                ScoreLatinChar(c, ref ls);
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

        int kana = hiragana + katakana;

        if (cyrillic > 0 && cyrillic >= latin && cyrillic >= cjk && cyrillic >= arabic)
            return ukSpec > 0 ? Language.UkrainianCyrillic : Language.RussianCyrillic;

        if (hangul > 0) return Language.KoreanHangul;
        if (greek > 0 && greek > latin / 3) return Language.GreekGrek;
        if (hebrew > 0) return Language.HebrewHebr;

        if (cjk > latin) return kana > 0 ? Language.JapaneseJpan : Language.ChineseSimplified;
        if (kana > latin && kana > cjk) return Language.JapaneseJpan;
        if (arabic > latin) return Language.ArabicArab;
        if (devanagari > latin) return Language.HindiDevanagari;
        if (thai > latin) return Language.ThaiThai;

        return SelectLatinLanguage(in ls);
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

    private static void ScoreLatinChar(char c, ref LatinScores s)
    {
        switch (c)
        {
            // ── German ──────────────────────────────────────────────────────────
            case 'ß': s.De += 5; break;                            // unique
            case 'ä' or 'Ä' or 'ö' or 'Ö': s.De += 2; s.Sv += 1; break; // also Swedish/Finnish
            case 'ü' or 'Ü': s.De += 2; s.Tr += 1; break;                // also Turkish

            // ── French ──────────────────────────────────────────────────────────
            case 'œ' or 'Œ': s.Fr += 5; break;                    // unique
            case 'ê' or 'Ê' or 'ô' or 'Ô': s.Fr += 2; break;
            case 'ë' or 'Ë' or 'ï' or 'Ï': s.Fr += 2; break;
            case 'û' or 'Û': s.Fr += 2; break;
            case 'â' or 'Â': s.Fr += 1; s.Ro += 1; break;         // also Romanian
            case 'ç' or 'Ç': s.Fr += 2; s.Pt += 1; s.Tr += 1; break; // also PT/TR
            case 'é' or 'É': s.Fr += 1; s.Pt += 1; break;           // Czech uses é but it's not distinctive
            case 'è' or 'È': s.Fr += 1; s.It += 2; break;         // stronger Italian signal
            case 'à' or 'À': s.Fr += 1; s.It += 1; s.Pt += 1; break;

            // ── Spanish ─────────────────────────────────────────────────────────
            case 'ñ' or 'Ñ': s.Es += 5; break;                    // unique
            case 'á' or 'Á': s.Es += 1; s.Pt += 1; s.Hu += 1; break;
            case 'ó' or 'Ó': s.Es += 1; s.Pt += 1; s.Hu += 1; break;
            case 'í' or 'Í': s.Es += 1; s.Pt += 1; s.Hu += 1; break; // Czech uses í but not distinctively
            case 'ú' or 'Ú': s.Es += 1; s.Pt += 1; s.Hu += 1; break;

            // ── Portuguese ──────────────────────────────────────────────────────
            case 'ã' or 'Ã' or 'õ' or 'Õ': s.Pt += 5; break;     // unique

            // ── Italian ─────────────────────────────────────────────────────────
            case 'ì' or 'Ì': s.It += 3; break;                    // rare outside Italian
            case 'ò' or 'Ò': s.It += 3; break;
            case 'ù' or 'Ù': s.It += 2; s.Fr += 1; break;

            // ── Polish ──────────────────────────────────────────────────────────
            case 'ą' or 'Ą' or 'ę' or 'Ę': s.Pl += 5; break;     // unique
            case 'ś' or 'Ś' or 'ź' or 'Ź' or 'ż' or 'Ż'
              or 'ć' or 'Ć' or 'ń' or 'Ń': s.Pl += 3; break;

            // ── Romanian ────────────────────────────────────────────────────────
            case 'ă' or 'Ă': s.Ro += 5; break;                    // unique
            case 'ș' or 'Ș' or 'ț' or 'Ț': s.Ro += 5; break;     // comma-below (correct form)
            case 'ţ' or 'Ţ': s.Ro += 4; break;                    // cedilla-t (legacy Romanian)
            case 'î' or 'Î': s.Ro += 2; s.Fr += 1; break;         // common in Romanian

            // ── Swedish / Nordic ────────────────────────────────────────────────
            case 'å' or 'Å': s.Sv += 5; break;                    // unique to Nordic
            case 'ø' or 'Ø' or 'æ' or 'Æ': s.Sv += 3; break;     // Norwegian / Danish

            // ── Czech / Slovak ──────────────────────────────────────────────────
            case 'ě' or 'Ě': s.Cs += 5; break;                    // unique to Czech
            case 'ř' or 'Ř': s.Cs += 5; break;                    // unique to Czech
            case 'š' or 'Š': s.Cs += 2; s.Tr += 1; break;
            case 'č' or 'Č': s.Cs += 2; s.Tr += 1; break;
            case 'ž' or 'Ž': s.Cs += 2; s.Tr += 1; break;
            case 'ľ' or 'Ľ' or 'ŕ' or 'Ŕ' or 'ĺ' or 'Ĺ': s.Cs += 3; break; // Slovak

            // ── Turkish ─────────────────────────────────────────────────────────
            case 'ş' or 'Ş': s.Tr += 5; break;                    // unique (cedilla-s)
            case 'ğ' or 'Ğ': s.Tr += 5; break;                    // unique
            case 'ı': s.Tr += 3; break;                       // ı (dotless i)

            // ── Hungarian ───────────────────────────────────────────────────────
            case 'ő' or 'Ő' or 'ű' or 'Ű': s.Hu += 5; break;     // double acute — unique
        }
    }

    private static bool IsUkrainianSpecific(char c) =>
        c is 'і' or 'І' or 'ї' or 'Ї' or 'є' or 'Є' or 'ґ' or 'Ґ';
}
