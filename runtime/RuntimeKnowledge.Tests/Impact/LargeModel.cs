using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using RuntimeKnowledge.Contracts;
using RuntimeKnowledge.Impact.Json;

namespace RuntimeKnowledge.Tests.Impact;

/// <summary>A synthetic workspace bundle sized for Q1: one source with 10,000 nodes and 50,000 assertions.</summary>
internal static class LargeModel
{
    public const string SourceId = "fixture/large";
    public const string Base = "https://example.test/large/graph#";

    private static readonly string[] Words =
    [
        "alpha", "bravo", "charlie", "delta", "echo", "foxtrot", "golf", "hotel", "india", "juliet",
        "kilo", "lima", "mike", "november", "oscar", "papa", "quebec", "romeo", "sierra", "tango",
        "uniform", "victor", "whiskey", "xray", "yankee", "zulu", "amber", "birch", "cedar", "dune",
        "ember", "fjord", "glade", "heath", "isle", "jade", "knoll", "loch", "moor", "nook",
        "oak", "pine", "quay", "ridge", "shore", "tarn", "vale", "weald", "yew", "zephyr"
    ];

    public static (byte[] Manifest, IReadOnlyDictionary<string, byte[]> Blobs) Bundle(int nodes = 10_000, int assertions = 50_000)
    {
        var commit = new string('7', 40);
        var evidenceBytes = Encoding.UTF8.GetBytes("Synthetic claims lead to other claims.\n");
        var evidence = new JsonObject
        {
            ["iri"] = Base + "evidence", ["path"] = "src/claims.txt", ["startLine"] = 1, ["endLine"] = 1,
            ["blobId"] = GitBlob.Sha1Hex(evidenceBytes), ["role"] = "implementation"
        };
        var nodeArray = new JsonArray();
        for (var i = 0; i < nodes; i++)
            nodeArray.Add(new JsonObject
            {
                ["iri"] = Base + $"n{i:00000}", ["kind"] = "https://example.test/kinds#record",
                ["label"] = $"Claim {i} {Words[i % Words.Length]}"
            });
        var assertionArray = new JsonArray();
        for (var j = 0; j < assertions; j++)
            assertionArray.Add(new JsonObject
            {
                ["iri"] = Base + $"a{j}", ["subject"] = Base + $"n{j % nodes:00000}",
                ["object"] = Base + $"n{(int)(((long)j * 7919 + 1) % nodes):00000}",
                ["relationship"] = $"https://example.test/rel#r{j % 20}", ["basis"] = "direct",
                ["evidenceIris"] = new JsonArray(Base + "evidence")
            });

        var input = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["snapshotId"] = new JsonObject
            {
                ["catalogCommit"] = new string('8', 40), ["catalogSha256"] = new string('9', 64),
                ["sources"] = new JsonArray(new JsonObject { ["id"] = SourceId, ["commit"] = commit })
            },
            ["sources"] = new JsonArray(new JsonObject
            {
                ["id"] = SourceId, ["commit"] = commit,
                ["graph"] = new JsonObject
                {
                    ["status"] = "available", ["schemaVersion"] = 2, ["nodes"] = nodeArray, ["assertions"] = assertionArray,
                    ["evidence"] = new JsonArray(evidence), ["coverage"] = new JsonArray()
                }
            }),
            ["catalog"] = new JsonObject { ["concepts"] = new JsonArray() },
            ["reviewedLinks"] = new JsonArray(),
            ["registry"] = null
        };

        var blobs = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        BundleFile Add(string role, string sourceId, string path, byte[] bytes)
        {
            var digest = Convert.ToHexStringLower(SHA256.HashData(bytes));
            blobs[digest] = bytes;
            return new BundleFile(role, sourceId, path, digest, bytes.Length);
        }
        var files = new[]
        {
            Add("catalog", "division-core", "dist/catalog.json", JsonSerializer.SerializeToUtf8Bytes(new { schemaVersion = 1 })),
            Add("nquads", "division-core", "dist/combined.nq", []),
            Add("sourceInventory", "division-core", "sources.json", JsonSerializer.SerializeToUtf8Bytes(new { sources = Array.Empty<string>() })),
            Add("projection", "division-core", "projection.json", JsonSerializer.SerializeToUtf8Bytes(new PublishedProjection([], [], [], []), WorkspaceJson.Options)),
            Add("impactInput", "division-core", "impact-input.json", Encoding.UTF8.GetBytes(EcmaJson.Stringify(input) + "\n")),
            Add("graphEvidence", SourceId, "src/claims.txt", evidenceBytes)
        };
        var manifest = new BundleManifest(1, new string('8', 40), files[0].Sha256,
            [new SourceRevision(SourceId, commit, 2, "available")], null, files);
        return (JsonSerializer.SerializeToUtf8Bytes(manifest, WorkspaceJson.Options), blobs);
    }
}
