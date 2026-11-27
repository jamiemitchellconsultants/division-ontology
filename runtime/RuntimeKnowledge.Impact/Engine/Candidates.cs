using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json.Nodes;
using RuntimeKnowledge.Impact;
using RuntimeKnowledge.Impact.Json;
using ImpactModel = RuntimeKnowledge.Impact.Model.ImpactModel;

namespace RuntimeKnowledge.Impact.Engine;

/// <summary>Port of findCandidates from the Node C4 pilot.</summary>
public static class Candidates
{
    /// <summary>Finds candidate nodes matching the query or exact IRI.
    /// Lower-casing uses ToLowerInvariant(), which differs from JavaScript only for U+0130.</summary>
    public static JsonObject Find(ImpactModel model, string query = "", string? exactIri = null)
    {
        if (exactIri == null && !query.Trim().Any(c => !char.IsWhiteSpace(c)))
            throw new ImpactException("query or exact IRI is required");

        var phrase = query.Trim().ToLowerInvariant();
        var tokens = Regex.Split(phrase, @"[^\p{L}\p{N}]+")
            .Where(t => t.Length > 0)
            .Distinct()
            .ToList();

        var leads = new List<JsonObject>();

        // Graph nodes
        foreach (var source in model.Sources)
        {
            foreach (var node in source.Nodes)
            {
                var nodeIri = node["iri"]!.GetValue<string>();
                var label = node["label"]!.GetValue<string>();
                var mapping = model.Registry.MappingFor(source.Id, nodeIri);
                var supported = mapping != null;
                Add(source.Id, nodeIri, label, "graph", supported, supported ? "Reviewed mapping" : "No supported mapping");
            }
        }

        // Catalog concepts
        foreach (var concept in model.Concepts)
        {
            var sourceId = concept["source"]!.GetValue<string>();
            if (!model.Sources.Any(s => s.Id == sourceId)) continue;
            var iri = concept["iri"]!.GetValue<string>();
            var label = concept["label"]!.GetValue<string>();
            var definition = concept["definition"]?.GetValue<string>();
            var text = definition != null ? label + " \u2014 " + definition : label;
            Add(sourceId, iri, text, "concept", false, "Catalog concept is not a supported graph node");
        }

        // Sort by rank, then sourceId, then nodeIri, then kind
        leads.Sort((a, b) =>
        {
            var rankA = a["_rank"]!.GetValue<int>();
            var rankB = b["_rank"]!.GetValue<int>();
            var cmp = rankA - rankB;
            if (cmp != 0) return cmp;
            cmp = string.CompareOrdinal(a["key"]!.AsObject()["sourceId"]!.GetValue<string>(),
                b["key"]!.AsObject()["sourceId"]!.GetValue<string>());
            if (cmp != 0) return cmp;
            cmp = string.CompareOrdinal(a["key"]!.AsObject()["nodeIri"]!.GetValue<string>(),
                b["key"]!.AsObject()["nodeIri"]!.GetValue<string>());
            if (cmp != 0) return cmp;
            return string.CompareOrdinal(a["kind"]!.GetValue<string>(), b["kind"]!.GetValue<string>());
        });

        // Remove _rank from output
        foreach (var lead in leads) lead.Remove("_rank");

        var gaps = new List<JsonObject>();
        foreach (var gap in model.Registry.Gaps) gaps.Add(gap.DeepClone()!.AsObject());
        gaps.AddRange(Coverage.Gaps(model));

        if (!leads.Any())
            gaps.Add(new JsonObject { ["code"] = "unknown-search", ["reason"] = "No matching lead; coverage does not prove no impact" });

        return new JsonObject
        {
            ["snapshotId"] = model.SnapshotId.DeepClone(),
            ["registryCommit"] = model.Registry.Commit,
            ["coverage"] = new JsonArray(Coverage.Inventory(model).Select(r => r.DeepClone()!).ToArray()),
            ["leads"] = new JsonArray(leads.Select(l => l.DeepClone()!).ToArray()),
            ["gaps"] = new JsonArray(gaps.Select(g => g.DeepClone()!).ToArray())
        };

        void Add(string sourceId, string nodeIri, string text, string kind, bool supported, string reason)
        {
            var lower = text.ToLowerInvariant();
            int rank;
            string matchKind;
            if (exactIri != null)
            {
                if (nodeIri != exactIri) return;
                rank = 0;
                matchKind = "exact-iri";
            }
            else if (lower.Contains(phrase))
            {
                rank = 1;
                matchKind = "phrase";
            }
            else
            {
                var terms = Regex.Split(lower, @"[^\p{L}\p{N}]+")
                    .Where(t => t.Length > 0)
                    .ToHashSet();
                if (!tokens.Any(t => terms.Contains(t))) return;
                rank = 2;
                matchKind = "token";
            }

            var source = model.Source(sourceId);
            leads.Add(new JsonObject
            {
                ["key"] = new JsonObject { ["sourceId"] = sourceId, ["nodeIri"] = nodeIri },
                ["matchedText"] = text,
                ["matchKind"] = matchKind,
                ["sourceCommit"] = source?.Commit,
                ["supported"] = supported,
                ["reason"] = reason,
                ["_rank"] = rank,
                ["kind"] = kind
            });
        }
    }
}
