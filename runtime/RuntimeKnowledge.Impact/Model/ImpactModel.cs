using System.Text.Json.Nodes;
using RuntimeKnowledge.Impact;
using RuntimeKnowledge.Impact.Bundle;
using RuntimeKnowledge.Impact.Json;

namespace RuntimeKnowledge.Impact.Model;

public sealed class ImpactModel
{
    public JsonObject SnapshotId { get; }
    public IReadOnlyList<SourceModel> Sources { get; }
    public IReadOnlyList<JsonObject> Concepts { get; }
    public IReadOnlyList<JsonObject> ReviewedLinks { get; }
    public RegistryModel Registry { get; }
    public ImpactBundle Bundle { get; }
    public MasterList? MasterList { get; }

    private readonly Dictionary<string, SourceModel> _byId = new(StringComparer.Ordinal);

    private ImpactModel(JsonObject snapshotId, IReadOnlyList<SourceModel> sources,
        IReadOnlyList<JsonObject> concepts, IReadOnlyList<JsonObject> reviewedLinks,
        RegistryModel registry, ImpactBundle bundle, MasterList? masterList)
    {
        SnapshotId = snapshotId;
        Sources = sources;
        Concepts = concepts;
        ReviewedLinks = reviewedLinks;
        Registry = registry;
        Bundle = bundle;
        MasterList = masterList;
        foreach (var s in sources) _byId[s.Id] = s;
    }

    public SourceModel? Source(string id) => _byId.TryGetValue(id, out var s) ? s : null;

    // B1, B2: a reviewed mapping wins; otherwise evidence cited by the node or its assertions supports it.
    public (bool Supported, string Reason) Support(string sourceId, string nodeIri)
    {
        if (Registry.MappingFor(sourceId, nodeIri) != null) return (true, "Reviewed mapping");
        var roles = Source(sourceId)?.CitingRoles(nodeIri) ?? [];
        return roles.Count > 0
            ? (true, "Verified evidence: " + string.Join(", ", roles))
            : (false, "No verified evidence or supported mapping");
    }

    public bool IsSupported(string sourceId, string nodeIri) => Support(sourceId, nodeIri).Supported;

    /// <summary>The term a node denotes, or null (B13: such a node takes part in no crossing).</summary>
    public string? TermOf(string sourceId, string nodeIri) => Source(sourceId)?.Node(nodeIri)?["denotes"]?.GetValue<string>();

    /// <summary>The reviewed links from a source's local term. A term that is itself a division-core concept
    /// has a synthetic exact link to itself (U3).</summary>
    public IReadOnlyList<JsonObject> LinksFrom(string sourceId, string term)
    {
        var links = ReviewedLinks
            .Where(l => Str(l, "source") == sourceId && Str(l, "local") == term)
            .Select(l => Link(sourceId, term, Str(l, "shared")!, Str(l, "relation")!))
            .ToList();
        if (IsCoreTerm(term)) links.Add(Link(sourceId, term, term, "exact"));
        return links;
    }

    /// <summary>The links in a source whose shared term is the one given, by either relation, plus the
    /// synthetic exact link of the shared term itself.</summary>
    public IReadOnlyList<JsonObject> LinksTo(string sourceId, string sharedTerm)
    {
        var links = ReviewedLinks
            .Where(l => Str(l, "source") == sourceId && Str(l, "shared") == sharedTerm)
            .Select(l => Link(sourceId, Str(l, "local")!, sharedTerm, Str(l, "relation")!))
            .ToList();
        if (IsCoreTerm(sharedTerm)) links.Add(Link(sourceId, sharedTerm, sharedTerm, "exact"));
        return links;
    }

    public IReadOnlyList<string> NodesDenoting(string sourceId, string term) =>
        Source(sourceId)?.Nodes.Where(n => n["denotes"]?.GetValue<string>() == term).Select(n => n["iri"]!.GetValue<string>()).ToList() ?? [];

    private bool IsCoreTerm(string iri) => Concepts.Any(c => Str(c, "source") == "division-core" && Str(c, "iri") == iri);

    private static string? Str(JsonObject o, string key) => o[key]?.GetValue<string>();

    private static JsonObject Link(string source, string local, string shared, string relation) =>
        new() { ["source"] = source, ["local"] = local, ["shared"] = shared, ["relation"] = relation };

