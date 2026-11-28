using System.Text.Json.Nodes;
using RuntimeKnowledge.Impact;
using RuntimeKnowledge.Impact.Json;
using RuntimeKnowledge.Impact.Model;
using ImpactModel = RuntimeKnowledge.Impact.Model.ImpactModel;

namespace RuntimeKnowledge.Impact.Engine;

/// <summary>Port of readImpactPaths from the Node C4 pilot, extended with the relationship master list (B6–B10).</summary>
public static class Paths
{
    private sealed class Walk
    {
        public required ImpactModel Model { get; init; }
        public required int MaxDepth { get; init; }
        public List<JsonObject> Gaps { get; } = [];
        public List<JsonObject> Paths { get; } = [];
        public SortedDictionary<string, JsonObject> EvidenceGaps { get; } = new(StringComparer.Ordinal);
        // B7: distinct assertion IRIs per relationship whose impact is none.
        public SortedDictionary<string, HashSet<string>> NotFollowed { get; } = new(StringComparer.Ordinal);
        // B8: per unlisted relationship, its distinct assertion IRIs and the nodes they were met at.
        public SortedDictionary<string, (HashSet<string> Assertions, SortedDictionary<string, (string SourceId, string NodeIri)> Nodes)> Unlisted { get; } =
            new(StringComparer.Ordinal);
        // B13, U19: per (source, term) without a reviewed link, the nodes visited that denote it.
        public Dictionary<(string SourceId, string Term), SortedDictionary<string, (string SourceId, string NodeIri)>> Unlinked { get; } = new();
        // B12: one gap per (source, term, shared term) that is not climbed.
        public HashSet<(string SourceId, string Term, string Shared)> Boundaries { get; } = new();
    }

    private static string Key((string SourceId, string NodeIri) pair) => EcmaJson.Stringify(new JsonObject
    {
        ["sourceId"] = pair.SourceId,
        ["nodeIri"] = pair.NodeIri
    });

    /// <summary>The gaps every trace carries whatever its starts: registry gaps and, without a list, B9's gap.</summary>
    public static IEnumerable<JsonObject> ModelGaps(ImpactModel model)
    {
        foreach (var g in model.Registry.Gaps)
            yield return g.DeepClone()!.AsObject();
        if (model.MasterList == null)
            yield return new JsonObject
            {
                ["code"] = "no-relationship-master-list",
                ["reason"] = "Impact direction is assumed from subject to object; no relationship master list is defined"
            };
    }

    public static JsonObject Read(ImpactModel model, IReadOnlyList<(string SourceId, string NodeIri)> starts, int maxDepth = 4)
    {
        if (!starts.Any())
            throw new ImpactException("at least one start is required");
        if (maxDepth < 1 || maxDepth > 6)
            throw new ImpactException("depth must be from 1 to 6");

        var walk = new Walk { Model = model, MaxDepth = maxDepth };
        var gaps = walk.Gaps;

        gaps.AddRange(ModelGaps(model));

        var evidenceGapsAt = gaps.Count;

        foreach (var gap in Coverage.Gaps(model))
            gaps.Add(gap);

        foreach (var start in starts.OrderBy(Key, StringComparer.Ordinal))
        {
            if (!model.IsSupported(start.SourceId, start.NodeIri))
            {
                gaps.Add(new JsonObject
                {
                    ["code"] = "unsupported-start",
                    ["sourceId"] = start.SourceId,
                    ["nodeIri"] = start.NodeIri,
                    ["reason"] = "Start lacks verified evidence or a supported mapping"
                });
                continue;
            }

            Step(walk, start, [], [Key(start)]);
        }

        gaps.InsertRange(evidenceGapsAt, walk.EvidenceGaps.Values.Concat(UnlistedGaps(walk)).Concat(TermGaps(walk)));

        if (!walk.Paths.Any())
            gaps.Add(new JsonObject
            {
                ["code"] = "unknown-paths",
                ["reason"] = "No paths found; missing relationships do not prove no impact"
            });

        var result = new JsonObject
        {
            ["snapshotId"] = model.SnapshotId.DeepClone()!.AsObject(),
            ["registryCommit"] = model.Registry.Commit
        };
        if (model.MasterList != null)
        {
            result["masterList"] = model.MasterList.Identity.DeepClone();
            result["notFollowed"] = new JsonArray(walk.NotFollowed.Select(n => (JsonNode)new JsonObject
            {
                ["relationship"] = n.Key,
                ["assertions"] = n.Value.Count
            }).ToArray());
        }
        result["coverage"] = new JsonArray(Coverage.Inventory(model).Select(r => r.DeepClone()!).ToArray());
        result["paths"] = new JsonArray(walk.Paths.Select(p => p.DeepClone()!).ToArray());
        result["gaps"] = new JsonArray(gaps.Select(g => g.DeepClone()!).ToArray());
        return result;
    }

