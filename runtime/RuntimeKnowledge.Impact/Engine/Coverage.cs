using System.Text.Json.Nodes;
using RuntimeKnowledge.Impact.Json;

namespace RuntimeKnowledge.Impact.Engine;

/// <summary>Ports of coverageGaps and coverageInventory from the Node graph loader.</summary>
public static class Coverage
{
    public static List<JsonObject> Gaps(Impact.Model.ImpactModel model)
    {
        var gaps = new List<JsonObject>();
        foreach (var source in model.Sources)
        {
            if (source.Status != "available")
            {
                gaps.Add(new JsonObject
                {
                    ["code"] = "graph-unavailable",
                    ["sourceId"] = source.Id,
                    ["reason"] = source.Reason ?? "Graph unavailable"
                });
                continue;
            }
            foreach (var c in source.Coverage)
            {
                var status = c["status"]!.GetValue<string>();
                if (status != "covered")
                {
                    var gap = new JsonObject
                    {
                        ["code"] = "coverage-" + status,
                        ["sourceId"] = source.Id,
                        ["coverageIri"] = c["iri"]!.GetValue<string>(),
                        ["selector"] = c["selector"]!.GetValue<string>()
                    };
                    if (c["reason"] is JsonValue rv) gap["reason"] = rv.DeepClone();
                    gaps.Add(gap);
                }
            }
        }
        gaps.StableSort((a, b) => string.CompareOrdinal(EcmaJson.Stringify(a), EcmaJson.Stringify(b)));
        return gaps;
    }

    public static List<JsonObject> Inventory(Impact.Model.ImpactModel model)
    {
        var records = new List<JsonObject>();
        foreach (var source in model.Sources)
            foreach (var entry in source.Coverage)
            {
                var record = new JsonObject { ["sourceId"] = source.Id };
                foreach (var (key, value) in entry)
                    if (!key.Equals("sourceId", System.StringComparison.Ordinal))
                        record[key] = value!.DeepClone();
                records.Add(record);
            }
        records.StableSort((a, b) => string.CompareOrdinal(EcmaJson.Stringify(a), EcmaJson.Stringify(b)));
        return records;
    }
}
