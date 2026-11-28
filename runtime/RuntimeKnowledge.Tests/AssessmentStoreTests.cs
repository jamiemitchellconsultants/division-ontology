using System.Text;
using RuntimeKnowledge.Contracts;
using RuntimeKnowledge.Store;
using Xunit;

namespace RuntimeKnowledge.Tests;

[Trait("Category", "Database")]
public sealed class AssessmentStoreTests
{
    private const string Frozen = "{\"format\":1,\"record\":{\"requestId\":\"CR-9\",\"z\":1,\"a\":2},\"digest\":\"d\"}";
    private const string Markdown = "# Impact assessment: CR-9\n\nbody | &#124;\n";
    private static readonly Actor Manager = new("https://issuer.test", "manager");

    private static async Task<(Scenario Scenario, AssessmentStore Store, string Snapshot)> OpenAsync()
    {
        var scenario = await Scenario.OpenAsync();
        var snapshot = await scenario.ActivateAsync("initial");
        return (scenario, new AssessmentStore(scenario.Db), snapshot);
    }

    [Fact]
    public async Task A_save_stores_exact_bytes_and_reads_back_identically()                   // B18, B20, Q5
    {
        var (scenario, store, snapshot) = await OpenAsync();
        await using var _ = scenario;
        var saved = await store.SaveAsync(Manager, snapshot, "CR-9", Frozen, Markdown, null, CancellationToken.None);
        var read = await store.GetAsync(saved.Id, CancellationToken.None);
        Assert.Equal(Frozen, read!.Frozen.ToJsonString());
        Assert.Equal(Markdown, read.Markdown);
        Assert.Equal(Encoding.UTF8.GetBytes(Markdown), await store.MarkdownAsync(saved.Id, CancellationToken.None));
        Assert.Equal(Manager, read.Author);
        Assert.Equal(1, read.Version);
        Assert.False(read.PinnedToEarlierSnapshot);
        Assert.Single((await store.RevisionsAsync(saved.Id, null, 50, CancellationToken.None))!.Revisions);
    }

    [Fact]
    public async Task A_failed_save_leaves_nothing_behind()                                    // B18, Q5
    {
        var (scenario, store, snapshot) = await OpenAsync();
        await using var _ = scenario;
        store.BeforeCommit = () => throw new InvalidOperationException("interrupted");
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveAsync(Manager, snapshot, "CR-9", Frozen, Markdown, null, CancellationToken.None));
        await using var connection = await scenario.Db.OpenAsync(CancellationToken.None);
        await using var count = new Npgsql.NpgsqlCommand(
            "SELECT (SELECT count(*) FROM workspace_projection.assessments) + (SELECT count(*) FROM workspace_journal.events WHERE event_type LIKE 'assessment%')", connection);
        Assert.Equal(0L, (long)(await count.ExecuteScalarAsync())!);
    }

    [Fact]
    public async Task Saving_against_another_snapshot_is_snapshot_changed()                    // B36
    {
        var (scenario, store, _) = await OpenAsync();
        await using var __ = scenario;
        var error = await Assert.ThrowsAsync<WorkspaceException>(() => store.SaveAsync(Manager, new string('0', 64), "CR-9", Frozen, Markdown, null, CancellationToken.None));
        Assert.Equal("snapshotChanged", error.Code);
    }

    [Fact]
    public async Task An_edit_keeps_the_previous_content_editor_and_time()                    // B22
    {
        var (scenario, store, snapshot) = await OpenAsync();
        await using var _ = scenario;
        var saved = await store.SaveAsync(Manager, snapshot, "CR-9", Frozen, Markdown, null, CancellationToken.None);
        var editor = new Actor("https://issuer.test", "second-manager");
        await store.ReviseAsync(editor, saved.Id, 1, snapshot, Frozen.Replace("\"z\":1", "\"z\":3"), Markdown + "more\n", CancellationToken.None);
        var read = await store.GetAsync(saved.Id, CancellationToken.None);
        Assert.Equal(2, read!.Version);
        var revisions = (await store.RevisionsAsync(saved.Id, null, 50, CancellationToken.None))!.Revisions;
        Assert.Equal([1, 2], revisions.Select(r => r.Version));
        Assert.Equal(Frozen, (await store.RevisionAsync(saved.Id, 1, CancellationToken.None))!.Frozen.ToJsonString());
        Assert.Equal(editor, revisions[1].Editor);
        var stale = await Assert.ThrowsAsync<WorkspaceException>(() => store.ReviseAsync(editor, saved.Id, 1, snapshot, Frozen, Markdown, CancellationToken.None));
        Assert.Equal("versionConflict", stale.Code);
    }

    [Fact]
    public async Task After_a_new_snapshot_it_is_readable_unchanged_and_edits_are_refused()   // B33, B34
    {
        var (scenario, store, snapshot) = await OpenAsync();
        await using var _ = scenario;
        var saved = await store.SaveAsync(Manager, snapshot, "CR-9", Frozen, Markdown, null, CancellationToken.None);
        var newer = await scenario.ActivateAsync("changed");
        var read = await store.GetAsync(saved.Id, CancellationToken.None);
        Assert.True(read!.PinnedToEarlierSnapshot);
        Assert.Equal(Frozen, read.Frozen.ToJsonString());
        var error = await Assert.ThrowsAsync<WorkspaceException>(() => store.ReviseAsync(Manager, saved.Id, 1, newer, Frozen, Markdown, CancellationToken.None));
        Assert.Equal("snapshotMismatch", error.Code);
    }

    [Fact]
    public async Task Approval_fields_are_refused_by_the_store()                              // B31
    {
        var (scenario, store, snapshot) = await OpenAsync();
        await using var _ = scenario;
        var error = await Assert.ThrowsAsync<WorkspaceException>(() => store.SaveAsync(Manager, snapshot, "CR-9",
            "{\"format\":1,\"record\":{\"approvalStatus\":\"approved\"}}", Markdown, null, CancellationToken.None));
        Assert.Equal("approvalNotSupported", error.Code);
    }

    [Fact]
    public async Task A_rebuild_copies_assessments_byte_for_byte_and_history_ignores_them()   // Q5
    {
        var (scenario, store, snapshot) = await OpenAsync();
        await using var _ = scenario;
        var saved = await store.SaveAsync(Manager, snapshot, "CR-9", Frozen, Markdown, null, CancellationToken.None);
        var replay = new ReplayStore(scenario.Db, scenario.Bundles);
        var live = await replay.DescribeCurrentAsync(CancellationToken.None);
        var rebuilt = await replay.RebuildAsync(saved.Position, CancellationToken.None);
        Assert.Equal(live.StateDigest, rebuilt.StateDigest);
        var history = await new ContributionStore(scenario.Db).HistoryAsync(null, new PageRequest(null, 50), CancellationToken.None);
        Assert.Empty(history.Entries);
    }
}
