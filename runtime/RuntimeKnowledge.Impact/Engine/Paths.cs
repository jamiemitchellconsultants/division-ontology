using System.Text.Json.Nodes;
using RuntimeKnowledge.Impact;
using RuntimeKnowledge.Impact.Json;
using ImpactModel = RuntimeKnowledge.Impact.Model.ImpactModel;

namespace RuntimeKnowledge.Impact.Engine;

/// <summary>Port of readImpactPaths from the Node C4 pilot.</summary>
public static class Paths
{
    public static JsonObject Read(ImpactModel model, IReadOnlyList<(string SourceId, string NodeIri)> starts, int maxDepth = 4)
    {
        if (!starts.Any())
            throw new ImpactException("at least one start is required");
        if (maxDepth < 1 || maxDepth > 6)
            throw new ImpactException("depth must be from 1 to 6");

        var paths = new List<JsonObject>();
        var gaps = new List<JsonObject>();

        // Add registry gaps first
        foreach (var g in model.Registry.Gaps)
            gaps.Add(g.DeepClone()!.AsObject());

        // Coverage gaps
        foreach (var gap in Coverage.Gaps(model))
            gaps.Add(gap);

        string Key((string SourceId, string NodeIri) pair) => EcmaJson.Stringify(new JsonObject
        {
            ["sourceId"] = pair.SourceId,
            ["nodeIri"] = pair.NodeIri
        });

        // Sort starts by key
        var sortedStarts = starts.OrderBy(s => Key(s)).ToList();

        foreach (var start in sortedStarts)
        {
            // Check for supported mapping
            if (model.Registry.MappingFor(start.SourceId, start.NodeIri) == null)
            {
                gaps.Add(new JsonObject
                {
                    ["code"] = "unsupported-start",
                    ["sourceId"] = start.SourceId,
                    ["nodeIri"] = start.NodeIri,
                    ["reason"] = "Start lacks a supported mapping"
                });
                continue;
            }

            var visited = new HashSet<string> { Key(start) };
            Walk(start, Array.Empty<JsonObject>(), visited, model, gaps, paths, Key, maxDepth);
        }

        if (!paths.Any())
            gaps.Add(new JsonObject
            {
                ["code"] = "unknown-paths",
                ["reason"] = "No paths found; missing relationships do not prove no impact"
            });

        return new JsonObject
        {
            ["snapshotId"] = model.SnapshotId.DeepClone()!.AsObject(),
            ["registryCommit"] = model.Registry.Commit,
            ["coverage"] = new JsonArray(Coverage.Inventory(model).Select(r => r.DeepClone()!).ToArray()),
            ["paths"] = new JsonArray(paths.Select(p => p.DeepClone()!).ToArray()),
            ["gaps"] = new JsonArray(gaps.Select(g => g.DeepClone()!).ToArray())
        };
    }

    private static void Walk(
        (string SourceId, string NodeIri) current,
        IReadOnlyList<JsonObject> steps,
        HashSet<string> visited,
        ImpactModel model,
        List<JsonObject> gaps,
        List<JsonObject> paths,
        Func<(string SourceId, string NodeIri), string> key,
        int maxDepth)
    {
        if (steps.Count >= maxDepth) return;

        var source = model.Source(current.SourceId);
        if (source == null || source.Status != "available")
        {
            gaps.Add(new JsonObject
            {
                ["code"] = "graph-unavailable",
                ["sourceId"] = current.SourceId,
                ["reason"] = "Graph unavailable"
            });
            return;
        }

        var edges = new List<(JsonObject Target, JsonObject Step)>();

        // Assertion edges (outgoing only)
        foreach (var assertion in source.Assertions)
        {
            var subject = assertion["subject"]!.GetValue<string>();
            if (subject != current.NodeIri) continue;

            var target = new JsonObject
            {
                ["sourceId"] = current.SourceId,
                ["nodeIri"] = assertion["object"]!.GetValue<string>()
            };

            var evidenceNodes = new JsonArray();
            if (assertion["evidenceIris"] is JsonArray eiris)
            {
                foreach (var eiri in eiris)
                {
                    var eiriStr = eiri!.GetValue<string>();
                    var enode = source.EvidenceNode(eiriStr);
                    if (enode != null) evidenceNodes.Add(enode.DeepClone()!);
                }
            }

            edges.Add((target, new JsonObject
            {
                ["type"] = "assertion",
                ["subject"] = new JsonObject
                {
                    ["sourceId"] = current.SourceId,
                    ["nodeIri"] = current.NodeIri
                },
                ["object"] = target,
                ["direction"] = "outgoing",
                ["assertionIri"] = assertion["iri"]!.GetValue<string>(),
                ["relationship"] = assertion["relationship"]!.GetValue<string>(),
                ["basis"] = assertion["basis"]!.GetValue<string>(),
                ["evidenceIris"] = new JsonArray(assertion["evidenceIris"]!.AsArray().Select(e => e!.DeepClone()!).ToArray()),
                ["evidence"] = evidenceNodes,
                ["commit"] = source.Commit
            }));
        }

        // Binding edges
        var binding = model.Registry.BindingFrom(current.SourceId, current.NodeIri);
        if (binding != null)
        {
            var target = new JsonObject
            {
                ["sourceId"] = binding["toSourceId"]!.GetValue<string>(),
                ["nodeIri"] = binding["targetNodeIri"]!.GetValue<string>()
            };

            edges.Add((target, new JsonObject
            {
                ["type"] = "binding",
                ["from"] = new JsonObject
                {
                    ["sourceId"] = current.SourceId,
                    ["nodeIri"] = current.NodeIri
                },
                ["to"] = target,
                ["registryCommit"] = model.Registry.Commit,
                ["identityEvidence"] = binding["identityEvidence"]!.DeepClone()!.AsObject()
            }));
        }

        // Sort edges: by target key, then assertionIri
        edges.Sort((a, b) =>
        {
            var cmp = string.CompareOrdinal(key((a.Target["sourceId"]!.GetValue<string>(), a.Target["nodeIri"]!.GetValue<string>())),
                key((b.Target["sourceId"]!.GetValue<string>(), b.Target["nodeIri"]!.GetValue<string>())));
            if (cmp != 0) return cmp;
            var aIri = a.Step["assertionIri"]?.GetValue<string>() ?? "";
            var bIri = b.Step["assertionIri"]?.GetValue<string>() ?? "";
            return string.CompareOrdinal(aIri, bIri);
        });

        foreach (var edge in edges)
        {
            var target = edge.Target;
            var step = edge.Step;
            var targetKey = key((target["sourceId"]!.GetValue<string>(), target["nodeIri"]!.GetValue<string>()));
            if (visited.Contains(targetKey)) continue;

            var next = new List<JsonObject>(steps) { step };
            var id = EcmaJson.Sha256Hex(EcmaJson.Stringify(new JsonArray(next.Select(s => s.DeepClone()!).ToArray())));
            paths.Add(new JsonObject
            {
                ["id"] = id,
                ["steps"] = new JsonArray(next.Select(s => s.DeepClone()!).ToArray())
            });

            var nextVisited = new HashSet<string>(visited) { targetKey };
            Walk((target["sourceId"]!.GetValue<string>(), target["nodeIri"]!.GetValue<string>()),
                next, nextVisited, model, gaps, paths, key, maxDepth);
        }
    }
}
