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

        var evidence = FrozenEvidence(record, model);
        frozen["evidence"] = new JsonArray(evidence.Select(e => (JsonNode)e.Frozen).ToArray());

        var snapshotGaps = Coverage.Gaps(model);
        var snapshotKeys = snapshotGaps.Select(g => EcmaJson.Stringify(g)).ToHashSet(StringComparer.Ordinal);
        var evidenceGaps = new SortedDictionary<string, JsonObject>(StringComparer.Ordinal);
        foreach (var gap in evidence.Select(e => e.Gap).OfType<JsonObject>())
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

    private static List<(JsonObject Frozen, JsonObject? Gap)> FrozenEvidence(JsonObject record, ImpactModel model)
    {
        var iris = record["likelyImpacts"]!.AsArray()
            .SelectMany(i => i!["evidenceIris"]!.AsArray().Select(e => e!.GetValue<string>()))
            .Distinct(StringComparer.Ordinal);
        var result = new List<(string SourceId, string Iri, JsonObject Frozen, JsonObject? Gap)>();
        foreach (var iri in iris)
        {
            var source = model.Sources.FirstOrDefault(s => s.EvidenceNode(iri) != null);
            if (source == null) continue;
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
            JsonObject? gap = null;
            // As on path steps, only page-role evidence is read; code and ordinary documents carry no text.
            if (evidence["role"]?.GetValue<string>() == EvidenceText.PageRole)
            {
                var cited = EvidenceText.Read(model, source.Id, evidence);
                gap = cited.Gap;
                if (cited.Page != null)
                {
                    if (cited.Lines != null)
                        frozen["lines"] = new JsonArray(cited.Lines.Select(l => (JsonNode)JsonValue.Create(l)!).ToArray());
                    var page = new JsonObject();
                    if (cited.Page.Title != null) page["title"] = cited.Page.Title;
                    if (cited.Page.ConfluenceUrl != null) page["confluenceUrl"] = cited.Page.ConfluenceUrl;
                    frozen["page"] = page;
                }
            }
            result.Add((source.Id, iri, frozen, gap));
        }
        return result
            .OrderBy(e => e.SourceId, StringComparer.Ordinal)
            .ThenBy(e => e.Iri, StringComparer.Ordinal)
            .Select(e => (e.Frozen, e.Gap))
            .ToList();
    }

    private static IEnumerable<JsonObject> TraceGaps(JsonObject record, ImpactModel model)
    {
        var starts = record["confirmedLeads"]!.AsArray()
            .Select(k => (k!["sourceId"]!.GetValue<string>(), k["nodeIri"]!.GetValue<string>()))
            .ToList();
        if (starts.Count == 0) return [];
        return Paths.Read(model, starts)["gaps"]!.AsArray().Select(g => g!.AsObject());
    }
}
