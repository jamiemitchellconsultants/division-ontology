using System.Text.Json.Nodes;
using RuntimeKnowledge.Impact;
using RuntimeKnowledge.Impact.Engine;
using RuntimeKnowledge.Impact.Json;
using ImpactModel = RuntimeKnowledge.Impact.Model.ImpactModel;

namespace RuntimeKnowledge.Impact.Engine;

/// <summary>Port of newAssessment, validateAssessment and viewDigest from the Node C4 pilot.</summary>
public static class Assessments
{
    public static JsonObject New(ImpactModel model, string requestId, string description)
    {
        if (!Text(requestId) || !Text(description))
            throw new ImpactException("request ID and description are required");

        return new JsonObject
        {
            ["schemaVersion"] = 2,
            ["requestId"] = requestId,
            ["description"] = description,
            ["assessor"] = "",
            ["pins"] = Pins(model),
            ["confirmedLeads"] = new JsonArray(),
            ["rejectedLeads"] = new JsonArray(),
            ["likelyImpacts"] = new JsonArray(),
            ["unresolved"] = new JsonArray()
        };
    }

    /// <summary>The model's pins, as a schema 2 record carries them. The registry pin is null when the bundle has no registry.</summary>
    public static JsonObject Pins(ImpactModel model) => new()
    {
        ["snapshotId"] = model.SnapshotId.DeepClone(),
        ["masterList"] = new JsonObject { ["status"] = "none" },
        ["registry"] = model.Registry.Commit.Length == 0 ? null : new JsonObject
        {
            ["commit"] = model.Registry.Commit,
            ["digests"] = new JsonObject
            {
                ["mappings"] = model.Registry.MappingSha256,
                ["bindings"] = model.Registry.BindingSha256
            }
        }
    };

    /// <summary>Validates the record and returns the digest of its frozen form.</summary>
    public static string Validate(JsonObject record, ImpactModel model, JsonArray paths, IReadOnlyList<JsonObject> views) =>
        Freeze(record, model, paths, views)["digest"]!.GetValue<string>();

    /// <summary>Validates the record and freezes it with everything it cites.</summary>
    public static JsonObject Freeze(JsonObject record, ImpactModel model, JsonArray paths, IReadOnlyList<JsonObject> views)
    {
        if (!Text(record["requestId"]?.GetValue<string>()) || !Text(record["description"]?.GetValue<string>()))
            throw new ImpactException("invalid assessment");

        var schema = record["schemaVersion"] is JsonValue v && v.TryGetValue<int>(out var n) ? n : 0;
        switch (schema)
        {
            case 1: CheckSchema1Pins(record, model); break;
            case 2: CheckSchema2Pins(record, model); break;
            default: throw new ImpactException("invalid assessment");
        }

        CheckContent(record, model, paths, views);

        if (schema == 1 ? record.ContainsKey("approved") || record.ContainsKey("approvalStatus") : ClaimsApproval(record))
            throw new ImpactException("approval is not supported");

        return Freezing.Freeze(record, model, paths, views);
    }

    private static bool Same(JsonNode? a, JsonNode? b) =>
        EcmaJson.Stringify(EcmaJson.Canonical(a)) == EcmaJson.Stringify(EcmaJson.Canonical(b));

    private static void CheckSchema1Pins(JsonObject record, ImpactModel model)
    {
        if (!Same(record["snapshotId"], model.SnapshotId))
            throw new ImpactException("snapshot mismatch");

        if (record["registryCommit"]?.GetValue<string>() != model.Registry.Commit ||
            record["registryDigests"]?["mappings"]?.GetValue<string>() != model.Registry.MappingSha256 ||
            record["registryDigests"]?["bindings"]?.GetValue<string>() != model.Registry.BindingSha256)
            throw new ImpactException("registry mismatch");
    }

    private static void CheckSchema2Pins(JsonObject record, ImpactModel model)
    {
        var expected = Pins(model);
        var pins = record["pins"] as JsonObject;
        if (pins == null || !Same(pins["snapshotId"], expected["snapshotId"]))
            throw new ImpactException("snapshot mismatch");
        if (!Same(pins["masterList"], expected["masterList"]))
            throw new ImpactException("master list mismatch");
        if (!Same(pins["registry"], expected["registry"]))
            throw new ImpactException("registry mismatch");
    }

    // B31: no approval or sign-off field, at the top level or in any likely impact.
    private static bool ClaimsApproval(JsonObject record)
    {
        static bool Claims(JsonObject o) => o.Select(p => p.Key).Any(k =>
        {
            var lower = k.ToLowerInvariant();
            return k is "approved" or "approvalStatus" || lower.Contains("signoff") || lower.Contains("sign-off");
        });
        return Claims(record) ||
               (record["likelyImpacts"] as JsonArray ?? []).OfType<JsonObject>().Any(Claims);
    }

