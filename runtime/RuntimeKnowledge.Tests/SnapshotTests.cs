using RuntimeKnowledge.Contracts;
using RuntimeKnowledge.Store;
using Xunit;

namespace RuntimeKnowledge.Tests;

public sealed class SnapshotTests
{
    [Fact]
    public async Task InvalidReplacementKeepsOneCoherentBaseline()
    {
        await using var s = await Scenario.OpenAsync();
        var initial = await s.ActivateAsync("initial");
        var error = await Assert.ThrowsAsync<WorkspaceException>(() => s.StageCorruptBundleAsync());
        Assert.Equal("invalidBundle", error.Code);
        var page = await s.InventoryAsync();
        Assert.Equal(initial, page.SnapshotId);
        Assert.Contains(page.Entities, entity => entity.Key == s.Isolated);
        Assert.Equal(2, page.Entities.Count(entity => entity.Label == "Order"));
        Assert.All(page.Entities, entity => Assert.Equal(initial, entity.Provenance.SnapshotId));
        Assert.Contains(await s.TraverseAsync(s.A), edge => edge.Key == s.PublishedEdge);
    }

    [Fact]
    public async Task StagedSnapshotStaysHiddenAndActivationInvalidatesOldCursor()
    {
        await using var s = await Scenario.OpenAsync();
        var initial = await s.ActivateAsync("initial");
        var firstPage = await s.InventoryAsync(new PageRequest(null, 1));
        Assert.NotNull(firstPage.NextCursor);
        var next = s.Bundles.AddSnapshot("missing");
        await s.Store.StageAsync(next, CancellationToken.None);
        Assert.Equal(initial, (await s.InventoryAsync()).SnapshotId);
        var position = await s.Store.ActivateAsync(next, CancellationToken.None);
        Assert.Equal(position, await s.Store.ActivateAsync(next, CancellationToken.None));
        var stale = await Assert.ThrowsAsync<WorkspaceException>(
            () => s.InventoryAsync(new PageRequest(firstPage.NextCursor, 1)));
        Assert.Equal("staleCursor", stale.Code);
        var after = await s.InventoryAsync();
        Assert.Equal(next, after.SnapshotId);
        Assert.DoesNotContain(after.Entities, entity => entity.Key == s.B);
        Assert.Contains(after.Gaps, gap => gap.Code == "graph-unavailable");
        Assert.Empty(await s.TraverseAsync(s.A));
    }

    [Fact]
    public async Task TraversalUsesDirectionAndPublishedAssertionIdentity()
    {
        await using var s = await Scenario.OpenAsync();
        await s.ActivateAsync("initial");
        Assert.Empty(await s.TraverseAsync(s.B));
        var assertion = Assert.Single(await s.TraverseAsync(s.B, direction: "incoming"));
        Assert.Equal(s.PublishedEdge, assertion.Key);
        Assert.Equal(s.A, assertion.From);
        Assert.Equal(s.B, assertion.To);
        Assert.Equal("direct", assertion.Provenance.Basis);
        Assert.Equal("docs/source.md", Assert.Single(assertion.Provenance.Evidence).Path);
    }
}