    private static IEnumerable<JsonObject> UnlistedGaps(Walk walk) => walk.Unlisted.Select(u => new JsonObject
    {
        ["code"] = "unlisted-relationship",
        ["relationship"] = u.Key,
        ["nodes"] = new JsonArray(u.Value.Nodes.Values.Select(n => (JsonNode)new JsonObject
        {
            ["sourceId"] = n.SourceId,
            ["nodeIri"] = n.NodeIri
        }).ToArray()),
        ["assertions"] = u.Value.Assertions.Count,
        ["reason"] = "The impact of this relationship is unknown: it is not in the relationship master list"
    });

    private static IEnumerable<JsonObject> TermGaps(Walk walk)
    {
        static IEnumerable<JsonObject> Sorted(IEnumerable<JsonObject> gaps) =>
            gaps.OrderBy(g => EcmaJson.Stringify(g), StringComparer.Ordinal);
        var unlinked = walk.Unlinked.Select(u => new JsonObject
        {
            ["code"] = "unlinked-term",
            ["sourceId"] = u.Key.SourceId,
            ["term"] = u.Key.Term,
            ["nodes"] = new JsonArray(u.Value.Values.Select(n => (JsonNode)new JsonObject
            {
                ["sourceId"] = n.SourceId,
                ["nodeIri"] = n.NodeIri
            }).ToArray()),
            ["reason"] = "Term has no reviewed link, so other sources are not reached from it"
        });
        var boundaries = walk.Boundaries.Select(b => new JsonObject
        {
            ["code"] = "specialization-boundary",
            ["sourceId"] = b.SourceId,
            ["term"] = b.Term,
            ["sharedTerm"] = b.Shared,
            ["reason"] = "Impact is not followed from a specialised term to its general term"
        });
        return Sorted(unlinked).Concat(Sorted(boundaries));
    }

    // B11–B13: a visited node crosses to other sources only through an exact reviewed link of the term it denotes.
    // Labels are never compared.
    private static List<(JsonObject Target, JsonObject Step)> Crossings(Walk walk, (string SourceId, string NodeIri) current)
    {
        var model = walk.Model;
        var edges = new List<(JsonObject, JsonObject)>();
        var term = model.TermOf(current.SourceId, current.NodeIri);
        if (term == null) return edges;

        var links = model.LinksFrom(current.SourceId, term);
        if (links.Count == 0)
        {
            if (!walk.Unlinked.TryGetValue((current.SourceId, term), out var nodes))
                walk.Unlinked[(current.SourceId, term)] = nodes = new SortedDictionary<string, (string, string)>(StringComparer.Ordinal);
            nodes.TryAdd(Key(current), current);
            return edges;
        }

        foreach (var fromLink in links)
        {
            var shared = fromLink["shared"]!.GetValue<string>();
            if (fromLink["relation"]!.GetValue<string>() != "exact")
            {
                walk.Boundaries.Add((current.SourceId, term, shared));
                continue;
            }
            foreach (var other in model.Sources.Where(s => s.Id != current.SourceId))
                foreach (var toLink in model.LinksTo(other.Id, shared))
                    foreach (var nodeIri in model.NodesDenoting(other.Id, toLink["local"]!.GetValue<string>()))
                    {
                        var target = new JsonObject { ["sourceId"] = other.Id, ["nodeIri"] = nodeIri };
                        edges.Add((target, new JsonObject
                        {
                            ["type"] = "reviewedLinkCrossing",
                            ["from"] = new JsonObject { ["sourceId"] = current.SourceId, ["nodeIri"] = current.NodeIri },
                            ["to"] = target.DeepClone(),
                            ["sharedTerm"] = shared,
                            ["links"] = new JsonArray(fromLink.DeepClone(), toLink.DeepClone()),
                            ["catalogCommit"] = model.SnapshotId["catalogCommit"]!.DeepClone()
                        }));
                    }
        }
        return edges;
    }