    private static void CheckContent(JsonObject record, ImpactModel model, JsonArray paths, IReadOnlyList<JsonObject> views)
    {

        foreach (var field in new[] { "confirmedLeads", "rejectedLeads", "likelyImpacts", "unresolved" })
        {
            if (record[field] is not JsonArray)
                throw new ImpactException($"invalid {field}");
        }

        var nodeExists = (JsonObject key) =>
        {
            var sourceId = key["sourceId"]?.GetValue<string>();
            var nodeIri = key["nodeIri"]?.GetValue<string>();
            if (string.IsNullOrEmpty(sourceId) || string.IsNullOrEmpty(nodeIri)) return false;
            var source = model.Source(sourceId);
            return source != null && source.Nodes.Any(n => n["iri"]!.GetValue<string>() == nodeIri);
        };

        foreach (var lead in record["confirmedLeads"]!.AsArray())
        {
            if (!nodeExists(lead!.AsObject()))
                throw new ImpactException("invalid confirmed lead");
        }

        foreach (var lead in record["rejectedLeads"]!.AsArray())
        {
            var key = lead!["key"]!.AsObject();
            if (!nodeExists(key) || !Text(lead["reason"]?.GetValue<string>()))
                throw new ImpactException("invalid rejected lead");
        }

        var pathMap = new Dictionary<string, JsonObject>();
        foreach (var p in paths) pathMap[p!["id"]!.GetValue<string>()] = p!.AsObject();

        var viewMap = new Dictionary<string, JsonObject>();
        foreach (var v in (views ?? Array.Empty<JsonObject>()))
            viewMap[ViewDigest(v)] = v;

        var same = (JsonObject a, JsonObject b) =>
            a["sourceId"]?.GetValue<string>() == b["sourceId"]?.GetValue<string>() &&
            a["nodeIri"]?.GetValue<string>() == b["nodeIri"]?.GetValue<string>();

        foreach (var impact in record["likelyImpacts"]!.AsArray())
        {
            var imp = impact!.AsObject();
            if (imp["key"] == null || !Text(imp["reason"]?.GetValue<string>()) ||
                imp["pathIds"] is not JsonArray || imp["evidenceIris"] is not JsonArray ||
                (imp["viewIds"] != null && imp["viewIds"] is not JsonArray))
                throw new ImpactException("invalid impact");

            var pathIds = imp["pathIds"]!.AsArray();
            var evidenceIris = imp["evidenceIris"]!.AsArray();

            if (pathIds.Count > 0)
            {
                if (!nodeExists(imp["key"]!.AsObject()) || evidenceIris.Count == 0)
                    throw new ImpactException("invalid path impact");

                var selected = new List<JsonObject?>();
                foreach (var id in pathIds)
                {
                    pathMap.TryGetValue(id!.GetValue<string>(), out var path);
                    selected.Add(path);
                }
                if (selected.Any(p => p == null))
                    throw new ImpactException("unresolved path");

                bool impactPresent = false;
                foreach (var p in selected.Where(p => p != null))
                {
                    foreach (var step in p!["steps"]!.AsArray())
                    {
                        var s = step!.AsObject();
                        foreach (var k in new[] { "subject", "object", "from", "to" })
                        {
                            if (s[k] != null && same(s[k].AsObject(), imp["key"]!.AsObject()))
                            {
                                impactPresent = true;
                                break;
                            }
                        }
                        if (impactPresent) break;
                    }
                    if (impactPresent) break;
                }

                if (!impactPresent)
                    throw new ImpactException("impact is not present in a cited path");

                var evidence = new HashSet<string>();
                foreach (var p in selected)
                    foreach (var step in p["steps"]!.AsArray())
                        foreach (var ei in step!.AsObject()["evidenceIris"]?.AsArray() ?? new JsonArray())
                            evidence.Add(ei!.GetValue<string>());

                foreach (var ei in evidenceIris)
                {
                    if (!evidence.Contains(ei!.GetValue<string>()))
                        throw new ImpactException("unresolved evidence");
                }
            }
            else
            {
                var viewIds = imp["viewIds"];
                if (viewIds is not JsonArray vidArray || vidArray.Count == 0 || evidenceIris.Count > 0)
                    throw new ImpactException("view evidence is required");

                var selectedViews = new List<JsonObject?>();
                foreach (var id in vidArray)
                {
                    viewMap.TryGetValue(id!.GetValue<string>(), out var view);
                    selectedViews.Add(view);
                }
                if (selectedViews.Any(v => v == null))
                    throw new ImpactException("unresolved view");

                bool verified = false;
                foreach (var view in selectedViews.Where(v => v != null))
                {
                    var inspected = view!["items"]!.AsArray()
                        .Select(n => n!.AsObject())
                        .FirstOrDefault(i => same(i["key"]!.AsObject(), imp["key"]!.AsObject()) &&
                                             i["basis"]?.GetValue<string>() == "architect-inspected");
                    if (inspected == null) continue;
                    var supplement = inspected["evidence"]!.AsArray()[0]!.DeepClone()!.AsObject();
                    var reconstructed = Views.Project(model,
                        (view["focus"]!.AsObject()["sourceId"]!.GetValue<string>(),
                         view["focus"]!.AsObject()["nodeIri"]!.GetValue<string>()),
                        view["level"]!.GetValue<string>(), supplement);
                    if (ViewDigest(reconstructed) == ViewDigest(view))
                        verified = true;
                }

                if (!verified)
                    throw new ImpactException("view evidence is not verified for impact");
            }
        }

        foreach (var gap in record["unresolved"]!.AsArray())
        {
            if (!Text(gap!["reason"]?.GetValue<string>()))
                throw new ImpactException("unresolved gap requires reason");
        }
    }

    private static bool Text(string? x) => x is { } s && s.Trim().Length > 0;

    private static string ViewDigest(JsonObject view) => EcmaJson.CanonicalDigest(view);
}
