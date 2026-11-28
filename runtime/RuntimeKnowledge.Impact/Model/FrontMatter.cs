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

    // Plain, single-quoted and double-quoted scalars are decoded as YAML does. A YAML null, and anchors, aliases,
    // tags, block and flow values, are treated as absent.
    private static string? Scalar(string raw)
    {
        var value = raw.Trim();
        if (value.Length >= 2 && value[0] == '\'' && value[^1] == '\'')
            return value[1..^1].Replace("''", "'");
        if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            return DoubleQuoted(value[1..^1]);
        if (value.Length > 0 && "&*!|>[{'\"".Contains(value[0])) return null;
        var comment = value.IndexOf(" #", StringComparison.Ordinal);
        if (comment >= 0) value = value[..comment].TrimEnd();
        return value is "" or "~" or "null" or "Null" or "NULL" ? null : value;
    }

    private static string? DoubleQuoted(string body)
    {
        var text = new System.Text.StringBuilder();
        for (var i = 0; i < body.Length; i++)
        {
            if (body[i] != '\\') { text.Append(body[i]); continue; }
            if (++i == body.Length) return null;
            switch (body[i])
            {
                case '\\': text.Append('\\'); break;
                case '"': text.Append('"'); break;
                case '/': text.Append('/'); break;
                case 'n': text.Append('\n'); break;
                case 't': text.Append('\t'); break;
                case ' ': text.Append(' '); break;
                default: return null;
            }
        }
        return text.ToString();
    }
}
