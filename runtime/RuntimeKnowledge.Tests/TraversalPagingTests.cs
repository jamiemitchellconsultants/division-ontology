using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using RuntimeKnowledge.Contracts;
using RuntimeKnowledge.Store;
using Xunit;
using static RuntimeKnowledge.Store.WorkspaceSql;

namespace RuntimeKnowledge.Tests;

public sealed class TraversalPagingTests
{
    private static string Signature(TraversalPath path) => string.Join(">",
        path.Steps.Select(step => step.Key?.Identifier ?? step.RuntimeId!.Value.ToString("D")));

    private static async Task<long> JournalHeadAsync(Scenario s)
    {
        await using var connection = new Npgsql.NpgsqlConnection(s.ConnectionString);
        await connection.OpenAsync();
        await using var command = new Npgsql.NpgsqlCommand(
            "SELECT COALESCE(MAX(position), 0) FROM workspace_journal.events", connection);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<List<TraversalResult>> PageAll(Scenario s, TraversalRequest request)
    {
        var pages = new List<TraversalResult>();
        string? cursor = null;
        do
        {
            var page = await s.Graph.TraverseAsync(request with { Cursor = cursor }, CancellationToken.None);
            pages.Add(page);
            if (page.NextCursor is not null)
                Assert.True(page.NextCursor.Length <= TraversalCursor.MaxCursorChars, "issued cursor exceeds the decode cap");
            Assert.Equal(page.NextCursor is not null, page.Truncated);
            cursor = page.NextCursor;
        } while (cursor is not null);
        return pages;
    }

    [Fact]
    public async Task BusyNodePagesThroughPublishedAndRuntimePathsExactlyOnce()
    {
        await using var s = await Scenario.OpenAsync();
        await s.ActivateTraversalAsync("busy");
        var store = new ContributionStore(s.Db);
        var runtime = await store.ApplyAsync(s.Actor,
            new CreateRelationship(s.Hub, Scenario.TraversalKey("leaf000"),
                RelationshipMeaning.Custom("extra", "Workspace edge beyond the first page.")), CancellationToken.None);

        var pages = await PageAll(s, new TraversalRequest(s.Hub, MaxDepth: 1));
        Assert.True(pages.Count >= 2);
        Assert.Equal(100, pages[0].Paths.Count);
        var signatures = pages.SelectMany(page => page.Paths).Select(Signature).ToArray();
        Assert.Equal(151, signatures.Length);
        Assert.Equal(signatures.Length, signatures.Distinct(StringComparer.Ordinal).Count());
        Assert.Contains(runtime.Id.ToString("D"), signatures);
        var lastPage = pages[^1];
        Assert.Contains(lastPage.Entities, entity => entity.Key == Scenario.TraversalKey("leaf000"));
    }

    [Fact]
    public async Task MultiHopPagesMatchTheExpectedSequenceAcrossABranchBoundary()
    {
        await using var s = await Scenario.OpenAsync();
        await s.ActivateTraversalAsync("branches");
        var expected = new List<string>();
        for (var m = 0; m < 2; m++) expected.Add($"https://fixture.test/t#h-m{m}");
        for (var m = 0; m < 2; m++)
            for (var sub = 0; sub < 3; sub++)
                expected.Add($"https://fixture.test/t#h-m{m}>https://fixture.test/t#m{m}-s{sub}");
        for (var m = 0; m < 2; m++)
            for (var sub = 0; sub < 3; sub++)
                for (var l = 0; l < 30; l++)
                    expected.Add($"https://fixture.test/t#h-m{m}>https://fixture.test/t#m{m}-s{sub}>https://fixture.test/t#s{m}{sub}-l{l:00}");

        var pages = await PageAll(s, new TraversalRequest(s.Hub, MaxDepth: 3));
        Assert.Equal(expected, pages.SelectMany(page => page.Paths).Select(Signature).ToArray());
    }

    [Fact]
    public async Task WideGraphSubmitsEveryCursorAndMatchesTheSequence()
    {
        await using var s = await Scenario.OpenAsync();
        await s.ActivateTraversalAsync("wide");
        var expected = new List<string>();
        for (var m = 0; m < 40; m++) expected.Add($"https://fixture.test/t#h-m{m:00}");
        for (var m = 0; m < 40; m++)
            for (var l = 0; l < 150; l++)
                expected.Add($"https://fixture.test/t#h-m{m:00}>https://fixture.test/t#m{m:00}-l{l:000}");

        var pages = await PageAll(s, new TraversalRequest(s.Hub, MaxDepth: 2));
        Assert.True(pages.Count > 10);
        Assert.Equal(expected, pages.SelectMany(page => page.Paths).Select(Signature).ToArray());
    }

    [Fact]
    public async Task ExactlyOneHundredWithPendingExpansionsIssuesACursorThenAnEmptyFinalPage()
    {
        await using var s = await Scenario.OpenAsync();
        await s.ActivateTraversalAsync("exact");
        var first = await s.Graph.TraverseAsync(new TraversalRequest(s.Hub, MaxDepth: 2), CancellationToken.None);
        Assert.Equal(100, first.Paths.Count);
        Assert.True(first.Truncated);
        Assert.NotNull(first.NextCursor);
        var second = await s.Graph.TraverseAsync(new TraversalRequest(s.Hub, MaxDepth: 2, Cursor: first.NextCursor), CancellationToken.None);
        Assert.Empty(second.Paths);
        Assert.False(second.Truncated);
        Assert.Null(second.NextCursor);
        Assert.NotEmpty(second.Entities);
    }

    [Fact]
    public async Task SnapshotRefreshThatRemovesTheStartYieldsStaleCursor()
    {
        await using var s = await Scenario.OpenAsync();
        await s.ActivateTraversalAsync("busy");
        var first = await s.Graph.TraverseAsync(new TraversalRequest(s.Hub, MaxDepth: 1), CancellationToken.None);
        await s.ActivateAsync("initial");
        var error = await Assert.ThrowsAsync<WorkspaceException>(() =>
            s.Graph.TraverseAsync(new TraversalRequest(s.Hub, MaxDepth: 1, Cursor: first.NextCursor), CancellationToken.None));
        Assert.Equal("staleCursor", error.Code);
    }

    [Fact]
    public async Task WorkspaceEditYieldsStaleCursor()
    {
        await using var s = await Scenario.OpenAsync();
        await s.ActivateTraversalAsync("busy");
        var first = await s.Graph.TraverseAsync(new TraversalRequest(s.Hub, MaxDepth: 1), CancellationToken.None);
        var store = new ContributionStore(s.Db);
        await store.ApplyAsync(s.Actor, new CreateAnnotation(Scenario.TraversalKey("leaf000"), "businessRbac", "Changed."),
            CancellationToken.None);
        var error = await Assert.ThrowsAsync<WorkspaceException>(() =>
            s.Graph.TraverseAsync(new TraversalRequest(s.Hub, MaxDepth: 1, Cursor: first.NextCursor), CancellationToken.None));
        Assert.Equal("staleCursor", error.Code);
    }

    [Fact]
    public async Task ChangedUnavailableStartYieldsInvalidCursorBeforeResolution()
    {
        await using var s = await Scenario.OpenAsync();
        await s.ActivateTraversalAsync("busy");
        var first = await s.Graph.TraverseAsync(new TraversalRequest(s.Hub, MaxDepth: 1), CancellationToken.None);
        var changed = new TraversalRequest(Scenario.TraversalKey("does-not-exist"), MaxDepth: 1, Cursor: first.NextCursor);
        var error = await Assert.ThrowsAsync<WorkspaceException>(() =>
            s.Graph.TraverseAsync(changed, CancellationToken.None));
        Assert.Equal("invalidCursor", error.Code);
    }

    [Fact]
    public async Task ExaminedStepsStayBoundedPerPageAndBudgetStopAdvancesPastFilteredRows()
    {
        await using var s = await Scenario.OpenAsync();
        await s.ActivateAsync("initial");
        var store = new ContributionStore(s.Db);
        for (var i = 0; i < 30; i++)
            await store.ApplyAsync(s.Actor, new CreateRelationship(s.A, s.B,
                RelationshipMeaning.Custom($"r{i}", "Filtered after refresh.")), CancellationToken.None);
        await s.ActivateAsync("missing");

        var filteredCounters = new TraversalCounters { Budget = 10 };
        var examinedTotal = 0;
        var filteredPaths = 0;
        string? filteredCursor = null;
        var filteredPages = 0;
        do
        {
            var page = await s.Graph.TraverseAsync(
                new TraversalRequest(s.A, MaxDepth: 1, Cursor: filteredCursor), CancellationToken.None, filteredCounters);
            Assert.True(filteredCounters.ExaminedSteps <= 10);
            examinedTotal += filteredCounters.ExaminedSteps;
            filteredPaths += page.Paths.Count;
            filteredCursor = page.NextCursor;
            filteredPages++;
        } while (filteredCursor is not null);
        Assert.Equal(0, filteredPaths);
        Assert.True(filteredPages >= 3, $"filtered rows should span budget pages, got {filteredPages}");
        Assert.Equal(30, examinedTotal);

        await s.ActivateTraversalAsync("busy");
        string? cursor = null;
        var counts = new List<int>();
        var busyExaminedTotal = 0;
        do
        {
            var counters = new TraversalCounters();
            var page = await s.Graph.TraverseAsync(new TraversalRequest(s.Hub, MaxDepth: 1, Cursor: cursor), CancellationToken.None, counters);
            counts.Add(counters.ExaminedSteps);
            busyExaminedTotal += counters.ExaminedSteps;
            Assert.True(counters.ExaminedSteps <= 1000);
            cursor = page.NextCursor;
        } while (cursor is not null);
        Assert.All(counts, count => Assert.True(count <= 202, $"examined {count}"));
        Assert.Equal(150, busyExaminedTotal);
    }

    [Fact]
    public async Task WellFormedAlteredFrontierStaysWithinTheCallersReadScope()
    {
        await using var s = await Scenario.OpenAsync();
        await s.ActivateTraversalAsync("busy");
        var first = await s.Graph.TraverseAsync(new TraversalRequest(s.Hub, MaxDepth: 1), CancellationToken.None);
        var journal = await JournalHeadAsync(s);
        var payload = TraversalCursor.Decode(first.NextCursor!, new TraversalRequest(s.Hub, MaxDepth: 1), first.SnapshotId, journal);
        var altered = payload with
        {
            Resume = payload.Resume with
            {
                Pending = [new TraversalPending([0]), .. payload.Resume.Pending.Skip(1)]
            }
        };
        TraversalResult continued;
        try
        {
            continued = await s.Graph.TraverseAsync(
                new TraversalRequest(s.Hub, MaxDepth: 1, Cursor: TraversalCursor.Encode(altered)), CancellationToken.None);
        }
        catch (WorkspaceException error)
        {
            Assert.Equal("invalidCursor", error.Code);
            return;
        }
        Assert.All(continued.Paths, path => Assert.Equal(s.Hub, path.Steps[0].From));
    }

    [Fact]
    public async Task BothDirectionsPageExactlyOnceAndPagingIsReadOnly()
    {
        await using var s = await Scenario.OpenAsync();
        await s.ActivateTraversalAsync("busy");
        var journalBefore = await JournalHeadAsync(s);
        var pages = await PageAll(s, new TraversalRequest(s.Hub, "both", 1));
        Assert.Equal(journalBefore, await JournalHeadAsync(s));
        var signatures = pages.SelectMany(page => page.Paths).Select(Signature).ToArray();
        Assert.Equal(150, signatures.Length);
        Assert.Equal(signatures.Length, signatures.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public async Task NeighbourReadsReturnASelfLoopOncePerDirection()
    {
        // AGE returns a both-matched self-loop as a single row in every direction, so edge-key
        // resume (r.key > after) can never skip its second row; this pins that assumption.
        await using var scenario = await Scenario.OpenAsync();
        await using var source = Npgsql.NpgsqlDataSource.Create(scenario.ConnectionString);
        var store = new Db(source);
        await using var connection = await store.OpenAsync(CancellationToken.None);
        await using var tx = connection.BeginTransaction();
        await ExecuteAsync(tx, "SELECT ag_catalog.create_graph('workspace_g1234abcd')", CancellationToken.None);
        await AgeGraph.AddVertexAsync(tx, "workspace_g1234abcd", "a", CancellationToken.None);
        await AgeGraph.AddEdgeAsync(tx, "workspace_g1234abcd", "e1", "a", "a", "snapshot", CancellationToken.None);
        foreach (var direction in new[] { "outgoing", "incoming", "both" })
        {
            var rows = await AgeGraph.NeighborsAsync(tx, "workspace_g1234abcd", "a", "snapshot",
                direction, CancellationToken.None, "", 10);
            Assert.Single(rows);
        }
        await tx.RollbackAsync();
        await connection.CloseAsync();
    }

    [Fact]
    public async Task BudgetPagesEmitEveryFetchedRowAndExaminedTotalsMatch()
    {
        await using var s = await Scenario.OpenAsync();
        await s.ActivateTraversalAsync("busy");
        string? cursor = null;
        var totalPaths = 0;
        var totalExamined = 0;
        do
        {
            var counters = new TraversalCounters { Budget = 10 };
            var page = await s.Graph.TraverseAsync(new TraversalRequest(s.Hub, MaxDepth: 1, Cursor: cursor), CancellationToken.None, counters);
            Assert.Equal(page.Paths.Count, counters.ExaminedSteps);
            totalPaths += page.Paths.Count;
            totalExamined += counters.ExaminedSteps;
            cursor = page.NextCursor;
        } while (cursor is not null);
        Assert.Equal(150, totalPaths);
        Assert.Equal(150, totalExamined);
    }

    [Fact]
    public async Task HttpTraversalFollowsTheNextLinkAndRejectsAStaleCursorAfterAnEdit()
    {
        var scenario = await Scenario.OpenAsync();
        await scenario.ActivateTraversalAsync("busy");
        await using var api = new ApiFixture(scenario, "Entra");
        var reader = api.ClientWithToken(["ReadKnowledgeGraph"]);
        var manager = api.ClientWithToken(["ManageKnowledgeGraph"]);

        using var firstResponse = await reader.PostAsJsonAsync("/api/traverse",
            new TraversalRequest(scenario.Hub, MaxDepth: 1), WorkspaceJson.Options);
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);
        Assert.Contains("</api/traverse>; rel=\"next\"",
            string.Join(",", firstResponse.Headers.GetValues("Link")));
        var first = (await firstResponse.Content.ReadFromJsonAsync<TraversalResult>(WorkspaceJson.Options))!;
        Assert.Equal(100, first.Paths.Count);
        Assert.NotNull(first.NextCursor);

        using var secondResponse = await reader.PostAsJsonAsync("/api/traverse",
            new TraversalRequest(scenario.Hub, MaxDepth: 1, Cursor: first.NextCursor), WorkspaceJson.Options);
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        var second = (await secondResponse.Content.ReadFromJsonAsync<TraversalResult>(WorkspaceJson.Options))!;
        Assert.NotEmpty(second.Paths);
        Assert.Empty(first.Paths.Select(Signature).Intersect(second.Paths.Select(Signature)));

        using var created = await manager.PostAsJsonAsync("/api/contributions/annotations",
            new CreateAnnotation(Scenario.TraversalKey("leaf000"), "businessExplanation", "Edited after paging."));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        using var stale = await reader.PostAsJsonAsync("/api/traverse",
            new TraversalRequest(scenario.Hub, MaxDepth: 1, Cursor: first.NextCursor), WorkspaceJson.Options);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        var problem = JsonSerializer.Deserialize<JsonElement>(await stale.Content.ReadAsStringAsync());
        Assert.Equal("staleCursor", problem.GetProperty("code").GetString());
    }

    [Fact]
    public async Task EmptyFirstPageRetainsTheStartEntityAndSerializesWithoutNextCursor()
    {
        await using var s = await Scenario.OpenAsync();
        await s.ActivateAsync("initial");
        var page = await s.Graph.TraverseAsync(new TraversalRequest(s.Isolated), CancellationToken.None);
        Assert.Empty(page.Paths);
        Assert.Contains(page.Entities, entity => entity.Key == s.Isolated);
        var json = JsonSerializer.Serialize(page, WorkspaceJson.Options);
        Assert.DoesNotContain("nextCursor", json);
        Assert.Contains("https://fixture.test/core#isolated", json);
    }
}
