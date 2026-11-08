using System.Net;
using System.Net.Http.Json;
using RuntimeKnowledge.Contracts;
using RuntimeKnowledge.Store;
using RuntimeKnowledge.Worker;
using Xunit;

namespace RuntimeKnowledge.Tests;

internal sealed class ScenarioRunner : IAsyncDisposable
{
    private readonly Scenario _store;
    private readonly ApiFixture _api;
    private readonly HttpClient _reader;
    private readonly HttpClient _manager;
    private readonly IndexCommand _index;
    private readonly ReplayStore _replay;

    public PublishedKey A => _store.A;
    public PublishedKey B => _store.B;
    public PublishedKey Isolated => _store.Isolated;
    public PublishedKey PublishedEdge => _store.PublishedEdge;

    private ScenarioRunner(Scenario store)
    {
        _store = store;
        var embeddings = new DeterministicEmbeddings();
        _api = new ApiFixture(store, "Entra", embeddings);
        _reader = _api.ClientWithToken(["ReadKnowledgeGraph"]);
        _manager = _api.ClientWithToken(["ManageKnowledgeGraph"]);
        _index = new IndexCommand(new IndexRepository(store.Db), embeddings,
            new EmbeddingProfile("Bedrock", "amazon.titan-embed-text-v2:0", "fixture", 1024, "knowledge-v1"));
        _replay = new ReplayStore(store.Db, store.Bundles);
    }

    public static async Task<ScenarioRunner> OpenAsync() => new(await Scenario.OpenAsync());

    public async Task<string> ActivateAsync(string variant)
    {
        var id = _store.Bundles.AddSnapshot(variant);
        await _store.Store.StageAsync(id, CancellationToken.None);
        await IndexAsync();
        await _store.Store.ActivateAsync(id, CancellationToken.None);
        return id;
    }

    public async Task IndexAsync()
    {
        // This small fixture fits in a single batch.
        var batch = await _index.RunBatchAsync(32, CancellationToken.None);
        Assert.Equal(0, batch.Deferred);
        Assert.Equal(batch.Claimed, batch.Indexed);
    }

    public async Task<T> Read<T>(string path)
    {
        using var response = await _reader.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<T>(WorkspaceJson.Options))!;
    }

    public async Task<T> Query<T>(string path, object body) =>
        (await Send<T>(HttpMethod.Post, path, body, reader: true))!;

    public Task<TraversalResult> Traverse(PublishedKey key, bool includeUnavailable = false) =>
        Query<TraversalResult>("/api/traverse", new TraversalRequest(key, IncludeUnavailable: includeUnavailable));

    public async Task<T?> Send<T>(HttpMethod method, string path, object body, HttpStatusCode status = HttpStatusCode.OK,
        bool reader = false)
    {
        using var request = new HttpRequestMessage(method, path) { Content = JsonContent.Create(body, options: WorkspaceJson.Options) };
        using var response = await (reader ? _reader : _manager).SendAsync(request);
        Assert.Equal(status, response.StatusCode);
        return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<T>(WorkspaceJson.Options) : default;
    }

    public async Task<RebuildReport> ReplayAndSwitchAsync()
    {
        var token = CancellationToken.None;
        await _replay.SetWritesPausedAsync(true, token);
        try
        {
            var live = await _replay.DescribeCurrentAsync(token);
            var rebuilt = await _replay.RebuildAsync(live.ThroughPosition, token);
            Assert.NotEqual(live.Generation, rebuilt.Generation);
            Assert.Equal(live.SnapshotId, rebuilt.SnapshotId);
            Assert.Equal(live.StateDigest, rebuilt.StateDigest);
            await _replay.CutoverAsync(rebuilt.Generation, live.ThroughPosition, token);
            Assert.Equal(rebuilt, await _replay.DescribeCurrentAsync(token));
            return rebuilt;
        }
        finally
        {
            await _replay.SetWritesPausedAsync(false, token);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _reader.Dispose();
        _manager.Dispose();
        await _api.DisposeAsync();
    }

    private sealed class DeterministicEmbeddings : IEmbeddingProvider
    {
        public Task<float[]> EmbedAsync(EmbeddingProfile profile, string input, EmbeddingPurpose purpose,
            CancellationToken token)
        {
            var vector = new float[profile.Dimensions];
            vector[input.Contains("reconcile", StringComparison.OrdinalIgnoreCase) ? 0 : 1] = 1;
            return Task.FromResult(vector);
        }
    }
}
