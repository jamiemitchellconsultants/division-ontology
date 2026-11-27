using System.Security.Cryptography;
using System.Text.Json;
using Npgsql;
using RuntimeKnowledge.Contracts;
using RuntimeKnowledge.Store;
using RuntimeKnowledge.Worker;

namespace RuntimeKnowledge.Tests;

internal sealed class Scenario : IAsyncDisposable
{
    private readonly DatabaseFixture _fixture;
    private readonly NpgsqlDataSource _data;
    private readonly BundleImport _import;

    public static readonly PublishedKey KeyA = new("fixture/A", "node", "https://fixture.test/a#order");
    public static readonly PublishedKey KeyB = new("fixture/B", "node", "https://fixture.test/b#order");
    public static readonly PublishedKey KeyIsolated = new("division-core", "concept", "https://fixture.test/core#isolated");
    public static readonly PublishedKey KeyEdge = new("fixture/A", "assertion", "https://fixture.test/a#assertion");
    public static readonly PublishedKey KeyType = new("fixture/A", "relationshipType", "https://fixture.test/a#uses");

    public PublishedKey A => KeyA;
    public PublishedKey B => KeyB;
    public PublishedKey Isolated => KeyIsolated;
    public PublishedKey PublishedEdge => KeyEdge;
    public Actor Actor { get; } = new("https://issuer.test", "contributor");
    public MemoryBundles Bundles { get; } = new();
    public string ConnectionString => _fixture.ConnectionString;
    public Db Db { get; }
    public SnapshotStore Store { get; }
    public GraphQueries Graph { get; }

    private Scenario(DatabaseFixture fixture)
    {
        _fixture = fixture;
        _data = NpgsqlDataSource.Create(fixture.ConnectionString);
        Db = new Db(_data);
        Store = new SnapshotStore(Db, Bundles);
        Graph = new GraphQueries(Db);
        _import = new BundleImport(Store);
    }

    public static async Task<Scenario> OpenAsync() => new(await DatabaseFixture.OpenAsync());

    public async Task<string> ActivateAsync(string variant)
    {
        var id = Bundles.AddSnapshot(variant);
        await _import.StageAsync(id, CancellationToken.None);
        await _import.ActivateAsync(id, CancellationToken.None);
        return id;
    }

    public static PublishedKey TraversalKey(string name) => new("fixture/T", "node", $"https://fixture.test/t#{name}");
    public PublishedKey Hub => TraversalKey("hub");

    public async Task<string> ActivateTraversalAsync(string kind)
    {
        var id = Bundles.AddTraversalSnapshot(kind);
        await _import.StageAsync(id, CancellationToken.None);
        await _import.ActivateAsync(id, CancellationToken.None);
        return id;
    }

    public Task<SnapshotCandidate> StageCorruptBundleAsync() =>
        _import.StageAsync(Bundles.AddSnapshot("corrupt", corrupt: true), CancellationToken.None);

    public Task<InventoryPage> InventoryAsync(PageRequest? page = null) =>
        Graph.InventoryAsync(page ?? new PageRequest(), CancellationToken.None);

    public async Task<IReadOnlyList<TraversalStep>> TraverseAsync(PublishedKey start,
        bool includeUnavailable = false, string direction = "outgoing")
    {
        var result = await Graph.TraverseAsync(new TraversalRequest(start, direction,
            IncludeUnavailable: includeUnavailable), CancellationToken.None);
        return result.Paths.SelectMany(path => path.Steps).DistinctBy(step => (step.Key, step.RuntimeId)).ToArray();
    }

    public async ValueTask DisposeAsync()
    {
        await _data.DisposeAsync();
        await _fixture.DisposeAsync();
    }
}

internal sealed class MemoryBundles : IBundleObjects
{
    private readonly Dictionary<string, byte[]> _objects = new(StringComparer.Ordinal);

