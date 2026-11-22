using RuntimeKnowledge.Contracts;

namespace RuntimeKnowledge.Web.Services;

public sealed record GraphQuery(PublishedKey Focus, string Direction)
{
    public TraversalRequest ToRequest(string? cursor = null) =>
        new(Focus, Direction, MaxDepth: 1, IncludeUnavailable: false, Cursor: cursor);
}

public sealed record GraphEdge(string Id, TraversalStep Step);

public sealed class GraphNeighbourhood
{
    private GraphNeighbourhood(GraphQuery query, TraversalResult page, GraphNeighbourhood? previous)
    {
        Query = query;
        SnapshotId = page.SnapshotId;
        NextCursor = page.NextCursor;
        TraversalIncomplete = page.Truncated;

        var nodes = previous is null ? new Dictionary<PublishedKey, TraversalEntity>() : new(previous.Nodes);
        foreach (var entity in page.Entities)
            nodes[entity.Key] = entity;
        Nodes = nodes;

        var edges = previous?.Edges.ToList() ?? [];
        var identities = edges.Select(edge => edge.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var path in page.Paths)
        {
            var step = path.Steps.Single();
            var id = step.RuntimeId is { } runtimeId ? $"r:{runtimeId:D}" : $"p:{WorkspaceLinks.Encode(step.Key!)}";
            if (identities.Add(id))
                edges.Add(new GraphEdge(id, step));
        }
        Edges = edges.AsReadOnly();

        Gaps = (previous?.Gaps ?? []).Concat(page.Gaps).Distinct().ToArray();
        var keys = nodes.Keys.Append(query.Focus);
        var sources = keys.Select(key => key.SourceId).ToHashSet(StringComparer.Ordinal);
        var identifiers = keys.Select(key => key.Identifier).ToHashSet(StringComparer.Ordinal);
        var relevant = new List<CoverageGap>();
        var other = new List<CoverageGap>();
        foreach (var gap in Gaps)
        {
            if (gap.SourceId is { } source && sources.Contains(source) &&
                (gap.NodeIri is null || identifiers.Contains(gap.NodeIri)))
                relevant.Add(gap);
            else
                other.Add(gap);
        }
        RelevantGaps = relevant.AsReadOnly();
        OtherGaps = other.AsReadOnly();
    }

    public GraphQuery Query { get; }
    public string SnapshotId { get; }
    public IReadOnlyDictionary<PublishedKey, TraversalEntity> Nodes { get; }
    public IReadOnlyList<GraphEdge> Edges { get; }
    public IReadOnlyList<CoverageGap> Gaps { get; }
    public IReadOnlyList<CoverageGap> RelevantGaps { get; }
    public IReadOnlyList<CoverageGap> OtherGaps { get; }
    public string? NextCursor { get; }
    public bool TraversalIncomplete { get; }

    public static GraphNeighbourhood FromPage(GraphQuery query, TraversalResult page) => new(query, page, null);

    public GraphNeighbourhood Append(TraversalResult page)
    {
        if (page.SnapshotId != SnapshotId)
            throw new InvalidOperationException("Cannot append a traversal page from a different snapshot.");
        return new GraphNeighbourhood(Query, page, this);
    }
}