    private static void Step(Walk walk, (string SourceId, string NodeIri) current, IReadOnlyList<JsonObject> steps, HashSet<string> visited)
    {
        // Term gaps are recorded for every visited node, including one the depth limit stops at.
        var crossings = Crossings(walk, current);
        // B7 and B8 apply to every node reached, so the depth limit stops steps, not the skipped-relationship counts.
        var atLimit = steps.Count >= walk.MaxDepth;
        var model = walk.Model;

        var source = model.Source(current.SourceId);
        if (atLimit && (source == null || source.Status != "available" || model.MasterList == null)) return;
        if (source == null || source.Status != "available")
        {
            walk.Gaps.Add(new JsonObject
            {
                ["code"] = "graph-unavailable",
                ["sourceId"] = current.SourceId,
                ["reason"] = "Graph unavailable"
            });
            return;
        }

        var edges = new List<(JsonObject Target, JsonObject Step)>();

        foreach (var assertion in source.AssertionsTouching(current.NodeIri))
        {
            var subject = assertion["subject"]!.GetValue<string>();
            var obj = assertion["object"]!.GetValue<string>();
            if (model.MasterList == null)
            {
                if (subject == current.NodeIri) edges.Add(AssertionEdge(walk, source, assertion, obj, "outgoing"));
                continue;
            }

            if (subject != current.NodeIri && obj != current.NodeIri) continue;
            var relationship = assertion["relationship"]!.GetValue<string>();
            // Assertion IRIs are unique only within a source.
            var iri = source.Id + "\u0000" + assertion["iri"]!.GetValue<string>();
            switch (model.MasterList.Impact(relationship))
            {
                case null:
                    if (!walk.Unlisted.TryGetValue(relationship, out var unlisted))
                        walk.Unlisted[relationship] = unlisted = ([], new SortedDictionary<string, (string, string)>(StringComparer.Ordinal));
                    unlisted.Assertions.Add(iri);
                    unlisted.Nodes.TryAdd(Key(current), current);
                    break;
                case "none":
                    if (!walk.NotFollowed.TryGetValue(relationship, out var counted))
                        walk.NotFollowed[relationship] = counted = new HashSet<string>(StringComparer.Ordinal);
                    counted.Add(iri);
                    break;
                case var _ when atLimit:
                    break;
                case var impact:
                    if (subject == current.NodeIri && impact is "subject-to-object" or "both")
                        edges.Add(AssertionEdge(walk, source, assertion, obj, "subject-to-object"));
                    if (obj == current.NodeIri && impact is "object-to-subject" or "both")
                        edges.Add(AssertionEdge(walk, source, assertion, subject, "object-to-subject"));
                    break;
            }
        }

        if (atLimit) return;

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
                ["from"] = new JsonObject { ["sourceId"] = current.SourceId, ["nodeIri"] = current.NodeIri },
                ["to"] = target,
                ["registryCommit"] = model.Registry.Commit,
                ["identityEvidence"] = binding["identityEvidence"]!.DeepClone()!.AsObject()
            }));
        }

        edges.AddRange(crossings);

        static string Text(JsonObject step, string name) => step[name]?.GetValue<string>() ?? "";
        edges.StableSort((a, b) =>
        {
            var cmp = string.CompareOrdinal(TargetKey(a.Target), TargetKey(b.Target));
            if (cmp == 0) cmp = string.CompareOrdinal(Text(a.Step, "assertionIri"), Text(b.Step, "assertionIri"));
            if (cmp == 0) cmp = string.CompareOrdinal(Text(a.Step, "type"), Text(b.Step, "type"));
            if (cmp == 0) cmp = string.CompareOrdinal(Text(a.Step, "direction"), Text(b.Step, "direction"));
            return cmp;
        });

        foreach (var (target, step) in edges)
        {
            var targetKey = TargetKey(target);
            if (visited.Contains(targetKey)) continue;

            var next = new List<JsonObject>(steps) { step };
            walk.Paths.Add(new JsonObject
            {
                ["id"] = EcmaJson.Sha256Hex(EcmaJson.Stringify(new JsonArray(next.Select(s => s.DeepClone()!).ToArray()))),
                ["steps"] = new JsonArray(next.Select(s => s.DeepClone()!).ToArray())
            });

            Step(walk, (target["sourceId"]!.GetValue<string>(), target["nodeIri"]!.GetValue<string>()),
                next, new HashSet<string>(visited) { targetKey });
        }
    }

    private static string TargetKey(JsonObject target) =>
        Key((target["sourceId"]!.GetValue<string>(), target["nodeIri"]!.GetValue<string>()));

    // subject and object are always the assertion's own; only the direction says which way the step goes (B6).
    private static (JsonObject Target, JsonObject Step) AssertionEdge(Walk walk, SourceModel source, JsonObject assertion,
        string targetIri, string direction)
    {
        var evidenceNodes = new JsonArray();
        if (assertion["evidenceIris"] is JsonArray iris)
            foreach (var iri in iris)
            {
                var evidence = source.EvidenceNode(iri!.GetValue<string>());
                if (evidence == null) continue;
                // Only page-role evidence is read, so code and ordinary-document steps keep their path ids.
                if (evidence["role"]?.GetValue<string>() == EvidenceText.PageRole)
                {
                    var cited = EvidenceText.Read(walk.Model, source.Id, evidence);
                    if (cited.Page != null) evidence["page"] = EvidenceText.StepPage(cited);
                    if (cited.Gap != null) walk.EvidenceGaps.TryAdd(EcmaJson.Stringify(cited.Gap), cited.Gap);
                }
                evidenceNodes.Add(evidence);
            }

        var target = new JsonObject { ["sourceId"] = source.Id, ["nodeIri"] = targetIri };
        return (target, new JsonObject
        {
            ["type"] = "assertion",
            ["subject"] = new JsonObject { ["sourceId"] = source.Id, ["nodeIri"] = assertion["subject"]!.GetValue<string>() },
            ["object"] = new JsonObject { ["sourceId"] = source.Id, ["nodeIri"] = assertion["object"]!.GetValue<string>() },
            ["direction"] = direction,
            ["assertionIri"] = assertion["iri"]!.GetValue<string>(),
            ["relationship"] = assertion["relationship"]!.GetValue<string>(),
            ["basis"] = assertion["basis"]!.GetValue<string>(),
            ["evidenceIris"] = new JsonArray(assertion["evidenceIris"]!.AsArray().Select(e => e!.DeepClone()!).ToArray()),
            ["evidence"] = evidenceNodes,
            ["commit"] = source.Commit
        });
    }
}
