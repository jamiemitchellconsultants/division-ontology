using System.Text.Json.Nodes;
using RuntimeKnowledge.Impact.Json;
using RuntimeKnowledge.Impact.Model;
using ImpactModel = RuntimeKnowledge.Impact.Model.ImpactModel;

namespace RuntimeKnowledge.Impact.Engine;

/// <summary>Freezes a validated assessment record with everything it cites, so it renders the same later.</summary>
public static class Freezing
{
    public static JsonObject Freeze(JsonObject record, ImpactModel model, JsonArray paths, IReadOnlyList<JsonObject> views)
    {
        var frozen = new JsonObject
        {
            ["format"] = 1,
            ["record"] = record.DeepClone(),
            ["pins"] = Pins(record),
            ["leads"] = new JsonObject
            {
                ["confirmed"] = new JsonArray(record["confirmedLeads"]!.AsArray()
                    .Select(k => (JsonNode)ConfirmedLead(model, k!.AsObject())).ToArray()),
                ["rejected"] = new JsonArray(record["rejectedLeads"]!.AsArray()
                    .Select(x => (JsonNode)RejectedLead(model, x!.AsObject())).ToArray())
            },
            ["paths"] = new JsonArray(CitedPaths(record, paths).Select(p => p.DeepClone()).ToArray())
        };

        var evidence = FrozenEvidence(record, model, paths);
        frozen["evidence"] = new JsonArray(evidence.Select(e => (JsonNode)e.Frozen).ToArray());

        var snapshotGaps = Coverage.Gaps(model);
        var snapshotKeys = snapshotGaps.Select(g => EcmaJson.Stringify(g)).ToHashSet(StringComparer.Ordinal);
        var evidenceGaps = new SortedDictionary<string, JsonObject>(StringComparer.Ordinal);
        foreach (var gap in evidence.Select(e => e.Gap).OfType<JsonObject>().Concat(LeadGaps(record, model)))
            evidenceGaps.TryAdd(EcmaJson.Stringify(gap), gap);

        frozen["gaps"] = new JsonObject
        {
            ["snapshot"] = new JsonArray(snapshotGaps.Select(g => (JsonNode)g.DeepClone()).ToArray()),
            ["traces"] = new JsonArray(TraceGaps(record, model)
                .Where(g => !snapshotKeys.Contains(EcmaJson.Stringify(g)))
                .Select(g => (JsonNode)g.DeepClone()).ToArray()),
            ["evidence"] = new JsonArray(evidenceGaps.Values.Select(g => (JsonNode)g.DeepClone()).ToArray()),
            ["unresolved"] = record["unresolved"]!.DeepClone()
        };

        frozen["digest"] = EcmaJson.CanonicalDigest(frozen);
        return frozen;
    }

    private static JsonObject Pins(JsonObject record)
    {
        if (record["schemaVersion"]!.GetValue<int>() == 2) return record["pins"]!.DeepClone().AsObject();
        return new JsonObject
        {
            ["snapshotId"] = record["snapshotId"]!.DeepClone(),
            ["masterList"] = new JsonObject { ["status"] = "none" },
            ["registry"] = new JsonObject
            {
                ["commit"] = record["registryCommit"]!.DeepClone(),
                ["digests"] = record["registryDigests"]!.DeepClone()
            }
        };
    }

    private static JsonObject ConfirmedLead(ImpactModel model, JsonObject key)
    {
        var source = model.Source(key["sourceId"]!.GetValue<string>());
        var node = source?.Node(key["nodeIri"]!.GetValue<string>());
        return new JsonObject
        {
            ["key"] = key.DeepClone(),
            ["label"] = node?["label"]?.DeepClone(),
            ["sourceCommit"] = source?.Commit
        };
    }

    private static JsonObject RejectedLead(ImpactModel model, JsonObject lead)
    {
        var key = lead["key"]!.AsObject();
        var node = model.Source(key["sourceId"]!.GetValue<string>())?.Node(key["nodeIri"]!.GetValue<string>());
        return new JsonObject
        {
            ["key"] = key.DeepClone(),
            ["label"] = node?["label"]?.DeepClone(),
            ["reason"] = lead["reason"]!.DeepClone()
        };
    }

