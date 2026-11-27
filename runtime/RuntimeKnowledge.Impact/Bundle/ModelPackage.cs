using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using RuntimeKnowledge.Impact.Json;

namespace RuntimeKnowledge.Impact.Bundle;

/// <summary>The bundle objects the impact engine needs for one snapshot, stored with the workspace (ADR 0030).
/// A model built from a package is the model the command line builds from the same bundle (B23).</summary>
public static class ModelPackage
{
    public static readonly string[] Roles = ["impactInput", "relationshipMasterList", "graphEvidence", "inspectedSource"];

    /// <summary>Deterministic, ZLib-compressed package of a manifest and its impact objects.</summary>
    public static (byte[] Package, string Sha256) Create(string manifestId, byte[] manifestBytes, Func<string, byte[]> blob)
    {
        var manifest = JsonNode.Parse(manifestBytes)!.AsObject();
        var digests = manifest["files"]!.AsArray()
            .Where(f => Roles.Contains(f!["role"]!.GetValue<string>()))
            .Select(f => f!["sha256"]!.GetValue<string>())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(d => d, StringComparer.Ordinal);
        var layout = new JsonObject
        {
            ["manifestId"] = manifestId,
            ["manifest"] = Convert.ToBase64String(manifestBytes),
            ["objects"] = new JsonArray(digests.Select(d => (JsonNode)new JsonObject
            {
                ["sha256"] = d,
                ["bytes"] = Convert.ToBase64String(blob(d))
            }).ToArray())
        };
        var raw = Encoding.UTF8.GetBytes(EcmaJson.Stringify(layout));
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
            zlib.Write(raw);
        return (compressed.ToArray(), Convert.ToHexStringLower(SHA256.HashData(raw)));
    }

    public static ImpactBundle Open(byte[] package)
    {
        using var input = new ZLibStream(new MemoryStream(package), CompressionMode.Decompress);
        using var raw = new MemoryStream();
        input.CopyTo(raw);
        var layout = JsonNode.Parse(raw.ToArray())!.AsObject();
        var objects = layout["objects"]!.AsArray().ToDictionary(
            o => o!["sha256"]!.GetValue<string>(),
            o => Convert.FromBase64String(o!["bytes"]!.GetValue<string>()),
            StringComparer.Ordinal);
        return ImpactBundle.FromObjects(layout["manifestId"]!.GetValue<string>(),
            Convert.FromBase64String(layout["manifest"]!.GetValue<string>()),
            sha => objects.TryGetValue(sha, out var bytes) ? bytes : null);
    }
}
