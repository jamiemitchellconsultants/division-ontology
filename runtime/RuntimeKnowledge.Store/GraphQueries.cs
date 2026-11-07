using System.Data;
using System.Text.Json;
using Npgsql;
using RuntimeKnowledge.Contracts;
using static RuntimeKnowledge.Store.WorkspaceSql;

namespace RuntimeKnowledge.Store;

public sealed class GraphQueries(Db db)
{
    private const int MaxPaths = 100;

    public async Task<InventoryPage> InventoryAsync(PageRequest request, CancellationToken cancellationToken)
    {
        if (request.Limit is < 1 or > 100)
            throw new WorkspaceException("invalidLimit", "Inventory limit must be from 1 to 100.");
        await using var connection = await db.OpenAsync(cancellationToken);
        await using var tx = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var state = await ReadStateAsync(tx, cancellationToken);
        var snapshot = state.RequireSnapshot();
        var after = DecodeCursor(request, snapshot);
        var entities = new List<PublishedEntity>();
        var types = new List<RelationshipType>();
        string? last = null;
        var more = false;
        await using (var command = Command(tx, """
            SELECT record_key, kind, body::text FROM workspace_projection.published_records
             WHERE generation = $1 AND snapshot_id = $2 AND record_key > $3
               AND kind IN ('concept', 'node', 'relationshipType')
             ORDER BY record_key LIMIT $4
            """, state.Generation, snapshot, after, request.Limit + 1))
        await using (var rows = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await rows.ReadAsync(cancellationToken))
            {
                if (entities.Count + types.Count == request.Limit)
                {
                    more = true;
                    break;
                }
                last = rows.GetString(0);
                if (rows.GetString(1) == "relationshipType")
                    types.Add(ReadJson<RelationshipType>(rows.GetString(2)));
                else
                    entities.Add(ReadJson<PublishedEntity>(rows.GetString(2)));
            }
        }

        var cursor = more ? Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(
            new InventoryCursor(snapshot, request.Limit, last!), WorkspaceJson.Options)) : null;
        return new InventoryPage(snapshot, entities, types, await GapsAsync(tx, state, cancellationToken), cursor);
    }

    public async Task<TraversalResult> TraverseAsync(TraversalRequest request, CancellationToken cancellationToken)
    {
        if (request.Start is null || request.MaxDepth is < 1 or > 8 ||
            request.Direction is not ("outgoing" or "incoming" or "both"))
            throw new WorkspaceException("invalidTraversal", "Traversal requires a start, a valid direction and a depth from 1 to 8.");
        await using var connection = await db.OpenAsync(cancellationToken);
        await using var tx = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var state = await ReadStateAsync(tx, cancellationToken);
        var snapshot = state.RequireSnapshot();
        var start = request.Start.StorageKey;
        if (await ReadPublishedAsync(tx, state, start, cancellationToken) is null)
            throw new WorkspaceException("entityNotFound", "The traversal start is not in the active snapshot.");

        var result = new List<TraversalPath>();
        var work = new Queue<PendingPath>();
        work.Enqueue(new PendingPath(start, [], new HashSet<string>(StringComparer.Ordinal) { start }));
        var truncated = false;
        while (work.TryDequeue(out var path) && !truncated)
        {
            var neighbors = await AgeGraph.NeighborsAsync(tx, state.GraphName, path.At, snapshot,
                request.Direction, cancellationToken);
            foreach (var (edgeKey, next) in neighbors)
            {
                if (result.Count == MaxPaths)
                {
                    truncated = true;
                    break;
                }
                var body = await ReadPublishedAsync(tx, state, edgeKey, cancellationToken) ??
                    throw new InvalidOperationException("AGE edge lacks a published record.");
                var edge = ReadJson<PublishedEdge>(body);
                var direction = edge.From.StorageKey == path.At ? "outgoing" : "incoming";
                var step = new TraversalStep(edge.Key, null, edge.From, edge.To, edge.Label,
                    direction, "published", edge.Provenance, Context: edge.Context);
                var steps = path.Steps.Append(step).ToArray();
                result.Add(new TraversalPath(steps));
                if (steps.Length < request.MaxDepth && !path.Visited.Contains(next))
                {
                    var visited = new HashSet<string>(path.Visited, StringComparer.Ordinal) { next };
                    work.Enqueue(new PendingPath(next, steps, visited));
                }
            }
        }
        return new TraversalResult(snapshot, result, await GapsAsync(tx, state, cancellationToken), truncated);
    }

    internal static async Task<string?> ReadPublishedAsync(NpgsqlTransaction tx, ProjectionState state,
        string key, CancellationToken token) => (string?)await ScalarAsync(tx, """
            SELECT body::text FROM workspace_projection.published_records
                WHERE generation = $1 AND snapshot_id = $2 AND record_key = $3
            """, token, state.Generation, state.RequireSnapshot(), key);

    private static string DecodeCursor(PageRequest request, string snapshot)
    {
        if (request.Cursor is null) return "";
        if (request.Cursor.Length > 8192) throw new WorkspaceException("invalidCursor", "Invalid inventory cursor.");
        InventoryCursor cursor;
        try
        {
            cursor = JsonSerializer.Deserialize<InventoryCursor>(Convert.FromBase64String(request.Cursor),
                WorkspaceJson.Options) ?? throw new FormatException();
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            throw new WorkspaceException("invalidCursor", "Invalid inventory cursor.");
        }
        if (cursor.Snapshot != snapshot || cursor.Limit != request.Limit)
            throw new WorkspaceException("staleCursor", "The snapshot or page size has changed. Start a new inventory query.");
        if (string.IsNullOrEmpty(cursor.LastKey)) throw new WorkspaceException("invalidCursor", "Invalid inventory cursor.");
        return cursor.LastKey;
    }

    private sealed record InventoryCursor(string Snapshot, int Limit, string LastKey);
    private sealed record PendingPath(string At, IReadOnlyList<TraversalStep> Steps, HashSet<string> Visited);
}