    private static List<JsonObject> CitedPaths(JsonObject record, JsonArray paths)
    {
        var byId = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        foreach (var p in paths) byId.TryAdd(p!["id"]!.GetValue<string>(), p.AsObject());
        var cited = new List<JsonObject>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var impact in record["likelyImpacts"]!.AsArray())
            foreach (var id in impact!["pathIds"]!.AsArray())
            {
                var pathId = id!.GetValue<string>();
                if (seen.Add(pathId) && byId.TryGetValue(pathId, out var path)) cited.Add(path);
            }
        return cited;
    }

    private static List<(JsonObject Frozen, JsonObject? Gap)> FrozenEvidence(JsonObject record, ImpactModel model, JsonArray paths)
    {
        var byId = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
        foreach (var p in paths) byId.TryAdd(p!["id"]!.GetValue<string>(), p.AsObject());

        // An evidence IRI is only unique within its source, so each citation resolves in the source of the
        // cited step that carries it.
        var citations = new SortedSet<(string SourceId, string Iri)>(Comparer<(string SourceId, string Iri)>.Create((a, b) =>
        {
            var bySource = string.CompareOrdinal(a.SourceId, b.SourceId);
            return bySource != 0 ? bySource : string.CompareOrdinal(a.Iri, b.Iri);
        }));
        foreach (var impact in record["likelyImpacts"]!.AsArray())
        {
            var iris = impact!["evidenceIris"]!.AsArray().Select(e => e!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
            foreach (var id in impact["pathIds"]!.AsArray())
            {
                if (!byId.TryGetValue(id!.GetValue<string>(), out var path)) continue;
                foreach (var step in path["steps"]!.AsArray())
                {
                    var sourceId = step!["subject"]?["sourceId"]?.GetValue<string>();
                    if (sourceId == null) continue;
                    foreach (var iri in step["evidenceIris"]?.AsArray() ?? [])
                        if (iris.Contains(iri!.GetValue<string>())) citations.Add((sourceId, iri.GetValue<string>()));
                }
            }
        }

        var result = new List<(string SourceId, string Iri, JsonObject Frozen, JsonObject? Gap)>();
        foreach (var (sourceId, iri) in citations)
        {
            var source = model.Source(sourceId);
            if (source?.EvidenceNode(iri) == null) continue;
            var evidence = source.EvidenceNode(iri)!;
            var frozen = new JsonObject
            {
                ["evidenceIri"] = iri,
                ["sourceId"] = source.Id,
                ["commit"] = source.Commit,
                ["role"] = evidence["role"]?.DeepClone(),
                ["path"] = evidence["path"]!.DeepClone(),
                ["startLine"] = evidence["startLine"]!.DeepClone(),
                ["endLine"] = evidence["endLine"]!.DeepClone(),
                ["blobId"] = evidence["blobId"]?.DeepClone()
            };
            // B34: every cited excerpt is frozen, whatever its role. The renderer prints page lines only (B28, B38).
            var cited = EvidenceText.Read(model, source.Id, evidence);
            var gap = cited.Gap;
            if (cited.Lines != null)
                frozen["lines"] = new JsonArray(cited.Lines.Select(l => (JsonNode)JsonValue.Create(l)!).ToArray());
            if (cited.Page != null)
            {
                var page = new JsonObject();
                if (cited.Page.Title != null) page["title"] = cited.Page.Title;
                if (cited.Page.ConfluenceUrl != null) page["confluenceUrl"] = cited.Page.ConfluenceUrl;
                frozen["page"] = page;
            }
            result.Add((source.Id, iri, frozen, gap));
        }
        return result
            .OrderBy(e => e.SourceId, StringComparer.Ordinal)
            .ThenBy(e => e.Iri, StringComparer.Ordinal)
            .Select(e => (e.Frozen, e.Gap))
            .ToList();
    }

    // B30: the gaps of the traces that supplied a path a likely impact cites (ADR 0035), and the gaps every trace
    // carries, which are kept whether or not any trace contributes.
    private static IEnumerable<JsonObject> TraceGaps(JsonObject record, ImpactModel model)
    {
        var cited = (record["likelyImpacts"] as JsonArray ?? [])
            .SelectMany(i => i!["pathIds"] as JsonArray ?? []).Select(id => id!.GetValue<string>())
            .ToHashSet(StringComparer.Ordinal);
        var gaps = new List<JsonObject>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (starts, depth) in Assessments.Traces(record))
        {
            var trace = Paths.Read(model, starts, depth);
            if (!trace["paths"]!.AsArray().Any(p => cited.Contains(p!["id"]!.GetValue<string>()))) continue;
            foreach (var gap in trace["gaps"]!.AsArray().Select(g => g!.AsObject()))
                if (seen.Add(EcmaJson.Stringify(gap))) gaps.Add(gap);
        }
        foreach (var gap in Paths.ModelGaps(model))
            if (seen.Add(EcmaJson.Stringify(gap))) gaps.Add(gap);
        return gaps;
    }

    // B30: the gaps search reported about the record's confirmed and rejected leads.
    private static IEnumerable<JsonObject> LeadGaps(JsonObject record, ImpactModel model) =>
        (record["confirmedLeads"] as JsonArray ?? []).Select(k => k)
            .Concat((record["rejectedLeads"] as JsonArray ?? []).Select(r => r?["key"]))
            .OfType<JsonObject>()
            .SelectMany(k => Candidates.LeadGaps(model, k["sourceId"]!.GetValue<string>(), k["nodeIri"]!.GetValue<string>()));
}
