using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json.Nodes;
using RuntimeKnowledge.Impact;
using RuntimeKnowledge.Impact.Json;
using RuntimeKnowledge.Impact.Model;
using ImpactModel = RuntimeKnowledge.Impact.Model.ImpactModel;

namespace RuntimeKnowledge.Impact.Engine;

/// <summary>Port of findCandidates from the Node C4 pilot.</summary>
public static class Candidates
{
    private static IEnumerable<CitedText> PageEvidence(ImpactModel model, SourceModel source, string nodeIri) =>
        source.CitingEvidence(nodeIri)
            .Where(e => e["role"]?.GetValue<string>() == EvidenceText.PageRole)
            .Select(e => EvidenceText.Read(model, source.Id, e));

    /// <summary>The gaps search reports about one graph lead: those of its page evidence (B30).</summary>
    public static IEnumerable<JsonObject> LeadGaps(ImpactModel model, string sourceId, string nodeIri)
    {
        var source = model.Source(sourceId);
        if (source == null || !model.IsSupported(sourceId, nodeIri)) return [];
        return PageEvidence(model, source, nodeIri).Select(c => c.Gap).OfType<JsonObject>();
    }

    /// <summary>Finds candidate nodes matching the query or exact IRI.
    /// Lower-casing is JavaScript's, through EcmaText.ToLower.</summary>
    public static JsonObject Find(ImpactModel model, string query = "", string? exactIri = null)
    {
        if (exactIri == null && !query.Trim().Any(c => !char.IsWhiteSpace(c)))
            throw new ImpactException("query or exact IRI is required");

        var phrase = EcmaText.ToLower(query.Trim());
        var tokens = Regex.Split(phrase, @"[^\p{L}\p{N}]+")
            .Where(t => t.Length > 0)
            .Distinct()
            .ToList();

        var leads = new List<JsonObject>();
        var evidenceGaps = new SortedDictionary<string, JsonObject>(StringComparer.Ordinal);

        // Graph nodes
        foreach (var source in model.Sources)
        {
            foreach (var node in source.Nodes)
            {
                var nodeIri = node["iri"]!.GetValue<string>();
                var label = node["label"]!.GetValue<string>();
                var (supported, reason) = model.Support(source.Id, nodeIri);
                Add(source.Id, nodeIri, label, "graph", supported, reason);
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
        leads.StableSort((a, b) =>
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
        gaps.AddRange(evidenceGaps.Values);

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
            var lower = EcmaText.ToLower(text);
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
            var lead = new JsonObject
            {
                ["key"] = new JsonObject { ["sourceId"] = sourceId, ["nodeIri"] = nodeIri },
                ["matchedText"] = text,
                ["matchKind"] = matchKind,
                ["sourceCommit"] = source?.Commit,
                ["supported"] = supported,
                ["reason"] = reason,
                ["_rank"] = rank,
                ["kind"] = kind
            };
            if (kind == "graph" && supported && source != null)
            {
                var pages = new JsonArray();
                foreach (var cited in PageEvidence(model, source, nodeIri))
                {
                    if (cited.Gap != null) evidenceGaps.TryAdd(EcmaJson.Stringify(cited.Gap), cited.Gap);
                    if (cited.Page != null) pages.Add(EvidenceText.LeadPage(cited));
                }
                if (pages.Count > 0) lead["pageEvidence"] = pages;
            }
            leads.Add(lead);
        }
    }
}
