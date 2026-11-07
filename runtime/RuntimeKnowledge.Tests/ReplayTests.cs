using Npgsql;
using RuntimeKnowledge.Contracts;
using RuntimeKnowledge.Store;
using Xunit;

namespace RuntimeKnowledge.Tests;

public sealed class ReplayTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    [Fact]
    public async Task EmptyProjectionReplaysLastPublishedProvenanceAndRemovals()
    {
        await using var s = await Scenario.OpenAsync();
        await s.ActivateAsync("initial");
        var store = new ContributionStore(s.Db);
        var replay = new ReplayStore(s.Db, s.Bundles);
        var edge = await store.ApplyAsync(s.Actor, new CreateRelationship(s.A, s.B,
            RelationshipMeaning.Custom("uses", "Retained edge.")), None);
        var note = await store.ApplyAsync(s.Actor,
            new CreateAnnotation(s.A, "businessExplanation", "Removed later."), None);
        await store.ApplyAsync(s.Actor, new RemoveContribution(note.Id, note.Version), None);
        var updated = await s.ActivateAsync("changed");
        await s.ActivateAsync("missing");

        var original = await replay.DescribeCurrentAsync(None);
        var rebuilt = await replay.RebuildAsync(original.ThroughPosition, None);
        Assert.NotEqual(original.Generation, rebuilt.Generation);
        Assert.Equal(original.StateDigest, rebuilt.StateDigest);
        await replay.SetWritesPausedAsync(true, None);
        await replay.CutoverAsync(rebuilt.Generation, rebuilt.ThroughPosition, None);
        await replay.SetWritesPausedAsync(false, None);

        Assert.Equal(original.StateDigest, (await replay.DescribeCurrentAsync(None)).StateDigest);
        Assert.DoesNotContain(await s.TraverseAsync(s.A), item => item.RuntimeId == edge.Id);
        Assert.Contains(await s.TraverseAsync(s.A, includeUnavailable: true),
            item => item.RuntimeId == edge.Id && item.LastKnownSnapshotId == updated);
        Assert.True((await store.FindAsync(note.Id, None))!.Removed);
        Assert.Equal(2, (await store.HistoryAsync(note.Id, new PageRequest(), None)).Entries.Count);
        await s.ActivateAsync("returned");
        Assert.Contains(await s.TraverseAsync(s.A), item => item.RuntimeId == edge.Id);
    }

    [Fact]
    public async Task MissingBundleCannotReplaceTheLiveProjection()
    {
        await using var s = await Scenario.OpenAsync();
        var id = await s.ActivateAsync("initial");
        s.Bundles.RemoveManifest(id);
        var replay = new ReplayStore(s.Db, s.Bundles);
        var current = await replay.DescribeCurrentAsync(None);
        var error = await Assert.ThrowsAsync<WorkspaceException>(
            () => replay.RebuildAsync(current.ThroughPosition, None));
        Assert.Equal("invalidBundle", error.Code);
        Assert.Equal(current, await replay.DescribeCurrentAsync(None));
    }

    [Theory]
    [InlineData("UPDATE workspace_journal.events SET schema_version = 2 WHERE position = 1")]
    [InlineData("DELETE FROM workspace_journal.events WHERE position = 1")]
    public async Task InvalidJournalFailsReplayWithoutSwitchingSnapshot(string mutation)
    {
        await using var s = await Scenario.OpenAsync();
        var id = await s.ActivateAsync("initial");
        await using var db = await s.Db.OpenAsync(None);
        await using var command = new NpgsqlCommand(mutation, db);
        await command.ExecuteNonQueryAsync();
        var error = await Assert.ThrowsAsync<WorkspaceException>(() => new ReplayStore(s.Db, s.Bundles).RebuildAsync(1, None));
        Assert.Equal("invalidJournal", error.Code);
        Assert.Equal(id, (await s.InventoryAsync()).SnapshotId);
    }

    [Fact]
    public async Task CutoverRequiresWritePauseAndTheSameJournalHead()
    {
        await using var s = await Scenario.OpenAsync();
        await s.ActivateAsync("initial");
        var replay = new ReplayStore(s.Db, s.Bundles);
        var current = await replay.DescribeCurrentAsync(None);
        var candidate = await replay.RebuildAsync(current.ThroughPosition, None);
        var live = await Assert.ThrowsAsync<WorkspaceException>(
            () => replay.CutoverAsync(candidate.Generation, current.ThroughPosition, None));
        Assert.Equal("writesNotPaused", live.Code);
        await new ContributionStore(s.Db).ApplyAsync(s.Actor,
            new CreateAnnotation(s.A, "businessExplanation", "Saved after rebuild."), None);
        await replay.SetWritesPausedAsync(true, None);
        var stale = await Assert.ThrowsAsync<WorkspaceException>(
            () => replay.CutoverAsync(candidate.Generation, current.ThroughPosition, None));
        Assert.Equal("rebuildStale", stale.Code);
        Assert.Equal(current.Generation, (await replay.DescribeCurrentAsync(None)).Generation);
    }
}
