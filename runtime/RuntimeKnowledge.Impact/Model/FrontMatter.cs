using System.Text.RegularExpressions;

namespace RuntimeKnowledge.Impact.Model;

public sealed record PageMetadata(string? Title, string? ConfluenceUrl);

public static class FrontMatter
{
    private static readonly Regex KeyValue = new(@"^([A-Za-z_][A-Za-z0-9_]*):(?:\s(.*))?$", RegexOptions.CultureInvariant);

    /// <summary>null when the file is not an exported page: it does not open with a front-matter block.</summary>
    public static PageMetadata? Parse(string text)
    {
        var lines = EvidenceText.SplitLines(text);
        if (lines.Count == 0 || lines[0] != "---") return null;
        var close = -1;
        for (var i = 1; i < lines.Count; i++)
            if (lines[i] == "---") { close = i; break; }
        if (close < 0) return null;

        string? title = null, url = null;
        for (var i = 1; i < close; i++)
        {
            var match = KeyValue.Match(lines[i]);
            if (!match.Success) continue;
            var value = Scalar(match.Groups[2].Value);
            switch (match.Groups[1].Value)
            {
                case "title" when title == null:
                    if (value != null && value.Trim().Length > 0) title = value;
                    break;
                case "confluence_url" when url == null:
                    if (value != null && Uri.TryCreate(value, UriKind.Absolute, out var u) && u.Scheme == "https") url = value;
                    break;
            }
        }
        return new PageMetadata(title, url);
    }

    // Plain, single-quoted and double-quoted scalars are decoded as YAML does, and a comment may follow any of them.
    // A YAML null, and anchors, aliases, tags, block and flow values, are treated as absent.
    private static string? Scalar(string raw)
    {
        var value = raw.Trim();
        if (value.Length > 0 && value[0] is '\'' or '"')
        {
            var (text, rest) = value[0] == '\'' ? SingleQuoted(value) : DoubleQuoted(value);
            return text != null && (rest.Length == 0 || rest[0] == '#') ? text : null;
        }
        if (value.Length > 0 && "&*!|>[{".Contains(value[0])) return null;
        var comment = value.IndexOf(" #", StringComparison.Ordinal);
        if (comment >= 0) value = value[..comment].TrimEnd();
        return value is "" or "~" or "null" or "Null" or "NULL" ? null : value;
    }

    // Returns the decoded text and what follows the closing quote, trimmed; null text when it never closes.
    private static (string? Text, string After) SingleQuoted(string value)
    {
        var text = new System.Text.StringBuilder();
        for (var i = 1; i < value.Length; i++)
        {
            if (value[i] != '\'') { text.Append(value[i]); continue; }
            if (i + 1 < value.Length && value[i + 1] == '\'') { text.Append('\''); i++; continue; }
            return (text.ToString(), value[(i + 1)..].Trim());
        }
        return (null, "");
    }

    private static (string? Text, string After) DoubleQuoted(string value)
    {
        var text = new System.Text.StringBuilder();
        for (var i = 1; i < value.Length; i++)
        {
            var c = value[i];
            if (c == '"') return (text.ToString(), value[(i + 1)..].Trim());
            if (c != '\\') { text.Append(c); continue; }
            if (++i == value.Length) return (null, "");
            switch (value[i])
            {
                case '\\': text.Append('\\'); break;
                case '"': text.Append('"'); break;
                case '/': text.Append('/'); break;
                case ' ': text.Append(' '); break;
                case '0': text.Append('\0'); break;
                case 'a': text.Append('\a'); break;
                case 'b': text.Append('\b'); break;
                case 't': text.Append('\t'); break;
                case 'n': text.Append('\n'); break;
                case 'v': text.Append('\v'); break;
                case 'f': text.Append('\f'); break;
                case 'r': text.Append('\r'); break;
                case 'e': text.Append('\u001B'); break;
                case 'N': text.Append('\u0085'); break;
                case '_': text.Append('\u00A0'); break;
                case 'L': text.Append('\u2028'); break;
                case 'P': text.Append('\u2029'); break;
                case 'x' or 'u' or 'U':
                    var digits = value[i] switch { 'x' => 2, 'u' => 4, _ => 8 };
                    if (i + digits >= value.Length ||
                        !int.TryParse(value.AsSpan(i + 1, digits), System.Globalization.NumberStyles.HexNumber, null, out var code) ||
                        !System.Text.Rune.IsValid(code))
                        return (null, "");
                    text.Append(new System.Text.Rune(code).ToString());
                    i += digits;
                    break;
                default: return (null, "");
            }
        }
        return (null, "");
    }
}
