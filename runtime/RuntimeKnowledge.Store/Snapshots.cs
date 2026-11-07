using RuntimeKnowledge.Contracts;
using Npgsql;
using static RuntimeKnowledge.Store.WorkspaceSql;

namespace RuntimeKnowledge.Store;

public sealed class SnapshotStore(Db db, IBundleObjects objects, TimeProvider? clock = null)
{
    private readonly BundleReader _reader = new(objects);
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public async Task<SnapshotCandidate> StageAsync(string manifestId, CancellationToken cancellationToken)
    {
        var bundle = await _reader.ReadAsync(manifestId, cancellationToken);
        await using var connection = await db.OpenAsync(cancellationToken);
        await using var tx = await connection.BeginTransactionAsync(cancellationToken);
        var state = await ReadStateAsync(tx, cancellationToken);
        await StageProjectionAsync(tx, state, bundle, cancellationToken);
        await ExecuteAsync(tx, """
            UPDATE workspace_projection.snapshots SET index_requested = true
            WHERE generation = $1 AND snapshot_id = $2
            """, cancellationToken, state.Generation, manifestId);
        await tx.CommitAsync(cancellationToken);
        return new SnapshotCandidate(manifestId, state.SnapshotId == manifestId ? "active" : "staged");
    }

    public async Task<long> ActivateAsync(string manifestId, CancellationToken cancellationToken)
    {
        if (!BundleReader.Hex(manifestId, 64)) throw new WorkspaceException("invalidBundle", "Invalid snapshot digest.");
        await using var connection = await db.OpenAsync(cancellationToken);
        await using var tx = await connection.BeginTransactionAsync(cancellationToken);
        await LockHeadAsync(tx, cancellationToken);
        var state = await ReadStateAsync(tx, cancellationToken);
        if (state.WritesPaused) throw new WorkspaceException("writesPaused", "Workspace writes are paused for rebuild.");
        if (state.SnapshotId == manifestId)
            return state.ActivationPosition ?? throw new InvalidOperationException("Activation position missing.");
        if (await ScalarAsync(tx, """
            SELECT EXISTS (SELECT FROM workspace_projection.snapshots WHERE generation = $1 AND snapshot_id = $2)
            """, cancellationToken, state.Generation, manifestId) is not true)
            throw new WorkspaceException("snapshotNotStaged", "Stage and validate the snapshot before activation.");

        var profile = await IndexRepository.ReadActiveProfileAsync(tx, cancellationToken);
        if (profile is not null && !await IndexRepository.BaselineReadyAsync(tx, state.Generation, manifestId,
            profile.Id, cancellationToken))
            throw new WorkspaceException("snapshotIndexPending", "The staged snapshot is waiting for its baseline search index.");

        var position = await AppendAsync(tx, "snapshotActivated", manifestId, null, null, null,
            _clock.GetUtcNow(), new { manifestId }, cancellationToken);
        await ExecuteAsync(tx, """
            UPDATE workspace_projection.state SET snapshot_id = $1, activation_position = $2 WHERE singleton
            """, cancellationToken, manifestId, position);
        await ExecuteAsync(tx, """
            UPDATE workspace_projection.snapshots SET index_requested = false
            WHERE generation = $1 AND snapshot_id = $2
            """, cancellationToken, state.Generation, manifestId);
        await tx.CommitAsync(cancellationToken);
        return position;
    }

    internal static async Task StageProjectionAsync(NpgsqlTransaction tx, ProjectionState state,
        VerifiedBundle bundle, CancellationToken token)
    {
        var snapshotId = bundle.SnapshotId;
        if (await ExecuteAsync(tx, """
            INSERT INTO workspace_projection.snapshots (generation, snapshot_id, manifest, gaps)
                VALUES ($1, $2, $3::jsonb, $4::jsonb) ON CONFLICT DO NOTHING
            """, token, state.Generation, snapshotId, Json(bundle.Manifest), Json(bundle.Projection.Gaps)) == 0)
            return;

        foreach (var entity in bundle.Projection.Entities.OrderBy(row => row.Key.StorageKey, StringComparer.Ordinal))
        {
            await AddRecordAsync(tx, state, snapshotId, entity.Key,
                entity with { Provenance = entity.Provenance with { SnapshotId = snapshotId } }, token);
            await EnsureVertexAsync(tx, state, entity.Key, token);
        }
        foreach (var type in bundle.Projection.RelationshipTypes.OrderBy(row => row.Key.StorageKey, StringComparer.Ordinal))
        {
            await AddRecordAsync(tx, state, snapshotId, type.Key,
                type with { Provenance = type.Provenance with { SnapshotId = snapshotId } }, token);
            await EnsureVertexAsync(tx, state, type.Key, token);
        }
        foreach (var edge in bundle.Projection.Edges)
        {
            await AddRecordAsync(tx, state, snapshotId, edge.Key,
                edge with { Provenance = edge.Provenance with { SnapshotId = snapshotId } }, token);
            await AgeGraph.AddEdgeAsync(tx, state.GraphName, edge.Key.StorageKey, edge.From.StorageKey,
                edge.To.StorageKey, snapshotId, token);
        }
    }

    private static Task<int> AddRecordAsync<T>(NpgsqlTransaction tx, ProjectionState state, string snapshotId,
        PublishedKey key, T value, CancellationToken token) =>
        ExecuteAsync(tx, """
            INSERT INTO workspace_projection.published_records (generation, snapshot_id, record_key, kind, body)
                VALUES ($1, $2, $3, $4, $5::jsonb)
            """, token, state.Generation, snapshotId, key.StorageKey, key.Kind, Json(value));

    private static async Task EnsureVertexAsync(NpgsqlTransaction tx, ProjectionState state,
        PublishedKey key, CancellationToken token)
    {
        if (await ExecuteAsync(tx, """
            INSERT INTO workspace_projection.vertices (generation, record_key) VALUES ($1, $2)
                ON CONFLICT DO NOTHING
            """, token, state.Generation, key.StorageKey) == 1)
            await AgeGraph.AddVertexAsync(tx, state.GraphName, key.StorageKey, token);
    }
}
