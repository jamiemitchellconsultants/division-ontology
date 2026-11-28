using System.Globalization;
using System.Text;

namespace RuntimeKnowledge.Impact.Json;

/// <summary>String operations that must give JavaScript's results.</summary>
public static class EcmaText
{
    /// <summary>String.prototype.toLowerCase(): invariant lower-casing plus the two context and length
    /// rules it adds, the Greek final sigma and the dotted capital I.</summary>
    public static string ToLower(string text)
    {
        if (text.IndexOf('Σ') < 0 && text.IndexOf('İ') < 0) return text.ToLowerInvariant();
        var result = new StringBuilder(text.Length + 1);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c == 'İ') result.Append("i̇");
            else if (c == 'Σ') result.Append(IsFinalSigma(text, i) ? 'ς' : 'σ');
            else if (char.IsHighSurrogate(c) && i + 1 < text.Length)
            {
                result.Append(text.Substring(i, 2).ToLowerInvariant());
                i++;
            }
            else result.Append(char.ToLowerInvariant(c));
        }
        return result.ToString();
    }

    // Unicode's Final_Sigma condition: a cased letter before (skipping case-ignorables) and none after.
    private static bool IsFinalSigma(string text, int at)
    {
        var before = at - 1;
        while (before >= 0 && IsCaseIgnorable(text[before])) before--;
        if (before < 0 || !IsCased(text[before])) return false;
        var after = at + 1;
        while (after < text.Length && IsCaseIgnorable(text[after])) after++;
        return after == text.Length || !IsCased(text[after]);
    }

    private static bool IsCased(char c) => char.GetUnicodeCategory(c) is
        UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter or UnicodeCategory.TitlecaseLetter;

    private static bool IsCaseIgnorable(char c) => c is '\'' or '.' or ':' or '·' or '’' ||
        char.GetUnicodeCategory(c) is UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark or
            UnicodeCategory.Format or UnicodeCategory.ModifierLetter or UnicodeCategory.ModifierSymbol;
}
