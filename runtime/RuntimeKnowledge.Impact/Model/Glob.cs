using System.Text.RegularExpressions;
using RuntimeKnowledge.Impact;

namespace RuntimeKnowledge.Impact.Model;

public static class Glob
{
    public static bool Matches(string selector, string path)
    {
        if (selector.Contains(".."))
            throw new ImpactException("invalid selector");
        if (selector.Contains('/') && selector[0] == '/')
            throw new ImpactException("invalid selector");

        // Build a regex from the glob pattern
        var regex = "^" + GlobToRegex(selector) + "$";
        return Regex.IsMatch(path, regex, RegexOptions.CultureInvariant);
    }

    private static string GlobToRegex(string pattern)
    {
        var result = new System.Text.StringBuilder();
        var i = 0;
        while (i < pattern.Length)
        {
            var c = pattern[i];
            switch (c)
            {
                case '*':
                    if (i + 1 < pattern.Length && pattern[i + 1] == '*')
                    {
                        // **
                        if (i + 2 < pattern.Length && pattern[i + 2] == '/')
                        {
                            result.Append("(?:[^/]+/)*");
                            i += 3;
                            continue;
                        }
                        else if (i + 2 == pattern.Length)
                        {
                            result.Append(".*");
                            i += 2;
                            continue;
                        }
                        result.Append("[^/]*");
                        i += 2;
                    }
                    else
                    {
                        result.Append("[^/]*");
                        i++;
                    }
                    break;
                case '?':
                    result.Append("[^/]");
                    i++;
                    break;
                case '.':
                case '+':
                case '(':
                case ')':
                case '[':
                case ']':
                case '{':
                case '}':
                case '|':
                case '^':
                case '$':
                case '~':
                    result.Append('\\').Append(c);
                    i++;
                    break;
                default:
                    result.Append(c);
                    i++;
                    break;
            }
        }
        return result.ToString();
    }
}
