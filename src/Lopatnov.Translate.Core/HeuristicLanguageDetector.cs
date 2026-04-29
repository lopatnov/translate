using Lopatnov.Translate.Core.Abstractions;

namespace Lopatnov.Translate.Core;

/// <summary>
/// Lightweight language detector based on Unicode block analysis and Latin diacritic scoring.
/// Handles the languages supported by this service without requiring an ML model.
/// Falls back to English for ambiguous Latin-script text.
/// </summary>
public sealed class HeuristicLanguageDetector : ILanguageDetector
{
    private const int SampleLength = 200;

    public string Detect(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return Language.EnglishLatin;

        var sample = text.Length > SampleLength ? text.AsSpan(0, SampleLength) : text.AsSpan();

        int cyrillic = 0, latin = 0, cjk = 0, arabic = 0, hiragana = 0, katakana = 0,
            devanagari = 0, thai = 0;
        int ukSpecific = 0;
        int de = 0, fr = 0, es = 0, pl = 0;

        foreach (var c in sample)
        {
            if (c is >= 'Ѐ' and <= 'ӿ')
            {
                cyrillic++;
                if (IsUkrainianSpecific(c)) ukSpecific++;
            }
            else if ((c is >= 'A' and <= 'Z') || (c is >= 'a' and <= 'z'))
                latin++;
            else if (c is >= 'À' and <= 'ɏ')
            {
                latin++;
                ScoreLatinDiacritic(c, ref de, ref fr, ref es, ref pl);
            }
            else if (c is >= '一' and <= '鿿') cjk++;
            else if (c is >= '぀' and <= 'ゟ') hiragana++;
            else if (c is >= '゠' and <= 'ヿ') katakana++;
            else if (c is >= '؀' and <= 'ۿ') arabic++;
            else if (c is >= 'ऀ' and <= 'ॿ') devanagari++;
            else if (c is >= '฀' and <= '๿') thai++;
        }

        // Dominant non-Latin script wins outright.
        int kana = hiragana + katakana;
        int scriptMax = Math.Max(Math.Max(cyrillic, arabic), Math.Max(devanagari, thai));

        if (cyrillic > 0 && cyrillic >= latin && cyrillic >= cjk && cyrillic >= arabic)
            return ukSpecific > 0 ? Language.UkrainianCyrillic : Language.RussianCyrillic;

        if (cjk > latin)
            return kana > 0 ? Language.JapaneseJpan : Language.ChineseSimplified;

        if (kana > latin && kana > cjk)
            return Language.JapaneseJpan;

        if (arabic > latin)
            return Language.ArabicArab;

        if (devanagari > latin)
            return Language.HindiDevanagari;

        if (thai > latin)
            return Language.ThaiThai;

        // Latin script: pick language with highest diacritic score.
        int maxScore = Math.Max(Math.Max(de, fr), Math.Max(es, pl));
        if (maxScore == 0)
            return Language.EnglishLatin;

        // Ties broken by priority: pl > de > fr > es
        if (pl == maxScore) return Language.PolishLatin;
        if (de == maxScore) return Language.GermanLatin;
        if (fr == maxScore) return Language.FrenchLatin;
        return Language.SpanishLatin;
    }

    private static bool IsUkrainianSpecific(char c) =>
        // і/І, ї/Ї, є/Є, ґ/Ґ are present in Ukrainian but absent from Russian.
        c is 'і' or 'І' or 'ї' or 'Ї' or 'є' or 'Є' or 'ґ' or 'Ґ';

    private static void ScoreLatinDiacritic(char c, ref int de, ref int fr, ref int es, ref int pl)
    {
        switch (c)
        {
            // German-exclusive: ä ö ü ß Ä Ö Ü
            case 'ä' or 'ö' or 'ü' or 'Ä' or 'Ö' or 'Ü': de += 2; break;
            case 'ß': de += 3; break;

            // French-exclusive or strongly French: œ ç â î
            case 'œ' or 'Œ': fr += 3; break;
            case 'ç' or 'Ç': fr += 2; break;
            case 'â' or 'î' or 'ô' or 'ê' or 'ë' or 'ï': fr += 2; break;
            case 'é' or 'è' or 'à': fr++; break; // shared but most common in French

            // Spanish-exclusive: ñ
            case 'ñ' or 'Ñ': es += 3; break;

            // Polish-exclusive: ą ę ś ź ż ć ń
            case 'ą' or 'Ą' or 'ę' or 'Ę': pl += 3; break;
            case 'ś' or 'Ś' or 'ź' or 'Ź' or 'ż' or 'Ż' or 'ć' or 'Ć' or 'ń' or 'Ń': pl += 2; break;
        }
    }
}
