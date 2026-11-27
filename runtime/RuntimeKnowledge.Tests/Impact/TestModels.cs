using System.Text;
using System.Text.Json.Nodes;
using RuntimeKnowledge.Impact;
using RuntimeKnowledge.Impact.Bundle;
using RuntimeKnowledge.Impact.Json;
using RuntimeKnowledge.Impact.Model;

namespace RuntimeKnowledge.Tests.Impact;

internal static class TestModels
{
    public static ImpactModel MakeModel(Action<JsonObject>? change = null)
    {
        var alphaCommit = new string('1', 40);
        var betaCommit = new string('2', 40);
        var catalogCommit = new string('3', 40);
        var registryCommit = new string('e', 40);
        var appContent = "export const api = 1;\n";
        var appBytes = Encoding.UTF8.GetBytes(appContent);
        var appSha256 = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(appBytes));

        var nodes = new JsonArray(
            new JsonObject { ["iri"] = "https://alpha.example/#system", ["kind"] = "https://ontology-kit.dev/schema#system", ["label"] = "Alpha API" },
            new JsonObject { ["iri"] = "https://alpha.example/#external", ["kind"] = "https://ontology-kit.dev/schema#system", ["label"] = "Beta" },
            new JsonObject { ["iri"] = "https://alpha.example/#container", ["kind"] = "https://ontology-kit.dev/schema#component", ["label"] = "Alpha service" },
            new JsonObject { ["iri"] = "https://alpha.example/#component", ["kind"] = "https://ontology-kit.dev/schema#component", ["label"] = "API handler" },
            new JsonObject { ["iri"] = "https://alpha.example/#code", ["kind"] = "https://ontology-kit.dev/schema#source-symbol", ["label"] = "handle" }
        );

        var assertions = new JsonArray(
            new JsonObject { ["iri"] = "https://alpha.example/#assertion", ["subject"] = "https://alpha.example/#system", ["object"] = "https://alpha.example/#external", ["relationship"] = "https://ontology-kit.dev/schema#communicates-with", ["basis"] = "inferred", ["evidenceIris"] = new JsonArray("https://alpha.example/#evidence") },
            new JsonObject { ["iri"] = "https://alpha.example/#contains1", ["subject"] = "https://alpha.example/#system", ["object"] = "https://alpha.example/#container", ["relationship"] = "https://ontology-kit.dev/schema#contains", ["basis"] = "direct", ["evidenceIris"] = new JsonArray("https://alpha.example/#evidence") },
            new JsonObject { ["iri"] = "https://alpha.example/#contains2", ["subject"] = "https://alpha.example/#container", ["object"] = "https://alpha.example/#component", ["relationship"] = "https://ontology-kit.dev/schema#contains", ["basis"] = "direct", ["evidenceIris"] = new JsonArray("https://alpha.example/#evidence") },
            new JsonObject { ["iri"] = "https://alpha.example/#contains3", ["subject"] = "https://alpha.example/#component", ["object"] = "https://alpha.example/#code", ["relationship"] = "https://ontology-kit.dev/schema#contains", ["basis"] = "direct", ["evidenceIris"] = new JsonArray("https://alpha.example/#evidence") }
        );

        var evidence = new JsonArray(
            new JsonObject { ["iri"] = "https://alpha.example/#evidence", ["path"] = "src/app.ts", ["startLine"] = 1, ["endLine"] = 1, ["blobId"] = "1".Repeat(40), ["role"] = "implementation" }
        );

        var coverage = new JsonArray(
            new JsonObject { ["iri"] = "https://alpha.example/#coverage", ["selector"] = "src/**", ["status"] = "covered", ["priority"] = 1 }
        );

        var mappings = new JsonArray(
            new JsonObject { ["sourceId"] = "fixture/Alpha", ["nodeIri"] = "https://alpha.example/#code", ["level"] = "code", ["role"] = "code", ["parent"] = new JsonObject { ["sourceId"] = "fixture/Alpha", ["nodeIri"] = "https://alpha.example/#component" }, ["supportAssertionIri"] = "https://alpha.example/#contains3", ["supportEvidenceIri"] = "https://alpha.example/#evidence" },
            new JsonObject { ["sourceId"] = "fixture/Alpha", ["nodeIri"] = "https://alpha.example/#component", ["level"] = "component", ["role"] = "component", ["parent"] = new JsonObject { ["sourceId"] = "fixture/Alpha", ["nodeIri"] = "https://alpha.example/#container" }, ["supportAssertionIri"] = "https://alpha.example/#contains2", ["supportEvidenceIri"] = "https://alpha.example/#evidence" },
            new JsonObject { ["sourceId"] = "fixture/Alpha", ["nodeIri"] = "https://alpha.example/#container", ["level"] = "container", ["role"] = "container", ["parent"] = new JsonObject { ["sourceId"] = "fixture/Alpha", ["nodeIri"] = "https://alpha.example/#system" }, ["supportAssertionIri"] = "https://alpha.example/#contains1", ["supportEvidenceIri"] = "https://alpha.example/#evidence" },
            new JsonObject { ["sourceId"] = "fixture/Alpha", ["nodeIri"] = "https://alpha.example/#system", ["level"] = "context", ["role"] = "software-system", ["supportAssertionIri"] = "https://alpha.example/#assertion", ["supportEvidenceIri"] = "https://alpha.example/#evidence" },
            new JsonObject { ["sourceId"] = "fixture/Alpha", ["nodeIri"] = "https://alpha.example/#external", ["level"] = "context", ["role"] = "software-system", ["supportAssertionIri"] = "https://alpha.example/#assertion", ["supportEvidenceIri"] = "https://alpha.example/#evidence" }
        );

