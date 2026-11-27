using System.Text.Json.Nodes;
using RuntimeKnowledge.Impact;

namespace RuntimeKnowledge.Impact.Render;

/// <summary>Renders a frozen schema 2 assessment (B25, B30, B32).</summary>
public static class AssessmentMarkdown
{
    public static string Render(JsonObject frozen)
    {
        var record = frozen["record"]!.AsObject();
        if (!Text(Str(record["assessor"])))
            throw new ImpactException("assessor is required");

        var e = (Func<string?, string>)AssessmentMarkdownV1.Escape;
        var pins = frozen["pins"]!.AsObject();
        var masterList = pins["masterList"]!.AsObject();
        var registry = pins["registry"] as JsonObject;

        var lines = new List<string>
        {
            "# Impact assessment: " + e(Str(record["requestId"])),
            "",
            e(Str(record["description"])),
            "",
            "Assessor: " + e(Str(record["assessor"])),
            "Snapshot: " + e(Str(pins["snapshotId"]?["catalogCommit"])),
            "Relationship master list: " + (Str(masterList["status"]) == "listed"
                ? e(Str(masterList["catalogCommit"])) + " (sha256 " + e(Str(masterList["sha256"])) + ")"
                : "none"),
            "C4 registry: " + (registry == null ? "none" : e(Str(registry["commit"]))),
            "Assessment digest: " + Str(frozen["digest"]),
            "",
            "## Confirmed leads"
        };

        foreach (var lead in frozen["leads"]!["confirmed"]!.AsArray())
            lines.Add("- " + Key(lead!["key"], e) + " — " + e(Str(lead["label"])));

        lines.Add("");
        lines.Add("## Not expected to be affected");
        foreach (var lead in frozen["leads"]!["rejected"]!.AsArray())
            lines.Add("- " + Key(lead!["key"], e) + " — " + e(Str(lead["reason"])));

        lines.Add("");
        lines.Add("## Likely impacts");
        foreach (var x in record["likelyImpacts"]!.AsArray())
            lines.Add("- " + Key(x!["key"], e) + " — " + e(Str(x["reason"])) +
                      "; paths " + List(x["pathIds"], e) +
                      "; evidence " + List(x["evidenceIris"], e) +
                      "; views " + List(x["viewIds"], e));

        lines.Add("");
        lines.Add("## Evidence");
        foreach (var ev in frozen["evidence"]!.AsArray())
        {
            lines.Add("- " + e(Str(ev!["evidenceIri"])) + " — " + e(Str(ev["role"])) + " in " + e(Str(ev["sourceId"])) + " " +
                      e(Str(ev["path"])) + ":" + Str(ev["startLine"]) + "-" + Str(ev["endLine"]) + " at " + e(Str(ev["commit"])));
            if (ev["page"] is not JsonObject page) continue;
            lines.Add("  Page: " + (page["title"] is null ? "untitled" : e(Str(page["title"]))) +
                      " (" + (page["confluenceUrl"] is null ? "no Confluence address" : e(Str(page["confluenceUrl"]))) + ")");
            foreach (var line in ev["lines"] as JsonArray ?? [])
                lines.Add("      " + e(Str(line)));
        }

        lines.Add("");
        lines.Add("## Unresolved gaps and questions");
        foreach (var x in record["unresolved"]!.AsArray())
            lines.Add("- " + e(Str(x!["code"])) + ": " + e(Str(x["reason"])));

        lines.AddRange(GapSections(frozen, e));
        return string.Join("\n", lines) + "\n";
    }

    /// <summary>The three gap sections every rendered assessment carries, schema 1 included (B30, B37 item 7).</summary>
    internal static IEnumerable<string> GapSections(JsonObject frozen, Func<string?, string> e)
    {
        var gaps = frozen["gaps"]!.AsObject();
        yield return "";
        yield return "## Snapshot coverage gaps";
        foreach (var g in gaps["snapshot"]!.AsArray())
            yield return "- " + e(Str(g!["code"])) + ": " + e(Str(g["sourceId"])) + " " + e(Str(g["selector"])) + " — " + e(Str(g["reason"]));
        yield return "";
        yield return "## Gaps from cited path traces";
        foreach (var g in gaps["traces"]!.AsArray())
            yield return "- " + e(Str(g!["code"])) + ": " + e(Str(g["reason"]));
        yield return "";
        yield return "## Evidence gaps";
        foreach (var g in gaps["evidence"]!.AsArray())
            yield return "- " + e(Str(g!["code"])) + ": " + e(Str(g["sourceId"])) + " " + e(Str(g["path"])) + " — " + e(Str(g["reason"]));
    }

    private static string Key(JsonNode? key, Func<string?, string> e) =>
        e(Str(key?["sourceId"])) + ": " + e(Str(key?["nodeIri"]));

    private static string List(JsonNode? values, Func<string?, string> e) =>
        string.Join(", ", (values as JsonArray ?? []).Select(v => e(Str(v))));

    private static string? Str(JsonNode? node) => node is JsonValue v ? v.ToString() : null;

    private static bool Text(string? x) => x is { } s && s.Trim().Length > 0;
}
