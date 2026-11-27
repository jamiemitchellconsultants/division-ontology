using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using RuntimeKnowledge.Impact.Bundle;
using RuntimeKnowledge.Impact.Json;
using RuntimeKnowledge.Impact.Model;

namespace RuntimeKnowledge.Tests.Impact;

/// <summary>A literal port of makeModel() in test/fixtures/c4/fixture-repos.mjs at c6c071c, as an impactInput
/// in an in-memory bundle. Keep it row for row with the JavaScript, including IRIs, labels and blob ids.</summary>
internal static class TestModels
{
    private const string K = "https://ontology-kit.dev/schema#";
    private const string A = "https://alpha.example/#";
    private const string B = "https://beta.example/#";
    private const string AlphaId = "fixture/Alpha";
    private const string BetaId = "fixture/Beta";

    public static ImpactModel MakeModel(Action<JsonObject>? change = null, Func<string, byte[]>? blobOverride = null,
        JsonObject? masterList = null)
    {
        var alphaCommit = new string('c', 40);
        var betaCommit = new string('d', 40);
        var catalogCommit = new string('a', 40);
        var registryCommit = new string('e', 40);
        var appBytes = blobOverride?.Invoke("src/app.ts") ?? Encoding.UTF8.GetBytes("export const api = 1;\n");
        var appSha256 = Convert.ToHexStringLower(SHA256.HashData(appBytes));

        static JsonObject Key(string sourceId, string nodeIri) => new() { ["sourceId"] = sourceId, ["nodeIri"] = nodeIri };
        static JsonObject Node(string iri, string kind, string label) => new() { ["iri"] = iri, ["kind"] = K + kind, ["label"] = label };
        var keys = new Dictionary<string, (string SourceId, string NodeIri)>
        {
            ["alphaSystem"] = (AlphaId, A + "system"),
            ["alphaContainer"] = (AlphaId, A + "container"),
            ["alphaComponent"] = (AlphaId, A + "component"),
            ["alphaCode"] = (AlphaId, A + "code"),
            ["externalBeta"] = (AlphaId, A + "external"),
            ["betaSystem"] = (BetaId, B + "system")
        };

        var nodes = new JsonArray(
            Node(A + "system", "system", "Alpha API"),
            Node(A + "container", "component", "Alpha service"),
            Node(A + "component", "component", "API handler"),
            Node(A + "code", "source-symbol", "handle"),
            Node(A + "external", "system", "Beta"));

        var evidence = new JsonArray(new JsonObject
        {
            ["iri"] = A + "evidence", ["path"] = "src/app.ts", ["startLine"] = 1, ["endLine"] = 1,
            ["blobId"] = new string('1', 40), ["role"] = "implementation"
        });

        var assertions = new JsonArray();
        foreach (var (name, subject, obj, relationship, basis) in new[]
        {
            ("one", "system", "container", "contains", "direct"),
            ("two", "container", "component", "contains", "direct"),
            ("three", "component", "code", "contains", "direct"),
            ("four", "system", "external", "communicates-with", "inferred")
        })
            assertions.Add(new JsonObject
            {
                ["iri"] = A + name, ["subject"] = A + subject, ["object"] = A + obj, ["relationship"] = K + relationship,
                ["basis"] = basis, ["evidenceIris"] = new JsonArray(A + "evidence")
            });

        var mappings = new JsonArray();
        foreach (var (name, level, role, parent, support) in new (string, string, string, string?, string)[]
        {
            ("alphaSystem", "context", "software-system", null, "one"),
            ("alphaContainer", "container", "container", "alphaSystem", "one"),
            ("alphaComponent", "component", "component", "alphaContainer", "two"),
            ("alphaCode", "code", "code", "alphaComponent", "three"),
            ("externalBeta", "context", "software-system", null, "four")
        })
        {
            var mapping = new JsonObject { ["sourceId"] = keys[name].SourceId, ["nodeIri"] = keys[name].NodeIri, ["level"] = level, ["role"] = role };
            if (parent != null) mapping["parent"] = Key(keys[parent].SourceId, keys[parent].NodeIri);
            mapping["supportAssertionIri"] = A + support;
            mapping["supportEvidenceIri"] = A + "evidence";
            mappings.Add(mapping);
        }

        var binding = new JsonObject
        {
            ["fromSourceId"] = AlphaId, ["externalNodeIri"] = A + "external", ["toSourceId"] = BetaId, ["targetNodeIri"] = B + "system",
            ["sourceAssertionIri"] = A + "four", ["sourceEvidenceIri"] = A + "evidence",
            ["identityEvidence"] = new JsonObject
            {
                ["sourceId"] = AlphaId, ["path"] = "src/app.ts", ["startLine"] = 1, ["endLine"] = 1,
                ["blobId"] = new string('1', 40), ["reason"] = "reviewed"
            }
        };

        var impactInput = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["snapshotId"] = new JsonObject
            {
                ["catalogCommit"] = catalogCommit, ["catalogSha256"] = new string('b', 64),
                ["sources"] = new JsonArray(
                    new JsonObject { ["id"] = AlphaId, ["commit"] = alphaCommit },
                    new JsonObject { ["id"] = BetaId, ["commit"] = betaCommit })
            },
            ["sources"] = new JsonArray(
                new JsonObject
                {
                    ["id"] = AlphaId, ["commit"] = alphaCommit,
                    ["graph"] = new JsonObject
                    {
                        ["status"] = "available", ["nodes"] = nodes, ["assertions"] = assertions,
                        ["evidence"] = evidence, ["coverage"] = new JsonArray()
                    }
                },
                new JsonObject
                {
                    ["id"] = BetaId, ["commit"] = betaCommit,
                    ["graph"] = new JsonObject
                    {
                        ["status"] = "available", ["nodes"] = new JsonArray(Node(B + "system", "system", "Beta")),
                        ["assertions"] = new JsonArray(), ["evidence"] = new JsonArray(), ["coverage"] = new JsonArray()
                    }
                }),
            ["catalog"] = new JsonObject
            {
                ["concepts"] = new JsonArray(new JsonObject
                {
                    ["iri"] = "https://alpha.example/concept", ["source"] = AlphaId, ["label"] = "Alpha concept", ["definition"] = "catalog only"
                })
            },
            ["reviewedLinks"] = new JsonArray(),
            ["registry"] = new JsonObject
            {
                ["commit"] = registryCommit, ["mappingSha256"] = new string('f', 64), ["bindingSha256"] = new string('0', 64),
                ["mappings"] = mappings, ["bindings"] = new JsonArray(binding), ["gaps"] = new JsonArray()
            }
        };

