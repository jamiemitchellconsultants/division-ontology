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

    /// <summary>The pilot's split(/[^\p{L}\p{N}]+/u): runs of letters and numbers, by Unicode scalar, so letters
    /// outside the Basic Multilingual Plane are kept.</summary>
    public static IEnumerable<string> Tokens(string text)
    {
        var token = new StringBuilder();
        foreach (var rune in text.EnumerateRunes())
        {
            if (Rune.IsLetter(rune) || Rune.IsNumber(rune)) { token.Append(rune.ToString()); continue; }
            if (token.Length > 0) { yield return token.ToString(); token.Clear(); }
        }
        if (token.Length > 0) yield return token.ToString();
    }

    // Unicode's Final_Sigma condition: a cased letter before (skipping case-ignorables) and none after, by scalar.
    private static bool IsFinalSigma(string text, int at)
    {
        Rune? Before(int end, out int start)
        {
            start = end;
            if (end < 0) return null;
            if (char.IsLowSurrogate(text[end]) && end > 0 && char.IsHighSurrogate(text[end - 1])) start = end - 1;
            return Rune.TryGetRuneAt(text, start, out var r) ? r : null;
        }
        var index = at - 1;
        Rune? before;
        while ((before = Before(index, out var start)) is { } r && IsCaseIgnorable(r)) index = start - 1;
        if (before is not { } cased || !IsCased(cased)) return false;
        var after = at + 1;
        while (after < text.Length && Rune.TryGetRuneAt(text, after, out var r) && IsCaseIgnorable(r)) after += r.Utf16SequenceLength;
        return after >= text.Length || !Rune.TryGetRuneAt(text, after, out var next) || !IsCased(next);
    }

    private static bool IsCased(Rune r) => Rune.GetUnicodeCategory(r) is
        UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter or UnicodeCategory.TitlecaseLetter;

    private static bool IsCaseIgnorable(Rune r) => r.Value is '\'' or '.' or ':' or 0x00B7 or 0x2019 ||
        Rune.GetUnicodeCategory(r) is UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark or
            UnicodeCategory.Format or UnicodeCategory.ModifierLetter or UnicodeCategory.ModifierSymbol;
}
