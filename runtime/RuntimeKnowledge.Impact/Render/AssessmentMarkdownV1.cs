using System.Text.Json.Nodes;
using RuntimeKnowledge.Impact;
using RuntimeKnowledge.Impact.Engine;
using RuntimeKnowledge.Impact.Json;
using ImpactModel = RuntimeKnowledge.Impact.Model.ImpactModel;

namespace RuntimeKnowledge.Impact.Render;

/// <summary>Port of renderAssessment from the Node C4 pilot.</summary>
public static class AssessmentMarkdownV1
{
    public static string Render(JsonObject record, ImpactModel model, JsonArray paths, IReadOnlyList<JsonObject> views)
    {
        if (!Text(record["architect"]?.GetValue<string>()))
            throw new ImpactException("architect is required");

        var frozen = Assessments.Freeze(record, model, paths, views);
        var digest = EcmaJson.CanonicalDigest(record);
        var escaped = Escape;

        var lines = new List<string>
        {
            "# Impact assessment: " + escaped(record["requestId"]!.GetValue<string>()),
            "",
            escaped(record["description"]!.GetValue<string>()),
            "",
            "Architect: " + escaped(record["architect"]!.GetValue<string>()),
            "Snapshot: " + escaped(record["snapshotId"]!.AsObject()["catalogCommit"]!.GetValue<string>()),
            "Assessment digest: " + digest,
            "",
            "## Confirmed leads"
        };

        foreach (var k in record["confirmedLeads"]!.AsArray())
            lines.Add("- " + escaped(k!.AsObject()["sourceId"]!.GetValue<string>()) + ": " + escaped(k.AsObject()["nodeIri"]!.GetValue<string>()));

        lines.Add("");
        lines.Add("## Not expected to be affected");
        foreach (var x in record["rejectedLeads"]!.AsArray())
            lines.Add("- " + escaped(x!["key"]!.AsObject()["sourceId"]!.GetValue<string>()) + ": " +
                      escaped(x["key"]!.AsObject()["nodeIri"]!.GetValue<string>()) + " — " + escaped(x["reason"]!.GetValue<string>()));

        lines.Add("");
        lines.Add("## Likely impacts");
        foreach (var x in record["likelyImpacts"]!.AsArray())
        {
            var pathIds = x["pathIds"]!.AsArray();
            var evidenceIris = x["evidenceIris"]!.AsArray();
            var viewIds = x["viewIds"]?.AsArray() ?? new JsonArray();
            lines.Add("- " + escaped(x["key"]!.AsObject()["sourceId"]!.GetValue<string>()) + ": " +
                      escaped(x["key"]!.AsObject()["nodeIri"]!.GetValue<string>()) + " — " +
                      escaped(x["reason"]!.GetValue<string>()) + "; paths " +
                      string.Join(", ", pathIds.Select(p => escaped(p!.GetValue<string>()))) +
                      "; evidence " + string.Join(", ", evidenceIris.Select(e => escaped(e!.GetValue<string>()))) +
                      "; views " + string.Join(", ", viewIds.Select(v => escaped(v!.GetValue<string>()))));
        }

        lines.Add("");
        lines.Add("## Unresolved gaps and questions");
        foreach (var x in record["unresolved"]!.AsArray())
            lines.Add("- " + escaped(x["code"]!.GetValue<string>()) + ": " + escaped(x["reason"]!.GetValue<string>()));

        lines.AddRange(AssessmentMarkdown.GapSections(frozen, Escape));
        return string.Join("\n", lines) + "\n";
    }

    private static bool Text(string? x) => x is { } s && s.Trim().Length > 0;

    internal static string Escape(string? x)
    {
        if (x == null) return "";
        var s = x.Replace("\r", "").Replace("\n", " ")
                 .Replace("|", "&#124;")
                 .Replace("<", "&lt;")
                 .Replace(">", "&gt;");
        return s;
    }
}