    public string AddSnapshot(string variant, bool corrupt = false, int publishedEdgeCount = 1)
    {
        var marker = variant switch { "initial" => '1', "changed" => '2', "missing" => '3', "returned" => '4', _ => '5' };
        var absent = variant == "missing";
        var divisionCommit = new string(marker, 40);
        var aCommit = new string('a', 40);
        var bCommit = new string(marker, 40);
        static Provenance Prov(PublishedKey key, string commit, string? basis = null, params Evidence[] evidence) =>
            new(key, commit, null, basis, evidence);

        var entities = new List<PublishedEntity>
        {
            new(Scenario.KeyA, "Order", "Customer order.", [], Prov(Scenario.KeyA, aCommit)),
            new(Scenario.KeyIsolated, "Independent term", null, [], Prov(Scenario.KeyIsolated, divisionCommit))
        };
        if (!absent) entities.Add(new PublishedEntity(Scenario.KeyB, "Order", "Warehouse order.", [],
            Prov(Scenario.KeyB, bCommit)));
        var types = new RelationshipType[] { new(Scenario.KeyType, "uses", null, null, null,
            Prov(Scenario.KeyType, aCommit)) };
        var edges = absent ? [] : Enumerable.Range(0, publishedEdgeCount).Select(index =>
        {
            var key = index == 0 ? Scenario.KeyEdge : Scenario.KeyEdge with { Identifier = $"{Scenario.KeyEdge.Identifier}-{index}" };
            return new PublishedEdge(key, Scenario.KeyA, Scenario.KeyB, "uses",
                Scenario.KeyType, Prov(key, aCommit, "direct",
                    new Evidence("docs/source.md", 1, 2, new string('e', 40), "implementation")));
        }).ToArray();
        var gaps = absent ? new[] { new CoverageGap("graph-unavailable", "fixture/B", null, "Fixture graph unavailable.") } : [];
        var files = new[]
        {
            AddFile("catalog", "dist/catalog.json", JsonSerializer.SerializeToUtf8Bytes(new { schemaVersion = 1 })),
            AddFile("nquads", "dist/combined.nq", []),
            AddFile("sourceInventory", "sources.json", JsonSerializer.SerializeToUtf8Bytes(new { sources = Array.Empty<string>() })),
            AddFile("projection", "projection.json", JsonSerializer.SerializeToUtf8Bytes(
                new PublishedProjection(entities, types, edges, gaps), WorkspaceJson.Options))
        };
        var manifest = new BundleManifest(1, divisionCommit, files[0].Sha256,
            [new("fixture/A", aCommit, 1, "available"),
             new("fixture/B", bCommit, absent ? null : 1, absent ? "unavailable" : "available")],
            null, files);
        var body = JsonSerializer.SerializeToUtf8Bytes(manifest, WorkspaceJson.Options);
        var id = Digest(body);
        _objects[$"manifests/{id}.json"] = body;
        if (corrupt) _objects[$"blobs/{files[^1].Sha256}"] = [1, 2, 3];
        return id;
    }

    public string AddTraversalSnapshot(string kind)
    {
        var commit = new string('a', 40);
        static Provenance Prov(PublishedKey key, string commit) => new(key, commit, null, null, []);
        var entities = new List<PublishedEntity>();
        var edges = new List<PublishedEdge>();
        void Entity(string name) => entities.Add(new PublishedEntity(Scenario.TraversalKey(name), name, null, [], Prov(Scenario.TraversalKey(name), commit)));
        var typeKey = new PublishedKey("fixture/T", "relationshipType", "https://fixture.test/t#uses");
        void Edge(string id, string from, string to)
        {
            var key = new PublishedKey("fixture/T", "assertion", $"https://fixture.test/t#{id}");
            var provenance = new Provenance(key, commit, null, "direct",
                [new Evidence("docs/source.md", 1, 2, new string('e', 40), "implementation")]);
            edges.Add(new PublishedEdge(key, Scenario.TraversalKey(from), Scenario.TraversalKey(to), "uses", typeKey, provenance));
        }
        Entity("hub");
        switch (kind)
        {
            case "busy":
                for (var i = 0; i < 150; i++) { Entity($"leaf{i:000}"); Edge($"e{i:000}", "hub", $"leaf{i:000}"); }
                break;
            case "trimmed":
                for (var i = 0; i < 99; i++) { Entity($"leaf{i:000}"); Edge($"e{i:000}", "hub", $"leaf{i:000}"); }
                break;
            case "exact":
                for (var i = 0; i < 100; i++) { Entity($"mid{i:000}"); Edge($"e{i:000}", "hub", $"mid{i:000}"); }
                break;
            case "branches":
                for (var m = 0; m < 2; m++)
                {
                    Entity($"mid{m}"); Edge($"h-m{m}", "hub", $"mid{m}");
                    for (var s = 0; s < 3; s++)
                    {
                        Entity($"sub{m}{s}"); Edge($"m{m}-s{s}", $"mid{m}", $"sub{m}{s}");
                        for (var l = 0; l < 30; l++) { Entity($"sub{m}{s}-leaf{l:00}"); Edge($"s{m}{s}-l{l:00}", $"sub{m}{s}", $"sub{m}{s}-leaf{l:00}"); }
                    }
                }
                break;
            case "wide":
                for (var m = 0; m < 40; m++)
                {
                    Entity($"mid{m:00}"); Edge($"h-m{m:00}", "hub", $"mid{m:00}");
                    for (var l = 0; l < 150; l++) { Entity($"mid{m:00}-leaf{l:000}"); Edge($"m{m:00}-l{l:000}", $"mid{m:00}", $"mid{m:00}-leaf{l:000}"); }
                }
                break;
            default: throw new ArgumentOutOfRangeException(nameof(kind), kind, null);
        }
        var files = new[]
        {
            AddFile("catalog", "dist/catalog.json", JsonSerializer.SerializeToUtf8Bytes(new { schemaVersion = 1 })),
            AddFile("nquads", "dist/combined.nq", []),
            AddFile("sourceInventory", "sources.json", JsonSerializer.SerializeToUtf8Bytes(new { sources = Array.Empty<string>() })),
            AddFile("projection", "projection.json", JsonSerializer.SerializeToUtf8Bytes(
                new PublishedProjection(entities,
                    [new RelationshipType(typeKey, "uses", null, null, null, Prov(typeKey, commit))], edges, []), WorkspaceJson.Options))
        };
        var manifest = new BundleManifest(1, new string('9', 40), files[0].Sha256,
            [new("fixture/T", commit, 1, "available")], null, files);
        var body = JsonSerializer.SerializeToUtf8Bytes(manifest, WorkspaceJson.Options);
        var id = Digest(body);
        _objects[$"manifests/{id}.json"] = body;
        return id;
    }

