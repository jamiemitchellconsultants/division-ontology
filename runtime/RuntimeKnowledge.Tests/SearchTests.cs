using RuntimeKnowledge.Contracts;
using RuntimeKnowledge.Store;
using RuntimeKnowledge.Worker;
using Xunit;

namespace RuntimeKnowledge.Tests;

public sealed class SearchTests
{
    private static readonly CancellationToken None = CancellationToken.None;
    private static readonly EmbeddingProfile Profile = new("fixture", "fixture-model", "v1", 3, "knowledge-v1");

    [Fact]
    public async Task SemanticSearchFollowsCurrentAnnotationVersionAndAvailability()
    {
        await using var s = await Scenario.OpenAsync();
        var embeddings = new FixtureEmbedding();
        var repository = new IndexRepository(s.Db);
        var worker = new IndexCommand(repository, embeddings, Profile);
        var search = new SearchStore(s.Db, embeddings, 0.8);
        var writes = new ContributionStore(s.Db);
        await StageAndActivateAsync(s, repository, worker);
        var note = await writes.ApplyAsync(s.Actor,
            new CreateAnnotation(s.A, "businessExplanation", "Reconciliation closes invoices."), None);
        Assert.True((await search.SearchAsync("reconcile unpaid invoices", 10, None)).IndexPending);
        await worker.RunBatchAsync(100, None);
        var result = await search.SearchAsync("reconcile unpaid invoices", 10, None);
        var hit = Assert.Single(result.Hits);
        Assert.Equal(note.Id, hit.AnnotationId);
        Assert.Equal(s.A, hit.EntityKey);
        Assert.Equal("Reconciliation closes invoices.", hit.MatchedText);
        Assert.False(result.IndexPending);

        var edited = await writes.ApplyAsync(s.Actor,
            new EditAnnotation(note.Id, note.Version, "businessExplanation", "Archive a delivery note."), None);
        Assert.Empty((await search.SearchAsync("reconcile unpaid invoices", 10, None)).Hits);
        await worker.RunBatchAsync(100, None);
        Assert.Equal(note.Id, Assert.Single((await search.SearchAsync("archive a delivery note", 10, None)).Hits).AnnotationId);
        await writes.ApplyAsync(s.Actor, new RemoveContribution(note.Id, edited.Version), None);
        Assert.Empty((await search.SearchAsync("archive a delivery note", 10, None)).Hits);
    }

    [Fact]
    public async Task InFlightOldJobCannotPublishAndOutageLeavesGraphReadable()
    {
        await using var s = await Scenario.OpenAsync();
        var embeddings = new FixtureEmbedding();
        var repo = new IndexRepository(s.Db);
        var worker = new IndexCommand(repo, embeddings, Profile);
        var writes = new ContributionStore(s.Db);
        await StageAndActivateAsync(s, repo, worker);
        var note = await writes.ApplyAsync(s.Actor,
            new CreateAnnotation(s.A, "businessExplanation", "Reconciliation closes invoices."), None);
        await repo.EnqueueAsync(None);
        var old = Assert.Single(await repo.ClaimAsync(10, None));
        await writes.ApplyAsync(s.Actor,
            new EditAnnotation(note.Id, note.Version, "businessExplanation", "Archive a delivery note."), None);
        Assert.False(await repo.CompleteAsync(old, [1, 0, 0], None));

        embeddings.Unavailable = true;
        var failure = await Assert.ThrowsAsync<WorkspaceException>(() =>
            new SearchStore(s.Db, embeddings, 0.8).SearchAsync("reconcile", 10, None));
        Assert.Equal("searchUnavailable", failure.Code);
        Assert.NotEmpty(await s.TraverseAsync(s.A));
        Assert.NotNull(await writes.FindAsync(note.Id, None));
    }

    [Fact]
    public async Task BaselineAndProfileActivateOnlyWhenTheIndexIsReady()
    {
        await using var s = await Scenario.OpenAsync();
        var embeddings = new FixtureEmbedding();
        var repo = new IndexRepository(s.Db);
        var worker = new IndexCommand(repo, embeddings, Profile);
        var baseline = await StageAndActivateAsync(s, repo, worker);
        var replacement = Profile with { Revision = "v2", Dimensions = 4 };
        await repo.SelectAsync(replacement, None);
        Assert.False(await repo.PromoteIfReadyAsync(None));
        Assert.Equal(Profile, await repo.ReadActiveProfileAsync(None));
        await new IndexCommand(repo, embeddings, replacement).RunBatchAsync(100, None);
        Assert.Equal(replacement, await repo.ReadActiveProfileAsync(None));
        Assert.Equal(baseline, (await s.InventoryAsync()).SnapshotId);
    }

    private static async Task<string> StageAndActivateAsync(Scenario s, IndexRepository repo, IndexCommand worker)
    {
        await repo.SelectAsync(Profile, None);
        var id = s.Bundles.AddSnapshot("initial");
        await s.Store.StageAsync(id, None);
        var blocked = await Assert.ThrowsAsync<WorkspaceException>(() => s.Store.ActivateAsync(id, None));
        Assert.Equal("snapshotIndexPending", blocked.Code);
        await worker.RunBatchAsync(100, None);
        await s.Store.ActivateAsync(id, None);
        return id;
    }

    private sealed class FixtureEmbedding : IEmbeddingProvider
    {
        public bool Unavailable { get; set; }

        public Task<float[]> EmbedAsync(EmbeddingProfile profile, string input, EmbeddingPurpose purpose,
            CancellationToken token)
        {
            if (Unavailable) throw new HttpRequestException("Fixture provider is unavailable.");
            var values = new float[profile.Dimensions];
            var index = input.Contains("reconcil", StringComparison.OrdinalIgnoreCase) ? 0 :
                input.Contains("archive", StringComparison.OrdinalIgnoreCase) ? 1 : 2;
            values[index] = 1;
            return Task.FromResult(values);
        }
    }
}
