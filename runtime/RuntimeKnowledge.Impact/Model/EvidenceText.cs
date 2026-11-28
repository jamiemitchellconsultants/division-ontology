using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json.Nodes;

namespace RuntimeKnowledge.Impact.Model;

public sealed record CitedText(string SourceId, string EvidenceIri, string Path, int StartLine, int EndLine,
    string Role, IReadOnlyList<string>? Lines, PageMetadata? Page, JsonObject? Gap);

public static class EvidenceText
{
    public const string PageRole = "selected-document";

    private static readonly UTF8Encoding Strict = new(false, throwOnInvalidBytes: true);
    private static readonly ConditionalWeakTable<ImpactModel, Dictionary<(string, string), string?>> Decoded = new();

    /// <summary>Reads the cited lines of one evidence record from the bundle, strictly as UTF-8.</summary>
    public static CitedText Read(ImpactModel model, string sourceId, JsonObject evidence)
    {
        var iri = evidence["iri"]!.GetValue<string>();
        var path = evidence["path"]!.GetValue<string>();
        var start = evidence["startLine"]!.GetValue<int>();
        var end = evidence["endLine"]!.GetValue<int>();
        var role = evidence["role"]?.GetValue<string>() ?? "";

        var text = Decode(model, sourceId, path);
        var lines = text == null ? null : SplitLines(text);
        if (lines == null || start < 1 || end < start || end > lines.Count)
        {
            return new CitedText(sourceId, iri, path, start, end, role, null, null, new JsonObject
            {
                ["code"] = "evidence-text-unavailable",
                ["sourceId"] = sourceId,
                ["evidenceIri"] = iri,
                ["path"] = path,
                ["reason"] = "Cited text could not be read at the pinned commit"
            });
        }

        var cited = lines.Skip(start - 1).Take(end - start + 1).ToList();
        var page = role == PageRole && IsMarkdown(path) ? FrontMatter.Parse(text!) : null;
        return new CitedText(sourceId, iri, path, start, end, role, cited, page, page == null ? null : MetadataGap(sourceId, path, page));
    }

    // An exported page is a Markdown file that opens with front matter; any other selected document is ordinary.
    private static bool IsMarkdown(string path) =>
        path.EndsWith(".md", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".markdown", StringComparison.OrdinalIgnoreCase);

    /// <summary>The `page` object a path step's evidence gains: title and address when present, lines when readable.</summary>
    public static JsonObject StepPage(CitedText text)
    {
        var page = new JsonObject();
        AddPageFields(page, text);
        return page;
    }

    /// <summary>One `pageEvidence` entry on a lead.</summary>
    public static JsonObject LeadPage(CitedText text)
    {
        var entry = new JsonObject
        {
            ["evidenceIri"] = text.EvidenceIri,
            ["path"] = text.Path,
            ["startLine"] = text.StartLine,
            ["endLine"] = text.EndLine
        };
        AddPageFields(entry, text);
        return entry;
    }

    private static void AddPageFields(JsonObject target, CitedText text)
    {
        if (text.Page?.Title != null) target["title"] = text.Page.Title;
        if (text.Page?.ConfluenceUrl != null) target["confluenceUrl"] = text.Page.ConfluenceUrl;
        if (text.Lines != null) target["lines"] = new JsonArray(text.Lines.Select(l => (JsonNode)JsonValue.Create(l)!).ToArray());
    }

    internal static List<string> SplitLines(string text) =>
        text.Split('\n').Select(line => line.EndsWith('\r') ? line[..^1] : line).ToList();

    private static JsonObject? MetadataGap(string sourceId, string path, PageMetadata page)
    {
        var reason = (page.Title, page.ConfluenceUrl) switch
        {
            (null, null) => "Exported page has no title or Confluence address",
            (null, _) => "Exported page has no title",
            (_, null) => "Exported page has no Confluence address",
            _ => null
        };
        return reason == null ? null : new JsonObject
        {
            ["code"] = "page-metadata-missing",
            ["sourceId"] = sourceId,
            ["path"] = path,
            ["reason"] = reason
        };
    }

    private static string? Decode(ImpactModel model, string sourceId, string path)
    {
        var cache = Decoded.GetValue(model, _ => new Dictionary<(string, string), string?>());
        lock (cache)
        {
            if (cache.TryGetValue((sourceId, path), out var known)) return known;
            string? text;
            try
            {
                var bytes = model.Bundle.Read("graphEvidence", sourceId, path);
                text = bytes == null ? null : Strict.GetString(bytes);
            }
            catch (DecoderFallbackException) { text = null; }
            cache[(sourceId, path)] = text;
            return text;
        }
    }
}
