using RuntimeKnowledge.Contracts;
using RuntimeKnowledge.Web.Services;
using Xunit;

namespace RuntimeKnowledge.Tests;

public sealed class GraphNeighbourhoodTests
{
    private static readonly PublishedKey Ecommerce = new("fixture/ecommerce", "concept", "https://example.test/ecommerce#Order");
    private static readonly PublishedKey Warehouse = new("fixture/warehouse", "concept", "https://example.test/warehouse#Order");
    private static readonly PublishedKey Published = new("fixture/ecommerce", "assertion", "https://example.test/ecommerce#handoff");
    private static readonly Guid RuntimeId = Guid.Parse("373ca2ef-d6aa-4192-8034-47532bcdfd18");

    [Fact]
    public void DirectionsAndExactCursorAreSentWithThePublishedFocus()
    {
        var query = new GraphQuery(Ecommerce, "incoming");
        Assert.Equal(new TraversalRequest(Ecommerce, "incoming", 1, false), query.ToRequest());
        Assert.Equal(new TraversalRequest(Ecommerce, "incoming", 1, false, "opaque"), query.ToRequest("opaque"));
    }

    [Fact]
    public void ParallelOriginsAndSameLabelNodesRemainDistinctAcrossPages()
    {
        var published = new TraversalStep(Published, null, Warehouse, Ecommerce, "hands off",
            "incoming", "published", null);
        var runtime = published with { Key = null, RuntimeId = RuntimeId, Origin = "runtime" };
        var first = GraphNeighbourhood.FromPage(new GraphQuery(Ecommerce, "both"), Page("next", published));
        var result = first.Append(Page(null, published, runtime));
        Assert.Equal(2, result.Nodes.Count);
        Assert.Equal(new[] { "Order", "Order" }, result.Nodes.Values.Select(entity => entity.Label));
        Assert.Equal(new[] { "p:" + WorkspaceLinks.Encode(Published), $"r:{RuntimeId:D}" }, result.Edges.Select(edge => edge.Id));
        Assert.All(result.Edges, edge => Assert.Equal((Warehouse, Ecommerce), (edge.Step.From, edge.Step.To)));
        Assert.Single(first.Edges);
    }

    [Fact]
    public void EmptyContinuationAdvancesAndAnEmptyFinalPageRetainsTheFocus()
    {
        var first = GraphNeighbourhood.FromPage(new GraphQuery(Ecommerce, "both"), Page("first"));
        var intermediate = first.Append(Page("second"));
        var final = intermediate.Append(Page(null));
        Assert.Equal("second", intermediate.NextCursor);
        Assert.True(intermediate.TraversalIncomplete);
        Assert.Empty(final.Edges);
        Assert.True(final.Nodes.ContainsKey(Ecommerce));
        Assert.Null(final.NextCursor);
        Assert.False(final.TraversalIncomplete);
    }

    [Fact]
    public void APageFromAnotherSnapshotCannotBeMerged()
    {
        var first = GraphNeighbourhood.FromPage(new GraphQuery(Ecommerce, "both"), Page("next"));
        Assert.Throws<InvalidOperationException>(() => first.Append(Page(null) with { SnapshotId = "replacement" }));
        Assert.Equal("next", first.NextCursor);
    }

    [Fact]
    public void CoverageGapsAreScopedToTheLoadedSourcesAndNodes()
    {
        var sourceGap = new CoverageGap("coverage-missing", "fixture/ecommerce", null, "Code coverage missing");
        var otherNode = new CoverageGap("denotation-unresolved", "fixture/ecommerce", "https://example.test/ecommerce#Other", "Unresolved");
        var focusNode = otherNode with { NodeIri = Ecommerce.Identifier };
        var elsewhere = new CoverageGap("graph-unavailable", "fixture/billing", null, "Graph unavailable");
        var view = GraphNeighbourhood.FromPage(new GraphQuery(Ecommerce, "both"),
            Page(null) with { Gaps = [sourceGap, otherNode, focusNode, elsewhere] });
        Assert.Equal(new[] { sourceGap, focusNode }, view.RelevantGaps);
        Assert.Equal(new[] { otherNode, elsewhere }, view.OtherGaps);
        Assert.Equal(4, view.Gaps.Count);
    }

    [Fact]
    public void EmptyFinalPageRetainsLoadedEdgesAndCoverage()
    {
        var edge = new TraversalStep(Published, null, Ecommerce, Warehouse, "hands off",
            "outgoing", "published", null);
        var gap = new CoverageGap("coverage-missing", Warehouse.SourceId, Warehouse.Identifier, "Missing evidence");
        var first = GraphNeighbourhood.FromPage(new GraphQuery(Ecommerce, "both"),
            Page("next", edge) with { Gaps = [gap] });
        var final = first.Append(Page(null));
        Assert.Equal(edge, Assert.Single(final.Edges).Step);
        Assert.True(final.Nodes.ContainsKey(Warehouse));
        Assert.Equal(gap, Assert.Single(final.RelevantGaps));
        Assert.Null(final.NextCursor);
    }

    [Fact]
    public void TruncationWithoutContinuationIsReported()
    {
        var view = GraphNeighbourhood.FromPage(new GraphQuery(Ecommerce, "both"),
            Page(null) with { Truncated = true });
        Assert.True(view.TraversalIncomplete);
        Assert.Null(view.NextCursor);
    }

    private static TraversalResult Page(string? cursor, params TraversalStep[] edges) =>
        new("snapshot", edges.Select(step => new TraversalPath([step])).ToArray(), [], cursor is not null,
            edges.Length == 0 ? [Entity(Ecommerce)] : [Entity(Ecommerce), Entity(Warehouse)], cursor);

    private static TraversalEntity Entity(PublishedKey key) =>
        new(key, "Order", new Provenance(key, "commit", "snapshot", null, []), true, []);
}