        var listBytes = masterList == null ? null : Encoding.UTF8.GetBytes(EcmaJson.Stringify(masterList));
        var listSha256 = listBytes == null ? null : Convert.ToHexStringLower(SHA256.HashData(listBytes));
        if (listBytes != null)
            impactInput["masterList"] = new JsonObject { ["status"] = "listed", ["catalogCommit"] = catalogCommit, ["sha256"] = listSha256 };

        change?.Invoke(impactInput);

        var impactBytes = Encoding.UTF8.GetBytes(EcmaJson.Stringify(impactInput) + "\n");
        var impactDigest = Convert.ToHexStringLower(SHA256.HashData(impactBytes));

        var manifest = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["divisionCommit"] = catalogCommit,
            ["catalogSha256"] = new string('b', 64),
            ["sources"] = new JsonArray(
                new JsonObject { ["id"] = AlphaId, ["commit"] = alphaCommit, ["graphSchemaVersion"] = 1, ["graphStatus"] = "available" },
                new JsonObject { ["id"] = BetaId, ["commit"] = betaCommit, ["graphSchemaVersion"] = 1, ["graphStatus"] = "available" }),
            ["registryCommit"] = registryCommit,
            ["files"] = new JsonArray(
                new JsonObject { ["role"] = "impactInput", ["sourceId"] = "division-core", ["path"] = "impact-input.json", ["sha256"] = impactDigest, ["bytes"] = impactBytes.Length },
                new JsonObject { ["role"] = "graphEvidence", ["sourceId"] = AlphaId, ["path"] = "src/app.ts", ["sha256"] = appSha256, ["bytes"] = appBytes.Length },
                new JsonObject { ["role"] = "inspectedSource", ["sourceId"] = AlphaId, ["path"] = "src/app.ts", ["sha256"] = appSha256, ["bytes"] = appBytes.Length })
        };
        if (listBytes != null)
            manifest["files"]!.AsArray().Add(new JsonObject
            {
                ["role"] = "relationshipMasterList", ["sourceId"] = "division-core", ["path"] = "relationships/master-list.json",
                ["sha256"] = listSha256, ["bytes"] = listBytes.Length
            });

        var manifestBytes = Encoding.UTF8.GetBytes(EcmaJson.Stringify(manifest) + "\n");
        var manifestId = Convert.ToHexStringLower(SHA256.HashData(manifestBytes));
        byte[]? Blob(string sha256) =>
            sha256 == appSha256 ? appBytes : sha256 == impactDigest ? impactBytes : sha256 == listSha256 ? listBytes : null;
        return ImpactModel.Build(ImpactBundle.FromObjects(manifestId, manifestBytes, Blob));
    }
}
