using System.Data;
using System.Text.Json;
using Npgsql;
using NpgsqlTypes;
using RuntimeKnowledge.Contracts;

namespace RuntimeKnowledge.Store;

internal sealed record ProjectionState(Guid Generation, string GraphName, string? SnapshotId,
    long? ActivationPosition, bool WritesPaused)
{
    public string RequireSnapshot() => SnapshotId ??
        throw new WorkspaceException("snapshotUnavailable", "No published knowledge snapshot is active.");
}

internal static class WorkspaceSql
{
    public static string Json<T>(T value) => JsonSerializer.Serialize(value, WorkspaceJson.Options);
    public static T ReadJson<T>(string value) =>
        JsonSerializer.Deserialize<T>(value, WorkspaceJson.Options) ??
        throw new InvalidOperationException("Missing stored workspace data.");

    public static NpgsqlCommand Command(NpgsqlTransaction tx, string sql, params object?[] values)
    {
        var command = new NpgsqlCommand(sql, tx.Connection!, tx);
        foreach (var value in values)
        {
            if (value is null)
                command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Unknown, Value = DBNull.Value });
            else
                command.Parameters.AddWithValue(value);
        }
        return command;
    }

    public static async Task<int> ExecuteAsync(NpgsqlTransaction tx, string sql, CancellationToken token,
        params object?[] values)
    {
        await using var command = Command(tx, sql, values);
        return await command.ExecuteNonQueryAsync(token);
    }

    public static async Task<object?> ScalarAsync(NpgsqlTransaction tx, string sql, CancellationToken token,
        params object?[] values)
    {
        await using var command = Command(tx, sql, values);
        return await command.ExecuteScalarAsync(token);
    }

    public static async Task<ProjectionState> ReadStateAsync(NpgsqlTransaction tx, CancellationToken token)
    {
        await using var command = Command(tx, """
            SELECT generation, graph_name, snapshot_id, activation_position, writes_paused
              FROM workspace_projection.state WHERE singleton
            """);
        await using var row = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, token);
        if (!await row.ReadAsync(token)) throw new InvalidOperationException("Workspace state is missing.");
        return new ProjectionState(row.GetGuid(0), row.GetString(1),
            row.IsDBNull(2) ? null : row.GetString(2), row.IsDBNull(3) ? null : row.GetInt64(3), row.GetBoolean(4));
    }

    public static async Task<long> LockHeadAsync(NpgsqlTransaction tx, CancellationToken token) =>
        (long)(await ScalarAsync(tx, """
            SELECT position FROM workspace_journal.head WHERE singleton FOR UPDATE
            """, token) ?? throw new InvalidOperationException("Workspace journal head is missing."));

    public static async Task<long> AppendAsync(NpgsqlTransaction tx, string type, string snapshotId,
        Guid? id, int? revision, Actor? actor, DateTimeOffset occurredAt, object payload, CancellationToken token)
    {
        return (long)(await ScalarAsync(tx, """
            INSERT INTO workspace_journal.events
                (position, event_id, event_type, schema_version, actor_issuer, actor_subject,
                 occurred_at, snapshot_id, aggregate_id, aggregate_revision, payload)
            VALUES (workspace_journal.next_position(), $1, $2, 1, $3, $4, $5, $6, $7, $8, $9::jsonb)
            RETURNING position
            """, token, Guid.NewGuid(), type, actor?.Issuer, actor?.Subject, occurredAt, snapshotId,
            id, revision, Json(payload)) ?? throw new InvalidOperationException("Journal append returned no position."));
    }

    public static async Task<IReadOnlyList<CoverageGap>> GapsAsync(NpgsqlTransaction tx,
        ProjectionState state, CancellationToken token) =>
        ReadJson<CoverageGap[]>((string?)await ScalarAsync(tx, """
            SELECT gaps::text FROM workspace_projection.snapshots WHERE generation = $1 AND snapshot_id = $2
            """, token, state.Generation, state.RequireSnapshot()) ??
            throw new InvalidOperationException("Active snapshot is missing."));
}

internal static class AgeGraph
{
    public static Task AddVertexAsync(NpgsqlTransaction tx, string graph, string key, CancellationToken token) =>
        WriteAsync(tx, graph, "CREATE (:Entity { key: $key })", new { key }, token);

    public static Task AddEdgeAsync(NpgsqlTransaction tx, string graph, string key, string from, string to,
        string scope, CancellationToken token) =>
        WriteAsync(tx, graph, """
            MATCH (a:Entity {key: $from}), (b:Entity {key: $to})
            CREATE (a)-[:Relationship {key: $key, scope: $scope}]->(b)
            """, new { key, from, to, scope }, token);

    public static async Task<IReadOnlyList<(string EdgeKey, string NextKey)>> NeighborsAsync(
        NpgsqlTransaction tx, string graph, string at, string scope, string direction, CancellationToken token)
    {
        var pattern = direction switch
        {
            "outgoing" => "(a:Entity {key: $at})-[r:Relationship]->(b:Entity)",
            "incoming" => "(a:Entity {key: $at})<-[r:Relationship]-(b:Entity)",
            "both" => "(a:Entity {key: $at})-[r:Relationship]-(b:Entity)",
            _ => throw new ArgumentOutOfRangeException(nameof(direction))
        };
        await using var command = await PrepareAsync(tx, graph, $"""
            MATCH {pattern}
            WHERE r.scope = $scope
            RETURN r.key, b.key ORDER BY r.key, b.key LIMIT 101
            """, "edge_key ag_catalog.agtype, next_key ag_catalog.agtype",
            "edge_key::text, next_key::text", new { at, scope }, token);
        await using var rows = await command.ExecuteReaderAsync(token);
        var result = new List<(string, string)>();
        while (await rows.ReadAsync(token))
            result.Add((rows.GetString(0), rows.GetString(1)));
        return result;
    }

    private static async Task WriteAsync(NpgsqlTransaction tx, string graph, string cypher, object args,
        CancellationToken token)
    {
        await using var command = await PrepareAsync(tx, graph, cypher, "v ag_catalog.agtype", "v", args, token);
        await command.ExecuteNonQueryAsync(token);
    }

    private static async Task<NpgsqlCommand> PrepareAsync(NpgsqlTransaction tx, string graph, string cypher,
        string columns, string select, object args, CancellationToken token)
    {
        const string prefix = "workspace_g";
        if (!graph.StartsWith(prefix, StringComparison.Ordinal) || graph.Length is < 12 or > 43 ||
            !graph[prefix.Length..].All(ch => ch is >= '0' and <= '9' or >= 'a' and <= 'f'))
            throw new InvalidOperationException("Invalid internal workspace graph name.");
        var command = new NpgsqlCommand($"""
            SELECT {select} FROM ag_catalog.cypher('{graph}', $cypher$ {cypher} $cypher$, $1)
                AS result ({columns})
            """, tx.Connection!, tx);
        try
        {
            command.Parameters.Add(new NpgsqlParameter
            {
                NpgsqlDbType = NpgsqlDbType.Unknown,
                Value = WorkspaceSql.Json(args)
            });
            await command.PrepareAsync(token);
            return command;
        }
        catch
        {
            await command.DisposeAsync();
            throw;
        }
    }
}