    public static ImpactModel Build(ImpactBundle bundle)
    {
        var inputBytes = bundle.Read("impactInput", "division-core", "impact-input.json");
        if (inputBytes == null)
            throw new ImpactException("impact input file not found in bundle");
        bundle.VerifyBlobs();

        var input = JsonNode.Parse(inputBytes)!.AsObject();
        var schemaVersion = input["schemaVersion"];
        if (schemaVersion is not JsonValue sv || sv.GetValue<int>() != 1)
            throw new ImpactException("unsupported impact input schema version");

        var snapshotId = input["snapshotId"]!.DeepClone()!.AsObject();
        var bundleSourcesArr = input["sources"]!.AsArray();
        var sources = new List<SourceModel>();
        foreach (var srcNode in bundleSourcesArr)
        {
            var src = srcNode!.AsObject();
            var id = src["id"]!.GetValue<string>();
            var commit = src["commit"]!.GetValue<string>();
            var graph = src["graph"]!.AsObject();
            var status = graph["status"]!.GetValue<string>();
            var reason = graph["reason"]?.GetValue<string>();
            var schemaVer = graph["schemaVersion"] as JsonValue;
            var schemaVersionVal = schemaVer?.GetValue<int?>();
            var evidenceSelectors = src["evidenceSelectors"] as JsonObject;

            var nodes = new List<JsonObject>();
            foreach (var n in graph["nodes"]!.AsArray()) nodes.Add(n!.DeepClone()!.AsObject());
            var nodeMap = new Dictionary<string, JsonObject>(nodes.Count, StringComparer.Ordinal);
            foreach (var n in nodes) nodeMap[n["iri"]!.GetValue<string>()] = n;

            var assertions = new List<JsonObject>();
            foreach (var a in graph["assertions"]!.AsArray()) assertions.Add(a!.DeepClone()!.AsObject());

            var evidence = new List<JsonObject>();
            foreach (var e in graph["evidence"]!.AsArray()) evidence.Add(e!.DeepClone()!.AsObject());
            var evidenceMap = new Dictionary<string, JsonObject>(evidence.Count, StringComparer.Ordinal);
            foreach (var e in evidence) evidenceMap[e["iri"]!.GetValue<string>()] = e;

            var coverage = new List<JsonObject>();
            foreach (var c in graph["coverage"]!.AsArray()) coverage.Add(c!.DeepClone()!.AsObject());

            sources.Add(new SourceModel(id, commit, evidenceSelectors, status, reason, schemaVersionVal,
                nodes, assertions, evidence, coverage, nodeMap, evidenceMap));
        }

        var concepts = new List<JsonObject>();
        if (input["catalog"]?.AsObject()["concepts"] is JsonArray ca)
            foreach (var c in ca) concepts.Add(c!.DeepClone()!.AsObject());

        var reviewedLinks = new List<JsonObject>();
        if (input["reviewedLinks"] is JsonArray ra)
            foreach (var r in ra) reviewedLinks.Add(r!.DeepClone()!.AsObject());

        var registryNode = input["registry"];
        RegistryModel registry;
        if (registryNode == null)
        {
            registry = RegistryModel.Empty;
        }
        else
        {
            var ro = registryNode.AsObject();
            var mappings = new List<JsonObject>();
            if (ro["mappings"] is JsonArray ma)
                foreach (var m in ma) mappings.Add(m!.DeepClone()!.AsObject());
            var bindings = new List<JsonObject>();
            if (ro["bindings"] is JsonArray ba)
                foreach (var b in ba) bindings.Add(b!.DeepClone()!.AsObject());
            var gaps = new List<JsonObject>();
            if (ro["gaps"] is JsonArray ga)
                foreach (var g in ga) gaps.Add(g!.DeepClone()!.AsObject());
            registry = new RegistryModel(
                ro["commit"]!.GetValue<string>(),
                ro["mappingSha256"]!.GetValue<string>(),
                ro["bindingSha256"]!.GetValue<string>(),
                mappings, bindings, gaps);
        }

        return new ImpactModel(snapshotId, sources, concepts, reviewedLinks, registry, bundle, MasterList.Read(input, bundle));
    }
}

public sealed class SourceModel
{
    public string Id { get; }
    public string Commit { get; }
    public JsonObject? EvidenceSelectors { get; }
    public string Status { get; }
    public string? Reason { get; }
    public int? SchemaVersion { get; }
    public IReadOnlyList<JsonObject> Nodes { get; }
    public IReadOnlyList<JsonObject> Assertions { get; }
    public IReadOnlyList<JsonObject> Evidence { get; }
    public IReadOnlyList<JsonObject> Coverage { get; }
    private readonly Dictionary<string, JsonObject> _nodes;
    private readonly Dictionary<string, JsonObject> _evidence;

    public SourceModel(string id, string commit, JsonObject? evidenceSelectors,
        string status, string? reason, int? schemaVersion,
        IReadOnlyList<JsonObject> nodes, IReadOnlyList<JsonObject> assertions,
        IReadOnlyList<JsonObject> evidence, IReadOnlyList<JsonObject> coverage,
        Dictionary<string, JsonObject> nodeMap, Dictionary<string, JsonObject> evidenceMap)
    {
        Id = id; Commit = commit; EvidenceSelectors = evidenceSelectors;
        Status = status; Reason = reason; SchemaVersion = schemaVersion;
        Nodes = nodes; Assertions = assertions; Evidence = evidence; Coverage = coverage;
        _nodes = nodeMap; _evidence = evidenceMap;
    }

