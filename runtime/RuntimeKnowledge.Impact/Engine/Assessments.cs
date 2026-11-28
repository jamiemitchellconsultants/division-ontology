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
        ["masterList"] = model.MasterList?.Identity.DeepClone() ?? new JsonObject { ["status"] = "none" },
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

    /// <summary>B39: the reviewed-link crossing step of a path that arrives at the given node, if there is one.</summary>
    public static JsonObject? CrossingSupport(JsonObject path, JsonNode key) =>
        path["steps"]!.AsArray().Select(s => s!.AsObject()).FirstOrDefault(s =>
            s["type"]?.GetValue<string>() == "reviewedLinkCrossing" &&
            s["to"]?["sourceId"]?.GetValue<string>() == key["sourceId"]?.GetValue<string>() &&
            s["to"]?["nodeIri"]?.GetValue<string>() == key["nodeIri"]?.GetValue<string>());

    private const int MaxTraces = 20, MaxTraceStarts = 20;

    /// <summary>ADR 0035: the paths a record may cite, the union of the paths of every trace it lists, each once,
    /// in trace order. Without `traces`, its confirmed leads at depth 4, as the pilot traced them.</summary>
    public static JsonArray CitablePaths(ImpactModel model, JsonObject record)
    {
        var cited = new JsonArray();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (starts, depth) in Traces(record))
            foreach (var path in Paths.Read(model, starts, depth)["paths"]!.AsArray())
                if (seen.Add(path!["id"]!.GetValue<string>())) cited.Add(path.DeepClone());
        return cited;
    }

    private static List<(List<(string SourceId, string NodeIri)> Starts, int Depth)> Traces(JsonObject record)
    {
        static (string, string) Key(JsonNode? key) =>
            key?["sourceId"] is JsonValue s && s.TryGetValue<string>(out var source) && source.Length > 0 &&
            key["nodeIri"] is JsonValue n && n.TryGetValue<string>(out var iri) && iri.Length > 0
                ? (source, iri) : throw new ImpactException("invalid traces");

        if (record["traces"] is not { } listed)
        {
            var leads = (record["confirmedLeads"] as JsonArray ?? []).Select(Key).ToList();
            return leads.Count == 0 ? [] : [(leads, 4)];
        }
        if (listed is not JsonArray traces || traces.Count > MaxTraces) throw new ImpactException("invalid traces");
        return traces.Select(trace =>
        {
            if (trace?["starts"] is not JsonArray starts || starts.Count is 0 or > MaxTraceStarts ||
                trace["maxDepth"] is not JsonValue d || !d.TryGetValue<int>(out var depth) || depth is < 1 or > 6)
                throw new ImpactException("invalid traces");
            return (starts.Select(Key).ToList(), depth);
        }).ToList();
    }

    /// <summary>Validates the record and returns its assessment digest: for schema 2 the digest of its frozen form,
    /// and for schema 1 the pilot's digest of the canonical record, so existing schema 1 feedback still validates (B37).</summary>
    public static string Validate(JsonObject record, ImpactModel model, JsonArray paths, IReadOnlyList<JsonObject> views)
    {
        var frozen = Freeze(record, model, paths, views);
        return record["schemaVersion"] is JsonValue v && v.TryGetValue<int>(out var schema) && schema == 1
            ? EcmaJson.CanonicalDigest(record)
            : frozen["digest"]!.GetValue<string>();
    }

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

        if (schema == 2) Traces(record);
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

    // B31: no approval or sign-off field anywhere in the record, since the whole record is frozen and stored.
    private static bool ClaimsApproval(JsonNode? node) => node switch
    {
        JsonObject o => o.Any(p =>
        {
            var lower = p.Key.ToLowerInvariant();
            return p.Key is "approved" or "approvalStatus" || lower.Contains("signoff") || lower.Contains("sign-off") ||
                   ClaimsApproval(p.Value);
        }),
        JsonArray a => a.Any(ClaimsApproval),
        _ => false
    };

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
                if (!nodeExists(imp["key"]!.AsObject()))
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
                // B39: a crossing that arrives at the impact's node is support enough, with no evidence IRI.
                if (evidenceIris.Count == 0 && !selected.Any(p => CrossingSupport(p!, imp["key"]!.AsObject()) != null))
                    throw new ImpactException("invalid path impact");

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
