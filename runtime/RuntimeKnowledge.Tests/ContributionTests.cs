using Npgsql;
using RuntimeKnowledge.Contracts;
using RuntimeKnowledge.Store;
using Xunit;

namespace RuntimeKnowledge.Tests;

public sealed class ContributionTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    [Fact]
    public async Task SuccessfulEdgeAndHistoryAreImmediatelyVisible()
    {
        await using var s = await Scenario.OpenAsync();
        await s.ActivateAsync("initial");
        var store = new ContributionStore(s.Db);
        var edge = await CreateEdge(s, store);
        Assert.Contains(await s.TraverseAsync(s.A), item => item.RuntimeId == edge.Id);
        var entry = Assert.Single((await store.HistoryAsync(edge.Id, new PageRequest(), None)).Entries);
        Assert.Equal(s.Actor, entry.Actor);
        Assert.Equal(edge.Position, entry.Position);
        Assert.Equal("relationshipCreated", entry.Type);
    }

    [Fact]
    public async Task ARejectedCommitLeavesNeitherHistoryNorGap()
    {
        await using var s = await Scenario.OpenAsync();
        await s.ActivateAsync("initial");
        var store = new ContributionStore(s.Db);
        var first = await CreateEdge(s, store);
        await using (var db = await s.Db.OpenAsync(None))
        await using (var trigger = new NpgsqlCommand("""
            CREATE FUNCTION reject_fixture_commit() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                IF NEW.payload #>> '{after,relationship,meaning,customLabel}' = 'reject commit' THEN
                    RAISE EXCEPTION 'fixture failure at commit';
                END IF;
                RETURN NEW;
            END
            $$;
            CREATE CONSTRAINT TRIGGER reject_fixture_commit AFTER INSERT ON workspace_journal.events
                DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION reject_fixture_commit();
            """, db))
            await trigger.ExecuteNonQueryAsync();

        await Assert.ThrowsAsync<PostgresException>(() => store.ApplyAsync(s.Actor,
            new CreateRelationship(s.A, s.B, RelationshipMeaning.Custom("reject commit", "Fixture-only failure.")), None));
        var second = await CreateEdge(s, store);
        Assert.Equal(first.Position + 1, second.Position);
        Assert.Equal(2, (await s.TraverseAsync(s.A)).Count(edge => edge.RuntimeId.HasValue));
        Assert.Equal(2, (await store.HistoryAsync(null, new PageRequest(), None)).Entries.Count);
    }

    [Fact]
    public async Task IncompleteMeaningCreatesNeitherRelationshipNorHistory()
    {
        await using var s = await Scenario.OpenAsync();
        await s.ActivateAsync("initial");
        var store = new ContributionStore(s.Db);
        var error = await Assert.ThrowsAsync<WorkspaceException>(() => store.ApplyAsync(s.Actor,
            new CreateRelationship(s.A, s.B, RelationshipMeaning.Custom("routes", " ")), None));
        Assert.Equal("invalidMeaning", error.Code);
        Assert.DoesNotContain(await s.TraverseAsync(s.A), step => step.RuntimeId.HasValue);
        Assert.Empty((await store.HistoryAsync(null, new PageRequest(), None)).Entries);
    }

    [Fact]
    public async Task PublicationWinsBeforeWaitingWriteAndRetainedRecordsStayEditable()
    {
        await using var s = await Scenario.OpenAsync();
        await s.ActivateAsync("initial");
        var store = new ContributionStore(s.Db);
        var edge = await CreateEdge(s, store);
        var missing = s.Bundles.AddSnapshot("missing");
        await s.Store.StageAsync(missing, None);
        await using var gate = await s.Db.OpenAsync(None);
        await using (var install = new NpgsqlCommand("""
            CREATE FUNCTION wait_fixture_activation() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                PERFORM pg_advisory_xact_lock(3185027);
                RETURN NEW;
            END
            $$;
            CREATE TRIGGER wait_fixture_activation BEFORE UPDATE OF snapshot_id ON workspace_projection.state
                FOR EACH ROW EXECUTE FUNCTION wait_fixture_activation();
            SELECT pg_advisory_lock(3185027);
            """, gate))
            await install.ExecuteNonQueryAsync();

        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var activation = s.Store.ActivateAsync(missing, deadline.Token);
        Task<ContributionResult>? waitingSave = null;
        try
        {
            await using var waiting = new NpgsqlCommand("""
                SELECT EXISTS (SELECT FROM pg_locks
                    WHERE locktype = 'advisory' AND objid = 3185027 AND NOT granted)
                """, gate);
            while (await waiting.ExecuteScalarAsync(deadline.Token) is not true)
                await Task.Delay(10, deadline.Token);
            waitingSave = store.ApplyAsync(s.Actor, new CreateRelationship(s.A, s.B,
                RelationshipMeaning.Custom("uses", "Racing publication.")), deadline.Token);
        }
        finally
        {
            await using var unlock = new NpgsqlCommand("SELECT pg_advisory_unlock(3185027)", gate);
            await unlock.ExecuteNonQueryAsync();
        }
        await activation;
        var failure = await Assert.ThrowsAsync<WorkspaceException>(() => waitingSave!);
        Assert.Equal("endpointUnavailable", failure.Code);
        Assert.DoesNotContain(await s.TraverseAsync(s.A), item => item.RuntimeId == edge.Id);
        Assert.Contains(await s.TraverseAsync(s.A, includeUnavailable: true),
            item => item.RuntimeId == edge.Id && item.EndpointUnavailable);

        var updated = await store.ApplyAsync(s.Actor, new EditRelationship(edge.Id, edge.Version, s.A, s.B,
            RelationshipMeaning.Custom("uses", "Clarified with a retained endpoint.")), None);
        Assert.Equal(2, updated.Version);
        var stale = await Assert.ThrowsAsync<WorkspaceException>(() =>
            store.ApplyAsync(s.Actor, new RemoveContribution(edge.Id, edge.Version), None));
        Assert.Equal("versionConflict", stale.Code);
        await s.ActivateAsync("returned");
        Assert.Contains(await s.TraverseAsync(s.A), item => item.RuntimeId == edge.Id);
    }

    [Fact]
    public async Task RoleNotesReturnDuringTraversalAndRemovalKeepsHistory()
    {
        await using var s = await Scenario.OpenAsync();
        await s.ActivateAsync("initial");
        var store = new ContributionStore(s.Db);
        var note = await store.ApplyAsync(s.Actor, new CreateAnnotation(s.B, "businessRbac",
            "Only the finance team releases invoices."), None);
        var traversal = await s.Graph.TraverseAsync(new TraversalRequest(s.A), None);
        Assert.Contains(traversal.Paths.SelectMany(path => path.Steps), step => step.Key == s.PublishedEdge);
        var context = Assert.Single(traversal.Entities.Where(entity => entity.Key == s.B).SelectMany(entity => entity.Annotations));
        Assert.Equal(note.Id, context.Id);
        Assert.Equal("businessRbac", context.Annotation!.Category);
        await store.ApplyAsync(s.Actor, new RemoveContribution(note.Id, note.Version), None);
        traversal = await s.Graph.TraverseAsync(new TraversalRequest(s.A), None);
        Assert.Empty(traversal.Entities.SelectMany(entity => entity.Annotations));
        Assert.Collection((await store.HistoryAsync(note.Id, new PageRequest(), None)).Entries,
            created => Assert.Equal("annotationCreated", created.Type),
            removed => Assert.True(removed.After.Removed));
    }

    [Fact]
    public async Task ExplicitAssociationPreservesBothEdgesAndProvenances()
    {
        await using var s = await Scenario.OpenAsync();
        var snapshot = await s.ActivateAsync("initial");
        var store = new ContributionStore(s.Db);
        var edge = await store.ApplyAsync(s.Actor,
            new CreateRelationship(s.A, s.B, RelationshipMeaning.Published(Scenario.KeyType)), None);
        var association = await store.ApplyAsync(s.Actor,
            new AssociateCodification(edge.Id, edge.Version, s.PublishedEdge), None);
        Assert.Equal(2, association.Version);
        var path = await s.TraverseAsync(s.A);
        var published = Assert.Single(path, item => item.Key == s.PublishedEdge && item.Origin == "published");
        Assert.Equal(edge.Id, Assert.Single(published.CodifyingContributions!).Id);
        var runtime = Assert.Single(path, item => item.RuntimeId == edge.Id);
        var link = Assert.Single(runtime.Contribution!.Associations);
        Assert.Equal(s.PublishedEdge, link.PublishedRelationship);
        Assert.Equal(snapshot, link.PublishedProvenance.SnapshotId);
        Assert.Equal(s.Actor, link.Actor);
    }

    private static Task<ContributionResult> CreateEdge(Scenario s, ContributionStore store) =>
        store.ApplyAsync(s.Actor, new CreateRelationship(s.A, s.B,
            RelationshipMeaning.Custom("reconciles", "Connects orders to accounting.")), None);
}