    public JsonObject? Node(string iri) => _nodes.TryGetValue(iri, out var n) ? (JsonObject?)n.DeepClone() : null;
    public JsonObject? EvidenceNode(string iri) => _evidence.TryGetValue(iri, out var e) ? (JsonObject?)e.DeepClone() : null;

    public JsonObject? Assertion(string iri)
    {
        foreach (var a in Assertions)
            if (a["iri"]!.GetValue<string>() == iri) return (JsonObject?)a.DeepClone();
        return null;
    }

    public IReadOnlyList<JsonObject> AssertionsFrom(string subjectIri)
    {
        var result = new List<JsonObject>();
        foreach (var a in Assertions)
            if (a["subject"]!.GetValue<string>() == subjectIri) result.Add((JsonObject)a.DeepClone()!);
        return result;
    }

    public IReadOnlyList<JsonObject> AssertionsTo(string objectIri)
    {
        var result = new List<JsonObject>();
        foreach (var a in Assertions)
            if (a["object"]!.GetValue<string>() == objectIri) result.Add((JsonObject)a.DeepClone()!);
        return result;
    }

    /// <summary>The evidence records that cite a node: its own evidenceIris (design records) and those of
    /// every assertion where it is subject or object. De-duplicated and sorted by evidence IRI.</summary>
    public IReadOnlyList<JsonObject> CitingEvidence(string nodeIri)
    {
        var cited = new SortedDictionary<string, JsonObject>(StringComparer.Ordinal);

        void Collect(JsonNode? evidenceIris)
        {
            if (evidenceIris is not JsonArray iris) return;
            foreach (var ei in iris)
            {
                var evIri = ei?.GetValue<string>();
                if (evIri != null && _evidence.TryGetValue(evIri, out var evNode)) cited.TryAdd(evIri, evNode);
            }
        }

        if (_nodes.TryGetValue(nodeIri, out var node)) Collect(node["evidenceIris"]);

        foreach (var a in AssertionsTouching(nodeIri))
            Collect(a["evidenceIris"]);

        return cited.Values.ToList();
    }

    private Dictionary<string, List<JsonObject>>? _touching;

    /// <summary>The assertions where a node is subject or object, in Map order. Built once, so lead support and
    /// path walks do not scan every assertion for every node (Q1).</summary>
    public IReadOnlyList<JsonObject> AssertionsTouching(string nodeIri)
    {
        var index = _touching;
        if (index is null)
        {
            index = new Dictionary<string, List<JsonObject>>(StringComparer.Ordinal);
            foreach (var a in Assertions)
            {
                var subject = a["subject"]!.GetValue<string>();
                var obj = a["object"]!.GetValue<string>();
                Append(subject, a);
                if (obj != subject) Append(obj, a);
            }
            _touching = index;
        }
        return index.TryGetValue(nodeIri, out var found) ? found : [];

        void Append(string key, JsonObject assertion)
        {
            if (!index.TryGetValue(key, out var list)) index[key] = list = [];
            list.Add(assertion);
        }
    }

    /// <summary>Returns the de-duplicated, sorted roles of the evidence that cites a node (B1).</summary>
    public IReadOnlyList<string> CitingRoles(string nodeIri) =>
        CitingEvidence(nodeIri)
            .Select(e => e["role"]?.GetValue<string>())
            .Where(r => !string.IsNullOrEmpty(r))
            .Select(r => r!)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(r => r, StringComparer.Ordinal)
            .ToList();
}

public sealed class RegistryModel
{
    public string Commit { get; }
    public string MappingSha256 { get; }
    public string BindingSha256 { get; }
    public IReadOnlyList<JsonObject> Mappings { get; }
    public IReadOnlyList<JsonObject> Bindings { get; }
    public IReadOnlyList<JsonObject> Gaps { get; }

    public RegistryModel(string commit, string mappingSha256, string bindingSha256,
        IReadOnlyList<JsonObject> mappings, IReadOnlyList<JsonObject> bindings,
        IReadOnlyList<JsonObject> gaps)
    {
        Commit = commit; MappingSha256 = mappingSha256; BindingSha256 = bindingSha256;
        Mappings = mappings; Bindings = bindings; Gaps = gaps;
    }

    public JsonObject? MappingFor(string sourceId, string nodeIri)
    {
        foreach (var m in Mappings)
            if (m["sourceId"]!.GetValue<string>() == sourceId && m["nodeIri"]!.GetValue<string>() == nodeIri)
                return (JsonObject?)m.DeepClone();
        return null;
    }

    public JsonObject? BindingFrom(string sourceId, string nodeIri)
    {
        foreach (var b in Bindings)
            if (b["fromSourceId"]!.GetValue<string>() == sourceId && b["externalNodeIri"]!.GetValue<string>() == nodeIri)
                return (JsonObject?)b.DeepClone();
        return null;
    }

    public static RegistryModel Empty => new("", "", "", Array.Empty<JsonObject>(), Array.Empty<JsonObject>(), Array.Empty<JsonObject>());
}
