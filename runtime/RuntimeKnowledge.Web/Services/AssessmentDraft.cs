using System.Text.Json;
using System.Text.Json.Nodes;
using RuntimeKnowledge.Contracts;

namespace RuntimeKnowledge.Web.Services;

/// <summary>The assessment being drafted, kept in memory for the tab and never stored (ADR 0034). It is bound to
/// the snapshot it was started on, and a snapshot change discards it (B36).</summary>
public sealed class AssessmentDraft
{
    public string? SnapshotId { get; private set; }
    public JsonObject? Record { get; private set; }
    public IReadOnlyList<ImpactPath> Paths { get; private set; } = [];
    public IReadOnlyList<ImpactKey> Starts { get; private set; } = [];
    public Guid? EditingId { get; private set; }
    public int? ExpectedVersion { get; private set; }
    public event Action? Changed;

    public string RequestId => Record?["requestId"]?.GetValue<string>() ?? "";

    public void Start(string snapshotId, string requestId, string description, string assessor, JsonObject pins, Guid? startedFrom = null)
    {
        Discard(raise: false);
        SnapshotId = snapshotId;
        Record = new JsonObject
        {
            ["schemaVersion"] = 2,
            ["requestId"] = requestId,
            ["description"] = description,
            ["assessor"] = assessor,
            ["pins"] = pins.DeepClone(),
            ["confirmedLeads"] = new JsonArray(),
            ["rejectedLeads"] = new JsonArray(),
            ["likelyImpacts"] = new JsonArray(),
            ["unresolved"] = new JsonArray()
        };
        if (startedFrom is { } earlier) Record["startedFrom"] = new JsonObject { ["assessmentId"] = earlier.ToString("D") };
        Changed?.Invoke();
    }

    /// <summary>The pins a draft carries, built from a search result exactly as the engine writes them.</summary>
    public static JsonObject Pins(ImpactSearchResult search)
    {
        var masterList = new JsonObject { ["status"] = search.MasterList.Status };
        if (search.MasterList.CatalogCommit is { } commit) masterList["catalogCommit"] = commit;
        if (search.MasterList.Sha256 is { } sha256) masterList["sha256"] = sha256;
        return new JsonObject
        {
            ["snapshotId"] = JsonSerializer.SerializeToNode(search.Pinned, WorkspaceJson.Options),
            ["masterList"] = masterList,
            ["registry"] = search.Registry is { } registry
                ? new JsonObject
                {
                    ["commit"] = registry.Commit,
                    ["digests"] = new JsonObject { ["mappings"] = registry.Mappings, ["bindings"] = registry.Bindings }
                }
                : null
        };
    }

    public void LoadForEdit(SavedAssessment saved)
    {
        Discard(raise: false);
        SnapshotId = saved.SnapshotId;
        Record = saved.Frozen["record"]!.DeepClone().AsObject();
        Paths = saved.Frozen["paths"]?.Deserialize<IReadOnlyList<ImpactPath>>(WorkspaceJson.Options) ?? [];
        Starts = Keys(Record["confirmedLeads"]);
        EditingId = saved.Id;
        ExpectedVersion = saved.Version;
        Changed?.Invoke();
    }

    public void UsePaths(IReadOnlyList<ImpactPath> paths, IReadOnlyList<ImpactKey>? starts = null)
    {
        Paths = paths;
        if (starts is not null) Starts = starts;
        Changed?.Invoke();
    }

    public void Confirm(ImpactKey key)
    {
        var record = Require();
        RemoveKey(record["rejectedLeads"]!.AsArray(), key, nested: true);
        if (!Keys(record["confirmedLeads"]).Contains(key)) record["confirmedLeads"]!.AsArray().Add(KeyNode(key));
        Changed?.Invoke();
    }

    public void Reject(ImpactKey key, string reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new ArgumentException("A rejected lead needs a reason.", nameof(reason));
        var record = Require();
        RemoveKey(record["confirmedLeads"]!.AsArray(), key, nested: false);
        RemoveKey(record["rejectedLeads"]!.AsArray(), key, nested: true);
        record["rejectedLeads"]!.AsArray().Add(new JsonObject { ["key"] = KeyNode(key), ["reason"] = reason });
        Changed?.Invoke();
    }

    public void AddImpact(ImpactKey key, string reason, IReadOnlyList<string> pathIds, IReadOnlyList<string> evidenceIris)
    {
        Require()["likelyImpacts"]!.AsArray().Add(new JsonObject
        {
            ["key"] = KeyNode(key),
            ["reason"] = reason,
            ["pathIds"] = new JsonArray(pathIds.Select(id => (JsonNode)JsonValue.Create(id)!).ToArray()),
            ["evidenceIris"] = new JsonArray(evidenceIris.Select(iri => (JsonNode)JsonValue.Create(iri)!).ToArray())
        });
        Changed?.Invoke();
    }

    public void RemoveImpact(int index)
    {
        Require()["likelyImpacts"]!.AsArray().RemoveAt(index);
        Changed?.Invoke();
    }

    public void AddGap(string code, string reason)
    {
        Require()["unresolved"]!.AsArray().Add(new JsonObject { ["code"] = code, ["reason"] = reason });
        Changed?.Invoke();
    }

    public void Discard() => Discard(raise: true);

    private void Discard(bool raise)
    {
        SnapshotId = null;
        Record = null;
        Paths = [];
        Starts = [];
        EditingId = null;
        ExpectedVersion = null;
        if (raise) Changed?.Invoke();
    }

    private JsonObject Require() => Record ?? throw new InvalidOperationException("No assessment draft is in progress.");

    private static JsonObject KeyNode(ImpactKey key) => new() { ["sourceId"] = key.SourceId, ["nodeIri"] = key.NodeIri };

    private static List<ImpactKey> Keys(JsonNode? array) => (array as JsonArray ?? [])
        .Select(k => new ImpactKey(k!["sourceId"]!.GetValue<string>(), k["nodeIri"]!.GetValue<string>())).ToList();

    private static void RemoveKey(JsonArray array, ImpactKey key, bool nested)
    {
        foreach (var item in array.Where(item =>
        {
            var k = nested ? item!["key"] : item;
            return k?["sourceId"]?.GetValue<string>() == key.SourceId && k?["nodeIri"]?.GetValue<string>() == key.NodeIri;
        }).ToList())
            array.Remove(item);
    }
}
