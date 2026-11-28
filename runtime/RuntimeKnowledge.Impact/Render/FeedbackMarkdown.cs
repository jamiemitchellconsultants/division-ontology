using System.Text.Json.Nodes;
using RuntimeKnowledge.Impact;

namespace RuntimeKnowledge.Impact.Render;

/// <summary>Port of renderFeedback from the Node C4 pilot.</summary>
public static class FeedbackMarkdown
{
    public static string Render(JsonObject record, JsonObject assessment)
    {
        var assessmentDigest = assessment["digest"]!.GetValue<string>();
        var recordDigest = record["assessmentDigest"]?.GetValue<string>();

        if (record["schemaVersion"]?.GetValue<int>() != 1 ||
            recordDigest != assessmentDigest ||
            recordDigest == null || recordDigest.Length != 64 ||
            !AllHex(recordDigest))
            throw new ImpactException("feedback assessment digest mismatch");

        if (record.ContainsKey("approvalStatus") || record.ContainsKey("approved"))
            throw new ImpactException("approval is not supported");

        var scalar = new[] { "architect", "reviewer", "perceivedAccuracy", "perceivedCompleteness", "confidence" };
        var lists = new[] { "usefulDiscoveries", "suspectedMisses", "falseLeads", "examples", "openQuestions" };

        foreach (var k in scalar)
        {
            var v = record[k];
            if (v is not JsonValue jv || jv.GetValue<string>() is not string s || string.IsNullOrEmpty(s.Trim()))
                throw new ImpactException("invalid qualitative feedback");
        }

        foreach (var k in lists)
        {
            if (record[k] is not JsonArray arr)
                throw new ImpactException("invalid qualitative feedback");
            foreach (var item in arr)
            {
                if (item is not JsonValue jv || jv.GetValue<string>() is not string s)
                    throw new ImpactException("invalid qualitative feedback");
            }
        }

        var esc = Escape;
        var outLines = new List<string>
        {
            "# Pilot feedback: " + esc(assessment["requestId"]!.GetValue<string>()),
            "",
            "Assessment digest: " + record["assessmentDigest"]!.GetValue<string>(),
            ""
        };

        foreach (var k in scalar)
            outLines.Add("- " + esc(TitleCase(k)) + ": " + esc(record[k]!.GetValue<string>()));

        foreach (var k in lists)
        {
            outLines.Add("");
            outLines.Add("## " + esc(TitleCase(k)));
            foreach (var value in record[k]!.AsArray())
                outLines.Add("- " + esc(value!.GetValue<string>()));
        }

        return string.Join("\n", outLines) + "\n";
    }

    private static bool AllHex(string s)
    {
        foreach (var c in s)
        {
            if (!((c >= 'a' && c <= 'f') || (c >= '0' && c <= '9'))) return false;
        }
        return true;
    }

    private static string TitleCase(string s)
    {
        // Insert space before each uppercase letter (except the first character)
        var result = new System.Text.StringBuilder();
        for (var i = 0; i < s.Length; i++)
        {
            if (i > 0 && char.IsUpper(s[i])) result.Append(' ');
            result.Append(s[i]);
        }
        return result.ToString();
    }

    private static string Escape(string? x)
    {
        if (x == null) return "";
        return x.Replace("\r", "").Replace("\n", " ")
                .Replace("<", "&lt;")
                .Replace(">", "&gt;");
    }
}