        var bindings = new JsonArray(
            new JsonObject {
                ["fromSourceId"] = "fixture/Alpha", ["externalNodeIri"] = "https://alpha.example/#external",
                ["toSourceId"] = "fixture/Beta", ["targetNodeIri"] = "https://beta.example/#system",
                ["sourceAssertionIri"] = "https://alpha.example/#assertion", ["sourceEvidenceIri"] = "https://alpha.example/#evidence",
                ["identityEvidence"] = new JsonObject {
                    ["sourceId"] = "fixture/Alpha", ["path"] = "src/app.ts", ["startLine"] = 1, ["endLine"] = 1,
                    ["blobId"] = "1".Repeat(40), ["reason"] = "reviewed"
                }
            }
        );

        var impactInput = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["snapshotId"] = new JsonObject { ["catalogCommit"] = catalogCommit, ["catalogSha256"] = "0".Repeat(64),
                ["sources"] = new JsonArray(
                    new JsonObject { ["id"] = "fixture/Alpha", ["commit"] = alphaCommit },
                    new JsonObject { ["id"] = "fixture/Beta", ["commit"] = betaCommit }
                )
            },
            ["sources"] = new JsonArray(
                new JsonObject {
                    ["id"] = "fixture/Alpha", ["commit"] = alphaCommit,
                    ["evidenceSelectors"] = new JsonObject { ["implementation"] = new JsonArray("src/**") },
                    ["graph"] = new JsonObject {
                        ["status"] = "available", ["schemaVersion"] = 1,
                        ["nodes"] = nodes, ["assertions"] = assertions,
                        ["evidence"] = evidence, ["coverage"] = coverage
                    }
                },
                new JsonObject {
                    ["id"] = "fixture/Beta", ["commit"] = betaCommit,
                    ["evidenceSelectors"] = new JsonObject { ["implementation"] = new JsonArray("src/**") },
                    ["graph"] = new JsonObject {
                        ["status"] = "available", ["schemaVersion"] = 1,
                        ["nodes"] = nodes.DeepClone()!.AsArray(), ["assertions"] = assertions.DeepClone()!.AsArray(),
                        ["evidence"] = evidence.DeepClone()!.AsArray(), ["coverage"] = coverage.DeepClone()!.AsArray()
                    }
                }
            ),
            ["catalog"] = new JsonObject { ["concepts"] = new JsonArray(new JsonObject { ["iri"] = "https://alpha.example/concept", ["source"] = "fixture/Alpha", ["label"] = "Catalog concept", ["definition"] = "Only in catalog" }) },
            ["reviewedLinks"] = new JsonArray(),
            ["registry"] = new JsonObject {
                ["commit"] = registryCommit,
                ["mappingSha256"] = "0".Repeat(64),
                ["bindingSha256"] = "0".Repeat(64),
                ["mappings"] = mappings,
                ["bindings"] = bindings,
                ["gaps"] = new JsonArray()
            }
        };

        change?.Invoke(impactInput);

        var impactBytes = Encoding.UTF8.GetBytes(EcmaJson.Stringify(impactInput) + "\n");
        var impactDigest = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(impactBytes));

        var sourceAlpha = new JsonObject { ["id"] = "fixture/Alpha", ["commit"] = alphaCommit, ["graphSchemaVersion"] = 1, ["graphStatus"] = "available" };
        var sourceBeta = new JsonObject { ["id"] = "fixture/Beta", ["commit"] = betaCommit, ["graphSchemaVersion"] = 1, ["graphStatus"] = "available" };

        var manifest = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["divisionCommit"] = catalogCommit,
            ["catalogSha256"] = "0".Repeat(64),
            ["sources"] = new JsonArray(sourceAlpha, sourceBeta),
            ["registryCommit"] = registryCommit,
            ["files"] = new JsonArray(
                new JsonObject { ["role"] = "impactInput", ["sourceId"] = "division-core", ["path"] = "impact-input.json", ["sha256"] = impactDigest, ["bytes"] = impactBytes.Length },
                new JsonObject { ["role"] = "graphEvidence", ["sourceId"] = "fixture/Alpha", ["path"] = "src/app.ts", ["sha256"] = appSha256, ["bytes"] = appBytes.Length },
                new JsonObject { ["role"] = "graphEvidence", ["sourceId"] = "fixture/Beta", ["path"] = "src/app.ts", ["sha256"] = appSha256, ["bytes"] = appBytes.Length },
                new JsonObject { ["role"] = "inspectedSource", ["sourceId"] = "fixture/Alpha", ["path"] = "src/app.ts", ["sha256"] = appSha256, ["bytes"] = appBytes.Length }
            )
        };

        var manifestBytes = Encoding.UTF8.GetBytes(EcmaJson.Stringify(manifest) + "\n");
        var manifestId = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(manifestBytes));

        byte[]? Blob(string sha256) => sha256 == appSha256 ? appBytes : sha256 == impactDigest ? impactBytes : null;

        var bundle = ImpactBundle.FromObjects(manifestId, manifestBytes, Blob);
        return ImpactModel.Build(bundle);
    }

    private static string Repeat(this string s, int count) => string.Join(s, Enumerable.Repeat(s, count));
}
