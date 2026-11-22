using System.Text.RegularExpressions;
using RuntimeKnowledge.Contracts;

namespace RuntimeKnowledge.Web.Services;

public sealed record GraphEvidenceRepositories(IReadOnlyDictionary<string, string> Map);

public static partial class GraphEvidenceLinks
{
    [GeneratedRegex(@"\Ahttps://github\.com/(?<owner>[A-Za-z0-9_.-]+)/(?<repo>[A-Za-z0-9_.-]+)\z", RegexOptions.CultureInvariant)]
    private static partial Regex RepositoryRoot();

    public static Uri? TryCreate(IReadOnlyDictionary<string, string> repositories, Provenance owner, Evidence evidence)
    {
        if (!repositories.TryGetValue(owner.Key.SourceId, out var root) || string.IsNullOrEmpty(root) ||
            owner.SourceCommit is not { Length: 40 or 64 } commit || !commit.All(Uri.IsHexDigit) ||
            string.IsNullOrWhiteSpace(evidence.Path))
            return null;

        var repository = RepositoryRoot().Match(root);
        if (!repository.Success || repository.Groups["owner"].Value is "." or ".." ||
            repository.Groups["repo"].Value is "." or "..")
            return null;

        var segments = evidence.Path.Split('/');
        if (segments.Any(segment => segment is "" or "." or ".." || segment.Contains('\\') || segment.Any(char.IsControl)))
            return null;

        var path = string.Join('/', segments.Select(Uri.EscapeDataString));
        var lines = evidence.StartLine > 0 && evidence.EndLine >= evidence.StartLine
            ? $"#L{evidence.StartLine}-L{evidence.EndLine}" : "";
        return new Uri($"{root}/blob/{commit}/{path}{lines}");
    }
}
