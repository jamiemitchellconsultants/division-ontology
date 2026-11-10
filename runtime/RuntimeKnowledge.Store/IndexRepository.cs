using System.Globalization;
using Npgsql;
using RuntimeKnowledge.Contracts;
using static RuntimeKnowledge.Store.WorkspaceSql;

namespace RuntimeKnowledge.Store;

public sealed class IndexRepository(Db db, TimeProvider? clock = null)
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public async Task SelectAsync(EmbeddingProfile profile, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(profile.Provider) || string.IsNullOrWhiteSpace(profile.Model) ||
            string.IsNullOrWhiteSpace(profile.Revision) || string.IsNullOrWhiteSpace(profile.TextProfile) ||
            profile.Dimensions is < 1 or > 16000)
            throw new WorkspaceException("invalidEmbedding", "Embedding profile is incomplete.");
        await using var connection = await db.OpenAsync(token);
        await using var tx = await connection.BeginTransactionAsync(token);
        await LockHeadAsync(tx, token);
        await ExecuteAsync(tx, """
            INSERT INTO workspace_projection.embedding_profiles (profile_id, body)
            VALUES ($1, $2::jsonb) ON CONFLICT DO NOTHING
            """, token, profile.Id, Json(profile));
        await ExecuteAsync(tx, """
            UPDATE workspace_projection.search_state
               SET desired_profile_id = $1, active_profile_id = coalesce(active_profile_id, $1)
             WHERE singleton AND desired_profile_id IS DISTINCT FROM $1
            """, token, profile.Id);
        await tx.CommitAsync(token);
    }

    public async Task<EmbeddingProfile?> ReadActiveProfileAsync(CancellationToken token)
    {
        await using var connection = await db.OpenAsync(token);
        await using var tx = await connection.BeginTransactionAsync(token);
        return await ReadActiveProfileAsync(tx, token);
    }

    internal static async Task<EmbeddingProfile?> ReadActiveProfileAsync(NpgsqlTransaction tx, CancellationToken token)
    {
        var raw = (string?)await ScalarAsync(tx, """
            SELECT p.body::text FROM workspace_projection.search_state s
            JOIN workspace_projection.embedding_profiles p ON p.profile_id = s.active_profile_id
            WHERE s.singleton
            """, token);
        return raw is null ? null : ReadJson<EmbeddingProfile>(raw);
    }

    public async Task EnqueueAsync(CancellationToken token)
    {
        await using var connection = await db.OpenAsync(token);
        await using var tx = await connection.BeginTransactionAsync(token);
        await ExecuteAsync(tx, """
            WITH selected_profiles AS (
                SELECT active_profile_id AS id FROM workspace_projection.search_state
                UNION SELECT desired_profile_id FROM workspace_projection.search_state
            ), docs AS (
                SELECT p.generation, p.snapshot_id AS scope, p.record_key, 1 AS revision,
                    NULL::uuid AS contribution_id,
                    concat_ws(E'\n', p.body->>'label', nullif(p.body->>'description', '')) AS input_text
                FROM workspace_projection.published_records p
                JOIN workspace_projection.state live USING (generation)
                JOIN workspace_projection.snapshots s
                    ON s.generation = p.generation AND s.snapshot_id = p.snapshot_id
                WHERE p.kind IN ('concept','node') AND (s.index_requested OR p.snapshot_id = live.snapshot_id)
                UNION ALL
                SELECT c.generation, 'annotation', c.contribution_id::text, c.revision,
                    c.contribution_id, c.body #>> '{annotation,text}'
                FROM workspace_projection.contributions c
                JOIN workspace_projection.contribution_index_work w
                  ON w.generation = c.generation AND w.contribution_id = c.contribution_id AND w.revision = c.revision
                JOIN workspace_projection.state live ON live.generation = c.generation
                WHERE c.kind = 'annotation' AND NOT c.removed
            )
            INSERT INTO workspace_projection.embedding_jobs
                (generation, profile_id, scope, record_key, revision, contribution_id, input_text)
            SELECT docs.generation, selected_profiles.id, scope, record_key, revision, contribution_id, input_text
            FROM docs CROSS JOIN selected_profiles WHERE selected_profiles.id IS NOT NULL
            ON CONFLICT DO NOTHING
            """, token);
        await tx.CommitAsync(token);
    }

    public async Task<IReadOnlyList<EmbeddingJob>> ClaimAsync(int limit, CancellationToken token)
    {
        if (limit is < 1 or > 100) throw new ArgumentOutOfRangeException(nameof(limit));
        await using var connection = await db.OpenAsync(token);
        await using var tx = await connection.BeginTransactionAsync(token);
        var now = _clock.GetUtcNow();
        var lease = Guid.NewGuid();
        var jobs = new List<EmbeddingJob>();
        await using (var command = Command(tx, """
            WITH next AS (
                SELECT j.job_id
                FROM workspace_projection.embedding_jobs j
                JOIN workspace_projection.state live USING (generation)
                JOIN workspace_projection.search_state s ON s.singleton
                WHERE j.embedding IS NULL AND j.available_at <= $1
                    AND (j.lease_until IS NULL OR j.lease_until <= $1)
                    AND j.profile_id IN (s.active_profile_id, s.desired_profile_id)
                    AND (j.contribution_id IS NULL OR EXISTS (
                        SELECT FROM workspace_projection.contributions c
                        WHERE c.generation = j.generation AND c.contribution_id = j.contribution_id
                            AND c.revision = j.revision AND NOT c.removed))
                ORDER BY j.job_id LIMIT $2 FOR UPDATE OF j SKIP LOCKED
            ), leased AS (
                UPDATE workspace_projection.embedding_jobs j SET lease_id = $3, lease_until = $4
                FROM next WHERE next.job_id = j.job_id RETURNING j.job_id, j.profile_id, j.input_text
            )
            SELECT leased.job_id, p.body::text, leased.input_text FROM leased
            JOIN workspace_projection.embedding_profiles p USING (profile_id)
            ORDER BY leased.job_id
            """, now, limit, lease, now.AddMinutes(10)))
        await using (var rows = await command.ExecuteReaderAsync(token))
        {
            while (await rows.ReadAsync(token))
                jobs.Add(new EmbeddingJob(rows.GetInt64(0), lease, ReadJson<EmbeddingProfile>(rows.GetString(1)), rows.GetString(2)));
        }
        await tx.CommitAsync(token);
        return jobs;
    }

    public async Task<bool> CompleteAsync(EmbeddingJob job, float[] vector, CancellationToken token)
    {
        job.Profile.ValidateVector(vector);
        await using var connection = await db.OpenAsync(token);
        await using var tx = await connection.BeginTransactionAsync(token);
        var wrote = await ExecuteAsync(tx, """
            UPDATE workspace_projection.embedding_jobs j SET embedding = $1::vector,
                lease_id = NULL, lease_until = NULL
            FROM workspace_projection.state live, workspace_projection.search_state s
            WHERE j.job_id = $2 AND j.lease_id = $3 AND j.profile_id = $4 AND j.lease_until >= $5
              AND j.embedding IS NULL AND j.generation = live.generation
              AND j.profile_id IN (s.active_profile_id, s.desired_profile_id)
              AND (j.contribution_id IS NULL OR EXISTS (
                  SELECT FROM workspace_projection.contributions c WHERE c.generation = j.generation
                    AND c.contribution_id = j.contribution_id AND c.revision = j.revision AND NOT c.removed))
            """, token, VectorText(vector), job.Id, job.Lease, job.Profile.Id, _clock.GetUtcNow());
        await tx.CommitAsync(token);
        return wrote == 1;
    }

    public async Task DeferAsync(EmbeddingJob job, CancellationToken token)
    {
        await using var connection = await db.OpenAsync(token);
        await using var tx = await connection.BeginTransactionAsync(token);
        await ExecuteAsync(tx, """
            UPDATE workspace_projection.embedding_jobs
            SET available_at = $1, lease_id = NULL, lease_until = NULL
            WHERE job_id = $2 AND lease_id = $3 AND embedding IS NULL
            """, token, _clock.GetUtcNow().AddSeconds(30), job.Id, job.Lease);
        await tx.CommitAsync(token);
    }

    public async Task<bool> PromoteIfReadyAsync(CancellationToken token)
    {
        await using var connection = await db.OpenAsync(token);
        await using var tx = await connection.BeginTransactionAsync(token);
        await LockHeadAsync(tx, token);
        var state = await ReadStateAsync(tx, token);
        var desired = (string?)await ScalarAsync(tx, """
            SELECT desired_profile_id FROM workspace_projection.search_state
            WHERE singleton AND desired_profile_id IS DISTINCT FROM active_profile_id
            """, token);
        if (desired is null || state.SnapshotId is null ||
            !await BaselineReadyAsync(tx, state.Generation, state.SnapshotId, desired, token) ||
            await AnnotationIndexPendingAsync(tx, state, desired, token)) return false;
        await ExecuteAsync(tx, """
            UPDATE workspace_projection.search_state SET active_profile_id = desired_profile_id WHERE singleton
            """, token);
        await tx.CommitAsync(token);
        return true;
    }

    internal static async Task<bool> BaselineReadyAsync(NpgsqlTransaction tx, Guid generation, string snapshot,
        string profile, CancellationToken token) =>
        await ScalarAsync(tx, """
            SELECT NOT EXISTS (
                SELECT FROM workspace_projection.published_records p
                WHERE p.generation = $1 AND p.snapshot_id = $2 AND p.kind IN ('concept','node')
                AND NOT EXISTS (SELECT FROM workspace_projection.embedding_jobs j
                    WHERE j.generation = p.generation AND j.scope = p.snapshot_id AND j.record_key = p.record_key
                        AND j.profile_id = $3 AND j.embedding IS NOT NULL))
            """, token, generation, snapshot, profile) is true;

    internal static async Task<bool> AnnotationIndexPendingAsync(NpgsqlTransaction tx, ProjectionState state,
        string profile, CancellationToken token) =>
        await ScalarAsync(tx, """
            SELECT EXISTS (
                SELECT FROM workspace_projection.contributions c
                JOIN workspace_projection.published_records p ON p.generation = c.generation
                    AND p.snapshot_id = $2 AND p.record_key = c.target_key
                WHERE c.generation = $1 AND c.kind = 'annotation' AND NOT c.removed
                  AND NOT EXISTS (SELECT FROM workspace_projection.embedding_jobs j WHERE j.generation = c.generation
                    AND j.contribution_id = c.contribution_id AND j.revision = c.revision
                    AND j.profile_id = $3 AND j.embedding IS NOT NULL))
            """, token, state.Generation, state.RequireSnapshot(), profile) is true;

    internal static string VectorText(float[] vector) =>
        "[" + string.Join(",", vector.Select(value => value.ToString("R", CultureInfo.InvariantCulture))) + "]";
}
