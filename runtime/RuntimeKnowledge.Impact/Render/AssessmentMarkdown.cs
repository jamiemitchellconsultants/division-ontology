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
        var t = (Func<string?, string>)Inert;
        var pins = frozen["pins"]!.AsObject();
        var masterList = pins["masterList"]!.AsObject();
        var registry = pins["registry"] as JsonObject;

        var lines = new List<string>
        {
            "# Impact assessment: " + t(Str(record["requestId"])),
            "",
            LineStart(t(Str(record["description"]))),
            "",
            "Assessor: " + t(Str(record["assessor"])),
            "Snapshot: " + e(Str(pins["snapshotId"]?["catalogCommit"]))
        };
        foreach (var source in pins["snapshotId"]?["sources"] as JsonArray ?? [])
            lines.Add("Source " + e(Str(source!["id"])) + ": " + e(Str(source["commit"])));
        lines.AddRange(new[]
        {
            "Relationship master list: " + (Str(masterList["status"]) == "listed"
                ? e(Str(masterList["catalogCommit"])) + " (sha256 " + e(Str(masterList["sha256"])) + ")"
                : "none"),
            "C4 registry: " + (registry == null ? "none" : e(Str(registry["commit"])) +
                " (mappings sha256 " + e(Str(registry["digests"]?["mappings"])) +
                ", bindings sha256 " + e(Str(registry["digests"]?["bindings"])) + ")"),
            "Assessment digest: " + Str(frozen["digest"]),
            "",
            "## Confirmed leads"
        });

        foreach (var lead in frozen["leads"]!["confirmed"]!.AsArray())
            lines.Add("- " + Key(lead!["key"], e) + " — " + t(Str(lead["label"])));

        lines.Add("");
        lines.Add("## Not expected to be affected");
        foreach (var lead in frozen["leads"]!["rejected"]!.AsArray())
            lines.Add("- " + Key(lead!["key"], e) + " — " + t(Str(lead["reason"])));

        lines.Add("");
        lines.Add("## Likely impacts");
        foreach (var x in record["likelyImpacts"]!.AsArray())
            lines.Add("- " + Key(x!["key"], e) + " — " + t(Str(x["reason"])) +
                      "; paths " + List(x["pathIds"], e) +
                      "; evidence " + List(x["evidenceIris"], e) +
                      "; views " + List(x["viewIds"], e));

        if (record["traces"] is JsonArray traces)
        {
            lines.Add("");
            lines.Add("## Cited traces");
            foreach (var trace in traces)
                lines.Add("- depth " + Str(trace!["maxDepth"]) + " from " +
                          string.Join(", ", (trace["starts"] as JsonArray ?? []).Select(k => Key(k, e))));
        }

        lines.Add("");
        lines.Add("## Cited paths");
        foreach (var path in frozen["paths"] as JsonArray ?? [])
        {
            lines.Add("- " + e(Str(path!["id"])));
            var n = 0;
            foreach (var step in path["steps"]!.AsArray())
                lines.Add("  " + ++n + ". " + Step(step!.AsObject(), e));
        }

        lines.Add("");
        lines.Add("## Evidence");
        foreach (var ev in frozen["evidence"]!.AsArray())
        {
            lines.Add("- " + e(Str(ev!["evidenceIri"])) + " — " + e(Str(ev["role"])) + " in " + e(Str(ev["sourceId"])) + " " +
                      e(Str(ev["path"])) + ":" + Str(ev["startLine"]) + "-" + Str(ev["endLine"]) + " at " + e(Str(ev["commit"])));
            if (ev["page"] is not JsonObject page) continue;
            lines.Add("  Page: " + (page["title"] is null ? "untitled" : t(Str(page["title"]))) +
                      " (" + (page["confluenceUrl"] is null ? "no Confluence address" : e(Str(page["confluenceUrl"]))) + ")");
            foreach (var line in ev["lines"] as JsonArray ?? [])
                lines.Add("      " + e(Str(line)));
        }

        lines.Add("");
        lines.Add("## Unresolved gaps and questions");
        foreach (var x in record["unresolved"]!.AsArray())
            lines.Add("- " + t(Str(x!["code"])) + ": " + t(Str(x["reason"])));

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

    // B32, B34: each frozen step as traversed, with what supports it.
    private static string Step(JsonObject step, Func<string?, string> e) => Str(step["type"]) switch
    {
        "assertion" => (Str(step["direction"]) == "object-to-subject"
                ? Key(step["object"], e) + " → " + Key(step["subject"], e)
                : Key(step["subject"], e) + " → " + Key(step["object"], e)) +
            " via " + e(Str(step["relationship"])) + " (" + e(Str(step["direction"])) + "); assertion " +
            e(Str(step["assertionIri"])) + " at " + e(Str(step["commit"])) + "; evidence " + List(step["evidenceIris"], e),
        "reviewedLinkCrossing" => Key(step["from"], e) + " → " + Key(step["to"], e) + " by reviewed links through " +
            e(Str(step["sharedTerm"])) + " (" + string.Join(", ", (step["links"] as JsonArray ?? []).Select(l =>
                e(Str(l!["source"])) + " " + e(Str(l["local"])) + " " + e(Str(l["relation"])))) + ") at catalog " +
            e(Str(step["catalogCommit"])),
        "binding" => Key(step["from"], e) + " → " + Key(step["to"], e) + " by C4 binding at registry " +
            e(Str(step["registryCommit"])) + "; identity evidence " + e(Str(step["identityEvidence"]?["sourceId"])) + " " +
            e(Str(step["identityEvidence"]?["path"])) + ":" + Str(step["identityEvidence"]?["startLine"]) + "-" +
            Str(step["identityEvidence"]?["endLine"]),
        var type => e(type)
    };

    // Q4: free text stays literal. Markdown syntax characters are backslash-escaped, then markup is made inert.
    private static string Inert(string? x)
    {
        if (x == null) return "";
        var text = new System.Text.StringBuilder(x.Length);
        foreach (var c in x)
        {
            if (c is '\\' or '`' or '*' or '_' or '[' or ']' or '#' or '!' or '~' or '&') text.Append('\\');
            text.Append(c);
        }
        return AssessmentMarkdownV1.Escape(text.ToString());
    }

    // A line of its own must not open a list, quote or code block.
    private static string LineStart(string line)
    {
        if (line.StartsWith("    ", StringComparison.Ordinal)) return "&#32;" + line[1..];
        if (line.Length > 0 && line[0] is '-' or '+' or '=') return "\\" + line;
        var digits = 0;
        while (digits < line.Length && char.IsAsciiDigit(line[digits])) digits++;
        if (digits > 0 && digits < line.Length && line[digits] is '.' or ')') return line[..digits] + "\\" + line[digits..];
        return line;
    }

    private static string Key(JsonNode? key, Func<string?, string> e) =>
        e(Str(key?["sourceId"])) + ": " + e(Str(key?["nodeIri"]));

    private static string List(JsonNode? values, Func<string?, string> e) =>
        string.Join(", ", (values as JsonArray ?? []).Select(v => e(Str(v))));

    private static string? Str(JsonNode? node) => node is JsonValue v ? v.ToString() : null;

    private static bool Text(string? x) => x is { } s && s.Trim().Length > 0;
}
