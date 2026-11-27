using RuntimeKnowledge.Contracts;
using RuntimeKnowledge.Store;
using Xunit;

namespace RuntimeKnowledge.Tests;

[Trait("Category", "Database")]
public sealed class AssessmentListTests
{
    private const string Frozen = "{\"format\":1,\"record\":{}}";
    private static readonly Actor Manager = new("https://issuer.test", "manager");

    private sealed class SteppingClock(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;
        public bool Freeze { get; set; }
        public override DateTimeOffset GetUtcNow() { var now = _now; if (!Freeze) _now = _now.AddSeconds(1); return now; }
    }

    [Fact]
    public async Task Lists_newest_first_in_pages_of_at_most_50_with_a_cursor()
    {
        await using var scenario = await Scenario.OpenAsync();
        var snapshot = await scenario.ActivateAsync("initial");
        var store = new AssessmentStore(scenario.Db, new SteppingClock(DateTimeOffset.Parse("2026-11-27T00:00:00Z")));
        var ids = new List<Guid>();
        for (var i = 0; i < 120; i++) ids.Add((await store.SaveAsync(Manager, snapshot, $"CR-{i:000}", Frozen, "# x\n", null, CancellationToken.None)).Id);
        var seen = new List<AssessmentSummary>();
        string? cursor = null;
        var pages = 0;
        do
        {
            var page = await store.ListAsync(cursor, 50, CancellationToken.None);
            Assert.True(page.Assessments.Count <= 50);
            seen.AddRange(page.Assessments);
            cursor = page.NextCursor;
            pages++;
        } while (cursor is not null);
        Assert.Equal(3, pages);
        Assert.Equal(Enumerable.Reverse(ids), seen.Select(s => s.Id));
        Assert.Equal("CR-119", seen[0].RequestId);
        Assert.All(seen, s => Assert.Equal(snapshot, s.SnapshotId));
    }

    [Fact]
    public async Task Equal_save_times_keep_a_stable_order_across_pages()
    {
        await using var scenario = await Scenario.OpenAsync();
        var snapshot = await scenario.ActivateAsync("initial");
        var store = new AssessmentStore(scenario.Db, new SteppingClock(DateTimeOffset.Parse("2026-11-27T00:00:00Z")) { Freeze = true });
        for (var i = 0; i < 7; i++) await store.SaveAsync(Manager, snapshot, $"CR-{i}", Frozen, "# x\n", null, CancellationToken.None);
        async Task<List<Guid>> ReadAll()
        {
            var ids = new List<Guid>();
            string? cursor = null;
            do
            {
                var page = await store.ListAsync(cursor, 3, CancellationToken.None);
                ids.AddRange(page.Assessments.Select(a => a.Id));
                cursor = page.NextCursor;
            } while (cursor is not null);
            return ids;
        }
        var firstRead = await ReadAll();
        Assert.Equal(7, firstRead.Distinct().Count());              // no row repeated or skipped across page boundaries
        Assert.Equal(firstRead, await ReadAll());                   // the tie order is stable between reads
    }

    [Theory]
    [InlineData(0)]
    [InlineData(51)]
    public async Task Limits_outside_1_to_50_are_refused(int limit)
    {
        await using var scenario = await Scenario.OpenAsync();
        await scenario.ActivateAsync("initial");
        var error = await Assert.ThrowsAsync<WorkspaceException>(() => new AssessmentStore(scenario.Db).ListAsync(null, limit, CancellationToken.None));
        Assert.Equal("invalidLimit", error.Code);
    }

    [Fact]
    public async Task A_malformed_cursor_is_invalid()
    {
        await using var scenario = await Scenario.OpenAsync();
        await scenario.ActivateAsync("initial");
        var error = await Assert.ThrowsAsync<WorkspaceException>(() => new AssessmentStore(scenario.Db).ListAsync("not-a-cursor", 50, CancellationToken.None));
        Assert.Equal("invalidCursor", error.Code);
    }
}
