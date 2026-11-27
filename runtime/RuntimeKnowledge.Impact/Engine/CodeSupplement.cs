using System.Text;
using System.Text.Json.Nodes;
using RuntimeKnowledge.Impact;
using RuntimeKnowledge.Impact.Json;
using RuntimeKnowledge.Impact.Model;

namespace RuntimeKnowledge.Impact.Engine;

/// <summary>Port of verifyCodeSupplement from the Node C4 pilot.</summary>
public static class CodeSupplement
{
    public static JsonObject Verify(ImpactModel model, (string SourceId, string NodeIri) focus, JsonObject supplement)
    {
        var s = supplement.AsObject();

        // Validate component focus
        if (!s.ContainsKey("component") ||
            s["sourceId"]!.GetValue<string>() != focus.SourceId ||
            s["component"]!.AsObject()["sourceId"]!.GetValue<string>() != focus.SourceId ||
            s["component"]!.AsObject()["nodeIri"]!.GetValue<string>() != focus.NodeIri)
        {
            throw new ImpactException("component focus mismatch");
        }

        var sourceId = s["sourceId"]!.GetValue<string>();
        var source = model.Source(sourceId);
        if (source == null)
            throw new ImpactException("source unavailable");

        // Validate reason
        var reason = s["reason"]?.GetValue<string>();
        if (string.IsNullOrEmpty(reason) || !reason.Any(c => !char.IsWhiteSpace(c)))
            throw new ImpactException("inspection reason required");

        // Read source file
        var path = s["path"]!.GetValue<string>();
        var bytes = model.Bundle.Read("inspectedSource", sourceId, path);
        if (bytes == null)
            bytes = model.Bundle.ReadFileFromCheckout(sourceId, path);
        if (bytes == null)
            throw new ImpactException("source path missing");

        // Verify blob
        var content = Encoding.UTF8.GetString(bytes);
        var reencoded = Encoding.UTF8.GetBytes(content);
        var blobId = GitBlob.Sha1Hex(reencoded);
        var expectedBlob = s["blobId"]!.GetValue<string>();
        if (blobId != expectedBlob)
            throw new ImpactException("blob mismatch");

        // Verify line range
        var startLine = s["startLine"]!.GetValue<int>();
        var endLine = s["endLine"]!.GetValue<int>();
        var lines = content.EndsWith("\n") ? content.Split('\n').Length - 1 : content.Split('\n').Length;
        if (startLine < 1 || endLine < startLine || endLine > lines)
            throw new ImpactException("invalid line range");

        // Verify role selector
        var role = s["role"]?.GetValue<string>() ?? "implementation";
        var selectors = source.EvidenceSelectors?[role];
        if (selectors == null || !(selectors is JsonArray selArray) || !selArray.Any(sel =>
        {
            var pattern = sel!.GetValue<string>();
            return Glob.Matches(pattern, path);
        }))
            throw new ImpactException("role selector mismatch");

        return new JsonObject
        {
            ["sourceId"] = sourceId,
            ["path"] = path,
            ["startLine"] = startLine,
            ["endLine"] = endLine,
            ["blobId"] = expectedBlob,
            ["component"] = s["component"]!.DeepClone()!,
            ["reason"] = reason,
            ["role"] = role,
            ["commit"] = source.Commit,
            ["basis"] = "architect-inspected"
        };
    }
}