    /// <summary>Adds a corpus scenario's first impact bundle as a workspace bundle. Corpus bundles carry only the
    /// engine's roles, so minimal catalog, nquads, source inventory and projection objects are added, and the
    /// manifest is rewritten.</summary>
    /// <param name="inspectedSource">An extra inspectedSource file, (sourceId, path), which changes the manifest id
    /// without changing the impact input, as a republished but otherwise identical snapshot would.</param>
    public string AddCorpusBundle(string scenario, (string SourceId, string Path)? inspectedSource = null)
    {
        var steps = Impact.CorpusPaths.Steps(scenario);
        var corpusId = steps.First(s => s!["manifest"] is not null)!["manifest"]!.GetValue<string>();
        var corpus = JsonSerializer.Deserialize<BundleManifest>(
            File.ReadAllBytes(Path.Combine(Impact.CorpusPaths.Objects, "manifests", corpusId + ".json")), WorkspaceJson.Options)!;
        foreach (var file in corpus.Files)
            _objects[$"blobs/{file.Sha256}"] = File.ReadAllBytes(Path.Combine(Impact.CorpusPaths.Objects, "blobs", file.Sha256));
        var workspace = new[]
        {
            AddFile("catalog", "dist/catalog.json", JsonSerializer.SerializeToUtf8Bytes(new { schemaVersion = 1 })),
            AddFile("nquads", "dist/combined.nq", []),
            AddFile("sourceInventory", "sources.json", JsonSerializer.SerializeToUtf8Bytes(new { sources = Array.Empty<string>() })),
            AddFile("projection", "projection.json", JsonSerializer.SerializeToUtf8Bytes(
                new PublishedProjection([], [], [], []), WorkspaceJson.Options))
        };
        var extra = new List<BundleFile>();
        if (inspectedSource is { } inspected)
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes($"marker for {inspected.Path}\n");
            var digest = Digest(bytes);
            _objects[$"blobs/{digest}"] = bytes;
            extra.Add(new BundleFile("inspectedSource", inspected.SourceId, inspected.Path, digest, bytes.Length));
        }
        var manifest = corpus with { CatalogSha256 = workspace[0].Sha256, Files = [.. corpus.Files, .. workspace, .. extra] };
        var body = JsonSerializer.SerializeToUtf8Bytes(manifest, WorkspaceJson.Options);
        var id = Digest(body);
        _objects[$"manifests/{id}.json"] = body;
        return id;
    }

    /// <summary>Adds a prepared bundle: its manifest and every blob it lists. Returns the manifest id.</summary>
    public string Add(byte[] manifest, IReadOnlyDictionary<string, byte[]> blobs)
    {
        foreach (var (digest, bytes) in blobs) _objects[$"blobs/{digest}"] = bytes;
        var id = Digest(manifest);
        _objects[$"manifests/{id}.json"] = manifest;
        return id;
    }

    private BundleFile AddFile(string role, string path, byte[] bytes)
    {
        var digest = Digest(bytes);
        _objects[$"blobs/{digest}"] = bytes;
        return new BundleFile(role, "division-core", path, digest, bytes.Length);
    }

    public void RemoveManifest(string id) => _objects.Remove($"manifests/{id}.json");

    public Task<Stream> OpenAsync(string objectKey, CancellationToken cancellationToken) =>
        Task.FromResult<Stream>(_objects.TryGetValue(objectKey, out var value)
            ? new MemoryStream(value, writable: false) : throw new FileNotFoundException("Fixture object missing.", objectKey));

    public async Task PutImmutableAsync(string objectKey, Stream content, CancellationToken cancellationToken)
    {
        using var copy = new MemoryStream();
        await content.CopyToAsync(copy, cancellationToken);
        var value = copy.ToArray();
        if (_objects.TryGetValue(objectKey, out var existing) && !existing.AsSpan().SequenceEqual(value))
            throw new InvalidOperationException("Fixture object digest collision.");
        _objects[objectKey] = value;
    }

    private static string Digest(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
}
