using System.Text;
using System.Text.Json.Nodes;
using RuntimeKnowledge.Impact.Json;

namespace RuntimeKnowledge.Impact.Render;

/// <summary>Port of renderView from the Node C4 pilot.</summary>
public static class ViewMarkdown
{
    public static (string Json, string Markdown) Render(JsonObject view)
    {
        var json = EcmaJson.File(view);

        var outLines = new List<string>();
        outLines.Add("# C4 " + Escape(view["level"]!.GetValue<string>()) + " view");
        outLines.Add("");
        outLines.Add("Catalog commit: `" + Escape(view["snapshotId"]!.AsObject()["catalogCommit"]!.GetValue<string>()) + "`");
        outLines.Add("Registry commit: `" + Escape(view["registryCommit"]!.GetValue<string>()) + "`");
        outLines.Add("View digest: " + ViewDigest(view));
        outLines.Add("");
        outLines.Add("```mermaid");
        outLines.Add("flowchart LR");

        var focus = view["focus"]!.AsObject();
        outLines.Add("  subgraph boundary[\"" + Escape(focus["nodeIri"]!.GetValue<string>()) + " boundary\"]");

        foreach (var item in view["items"]!.AsArray().Select(n => n!.AsObject()).Where(i => i["insideBoundary"]!.GetValue<bool>()))
            outLines.Add("    " + ItemId(item["key"]!.AsObject()) + "[\"" + Escape(item["label"]!.GetValue<string>()) + "\"]");

        outLines.Add("  end");

        foreach (var item in view["items"]!.AsArray().Select(n => n!.AsObject()).Where(i => !i["insideBoundary"]!.GetValue<bool>()))
            outLines.Add("  " + ItemId(item["key"]!.AsObject()) + "[\"" + Escape(item["label"]!.GetValue<string>()) + "\"]");

        foreach (var edge in view["edges"]!.AsArray().Select(n => n!.AsObject()))
        {
            var fromId = ItemId(edge["from"]!.AsObject());
            var toId = ItemId(edge["to"]!.AsObject());
            var dash = edge["basis"]!.GetValue<string>() == "inferred" ? "-.->" : "-->";
            outLines.Add("  " + fromId + " " + dash + " " + toId);
        }

        foreach (var item in view["items"]!.AsArray().Select(n => n!.AsObject()))
            outLines.Add("  click " + ItemId(item["key"]!.AsObject()) + " \"#evidence-" + Digest(item["key"]!.AsObject()) + "\"");

        outLines.Add("```");
        outLines.Add("");
        outLines.Add("Legend: dashed relationships are inferred and tentative; architect-inspected code is separately identified.");
        outLines.Add("");
        outLines.Add("## Evidence");
        outLines.Add("");
        outLines.Add("| Item | Assertion / basis | Repository | Commit | Source | Blob |");
        outLines.Add("| --- | --- | --- | --- | --- | --- |");

        foreach (var item in view["items"]!.AsArray().Select(n => n!.AsObject()))
        {
            var itemKey = item["key"]!.AsObject();
            var evidenceList = item["evidence"];
            if (evidenceList == null) continue;
            foreach (var eNode in evidenceList.AsArray().Select(n => n!.AsObject()).Where(e => e != null))
            {
                var anchor = "<a id=\"evidence-" + Digest(itemKey) + "\"></a>";
                var label = Escape(item["label"]!.GetValue<string>());
                var assertionIri = Escape(item["assertionIri"]?.GetValue<string>() ?? item["basis"]!.GetValue<string>());
                var sourceId = Escape(itemKey["sourceId"]!.GetValue<string>());
                var commit = Escape(item["commit"]!.GetValue<string>());
                var path = Escape(eNode["path"]!.GetValue<string>());
                var startLine = eNode["startLine"]!.GetValue<int>();
                var endLine = eNode["endLine"]!.GetValue<int>();
                var blobId = Escape(eNode["blobId"]!.GetValue<string>());
                var link = Link(itemKey["sourceId"]!.GetValue<string>(), item["commit"]!.GetValue<string>(), eNode, startLine, endLine);
                outLines.Add("| " + anchor + label + " | " + assertionIri + " | " + sourceId + " | " + commit + " | [" + path + ":" + startLine + "-" + endLine + "](" + link + ") | " + blobId + " |");
            }
        }

        foreach (var edge in view["edges"]!.AsArray().Select(n => n!.AsObject()))
        {
            var evidenceList = edge["evidence"];
            if (evidenceList == null) continue;
            foreach (var eNode in evidenceList.AsArray().Select(n => n!.AsObject()).Where(e => e != null))
            {
                var relationship = Escape(edge["relationship"]!.GetValue<string>());
                var assertionIri = Escape(edge["assertionIri"]!.GetValue<string>());
                var basis = Escape(edge["basis"]!.GetValue<string>());
                var from = edge["from"]!.AsObject();
                var fromSourceId = Escape(from["sourceId"]!.GetValue<string>());
                var commit = Escape(edge["commit"]!.GetValue<string>());
                var path = Escape(eNode["path"]!.GetValue<string>());
                var startLine = eNode["startLine"]!.GetValue<int>();
                var endLine = eNode["endLine"]!.GetValue<int>();
                var blobId = Escape(eNode["blobId"]!.GetValue<string>());
                var link = Link(from["sourceId"]!.GetValue<string>(), edge["commit"]!.GetValue<string>(), eNode, startLine, endLine);
                outLines.Add("| " + relationship + " | " + assertionIri + " / " + basis + " | " + fromSourceId + " | " + commit + " | [" + path + ":" + startLine + "-" + endLine + "](" + link + ") | " + blobId + " |");
            }
        }

        outLines.Add("");
        outLines.Add("## Coverage");
        outLines.Add("");
        outLines.Add("| Source | Scope | Status | Reason |");
        outLines.Add("| --- | --- | --- | --- |");

        foreach (var c in view["coverage"]!.AsArray().Select(n => n!.AsObject()))
        {
            var sourceId = Escape(c["sourceId"]!.GetValue<string>());
            var selector = Escape(c["selector"]!.GetValue<string>());
            var status = Escape(c["status"]!.GetValue<string>());
            var reason = c["reason"] != null ? Escape(c["reason"]!.GetValue<string>()) : "";
            outLines.Add("| " + sourceId + " | " + selector + " | " + status + " | " + reason + " |");
        }

        outLines.Add("");
        outLines.Add("## Gaps");
        outLines.Add("");
        outLines.Add("| Code | Source | Scope | Reason |");
        outLines.Add("| --- | --- | --- | --- |");

        foreach (var gap in view["gaps"]!.AsArray().Select(n => n!.AsObject()))
        {
            var code = Escape(gap["code"]!.GetValue<string>());
            var sourceId = gap["sourceId"] != null ? Escape(gap["sourceId"]!.GetValue<string>()) : "";
            var selector = gap["selector"] != null ? Escape(gap["selector"]!.GetValue<string>()) : "";
            var reason = gap["reason"] != null ? Escape(gap["reason"]!.GetValue<string>()) : "";
            outLines.Add("| " + code + " | " + sourceId + " | " + selector + " | " + reason + " |");
        }

        if (!view["gaps"]!.AsArray().Any())
            outLines.Add("| None recorded | | | Coverage does not establish architectural completeness. |");

        return (json, string.Join("\n", outLines) + "\n");
    }

    private static string EncodeUriComponent(string s)
    {
        var bytes = Encoding.UTF8.GetBytes(s);
        var sb = new StringBuilder();
        foreach (var b in bytes)
        {
            var c = (char)b;
            if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') ||
                c == '-' || c == '_' || c == '.' || c == '!' || c == '~' || c == '*' || c == '\'' ||
                c == '(' || c == ')')
            {
                sb.Append(c);
            }
            else
            {
                sb.Append('%');
                sb.Append(((int)b).ToString("X2"));
            }
        }
        return sb.ToString();
    }

    private static string Escape(string? x)
    {
        if (x == null) return "";
        var s = x.Replace("&", "&amp;")
                 .Replace("<", "&lt;")
                 .Replace(">", "&gt;")
                 .Replace("\"", "&quot;")
                 .Replace("|", "&#124;")
                 .Replace("\r\n", " ")
                 .Replace("\n", " ")
                 .Replace("\r", " ");
        return s;
    }

    private static string Digest(JsonObject key) => EcmaJson.Sha256Hex(EcmaJson.Stringify(key)).Substring(0, 16);
    private static string ViewDigest(JsonObject view) => EcmaJson.CanonicalDigest(view);
    private static string ItemId(JsonObject key) => "n" + Digest(key);

    private static string Link(string sourceId, string commit, JsonObject evidence, int startLine, int endLine)
    {
        var path = evidence["path"]!.GetValue<string>();
        var encodedPath = string.Join("/", path.Split('/').Select(EncodeUriComponent));
        var url = "https://github.com/" + string.Join("/", sourceId.Split('/').Select(EncodeUriComponent)) +
                  "/blob/" + commit + "/" + encodedPath + "#L" + startLine;
        if (endLine != startLine) url += "-L" + endLine;
        return url;
    }
}
