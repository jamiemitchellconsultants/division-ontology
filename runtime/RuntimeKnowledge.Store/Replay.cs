using System.Data;
using System.Security.Cryptography;
using System.Text;
using Npgsql;
using RuntimeKnowledge.Contracts;
using static RuntimeKnowledge.Store.WorkspaceSql;

namespace RuntimeKnowledge.Store;

public sealed record RebuildReport(Guid Generation, long ThroughPosition, string SnapshotId, string StateDigest);

public sealed class ReplayStore(Db db, IBundleObjects objects)
{
    private readonly BundleReader _reader = new(objects);

    public async Task<RebuildReport> DescribeCurrentAsync(CancellationToken token)
    {
        await using var connection = await db.OpenAsync(token);
        await using var tx = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, token);
        var state = await ReadStateAsync(tx, token);
        var head = (long)(await ScalarAsync(tx, "SELECT position FROM workspace_journal.head WHERE singleton", token)
            ?? throw new InvalidOperationException("Journal head is missing."));
        return new RebuildReport(state.Generation, head, state.RequireSnapshot(),
            await DigestAsync(tx, state, head, token));
    }

    public async Task<RebuildReport> RebuildAsync(long throughPosition, CancellationToken token)
    {
        if (throughPosition < 1) throw InvalidJournal("A rebuild requires a published journal prefix.");
        await using var connection = await db.OpenAsync(token);
        await using var tx = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, token);
        var events = await ReadPrefixAsync(tx, throughPosition, token);
        var generation = Guid.NewGuid();
        var graphName = $"workspace_g{generation:N}";
        var candidate = new ProjectionState(generation, graphName, null, null, false);
        await CreateGraphAsync(tx, graphName, token);

        foreach (var entry in events)
        {
            if (entry.Type == "snapshotActivated")
            {
                var activation = ReadJson<SnapshotActivation>(entry.Payload);
                if (entry.SnapshotId is null || activation.ManifestId != entry.SnapshotId ||
                    entry.ContributionId is not null || entry.Actor is not null)
                    throw InvalidJournal("Inconsistent snapshot activation record.");
                if (await ScalarAsync(tx, """
                    SELECT EXISTS (SELECT FROM workspace_projection.snapshots
                                   WHERE generation = $1 AND snapshot_id = $2)
                    """, token, candidate.Generation, entry.SnapshotId) is not true)
                {
                    var bundle = await _reader.ReadAsync(entry.SnapshotId, token);
                    await SnapshotStore.StageProjectionAsync(tx, candidate, bundle, token);
                }
                candidate = candidate with { SnapshotId = entry.SnapshotId, ActivationPosition = entry.Position };
                continue;
            }

            if (candidate.SnapshotId is null || candidate.SnapshotId != entry.SnapshotId || entry.Actor is null)
                throw InvalidJournal("Contribution lacks its recorded active snapshot or actor.");
            if (entry.Type is AssessmentStore.Saved or AssessmentStore.Revised)
            {
                if (entry.ContributionId is not { } assessmentId || entry.Revision is not { } version)
                    throw InvalidJournal("Assessment event lacks its aggregate.");
                await AssessmentStore.ApplyRecordedAsync(tx, candidate, entry.Type, assessmentId, version, entry.Actor,
                    entry.OccurredAt, entry.SnapshotId!, ReadJson<AssessmentEvent>(entry.Payload), token);
                continue;
            }
            var change = ReadJson<ContributionChange>(entry.Payload);
            await CheckChangeAsync(tx, candidate, entry, change, token);
            await ContributionStore.ApplyRecordedAsync(tx, candidate, change, token);
        }

        var snapshot = candidate.RequireSnapshot();
        await ExecuteAsync(tx, """
            DELETE FROM workspace_projection.contribution_index_work w
            USING workspace_projection.contributions c
            WHERE w.generation = $1 AND c.generation = w.generation AND c.contribution_id = w.contribution_id
              AND (c.removed OR c.revision <> w.revision)
            """, token, candidate.Generation);
        await ExecuteAsync(tx, """
            UPDATE workspace_projection.snapshots SET index_requested = false WHERE generation = $1
            """, token, candidate.Generation);
        var digest = await DigestAsync(tx, candidate, throughPosition, token);
        await ExecuteAsync(tx, """
            INSERT INTO workspace_projection.rebuilds
                (generation, graph_name, through_position, snapshot_id, activation_position, state_digest)
            VALUES ($1, $2, $3, $4, $5, $6)
            """, token, generation, graphName, throughPosition, snapshot, candidate.ActivationPosition, digest);
        await tx.CommitAsync(token);
        return new RebuildReport(generation, throughPosition, snapshot, digest);
    }

    public async Task SetWritesPausedAsync(bool paused, CancellationToken token)
    {
        await using var connection = await db.OpenAsync(token);
        await using var tx = await connection.BeginTransactionAsync(token);
        await LockHeadAsync(tx, token);
        await ExecuteAsync(tx, "UPDATE workspace_projection.state SET writes_paused = $1 WHERE singleton", token, paused);
        await tx.CommitAsync(token);
    }

    public async Task CutoverAsync(Guid generation, long expectedJournalHead, CancellationToken token)
    {
        await using var connection = await db.OpenAsync(token);
        await using var tx = await connection.BeginTransactionAsync(token);
        var head = await LockHeadAsync(tx, token);
        var live = await ReadStateAsync(tx, token);
        if (!live.WritesPaused)
            throw new WorkspaceException("writesNotPaused", "Pause writes before switching to rebuilt projections.");
        if (head != expectedJournalHead)
            throw new WorkspaceException("rebuildStale", "The journal advanced. Rebuild through the current position.");

        ProjectionState rebuilt;
        string expectedDigest;
        await using (var command = Command(tx, """
            SELECT graph_name, snapshot_id, activation_position, state_digest
              FROM workspace_projection.rebuilds WHERE generation = $1 AND through_position = $2
            """, generation, head))
        await using (var row = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, token))
        {
            if (!await row.ReadAsync(token))
                throw new WorkspaceException("rebuildStale", "No verified rebuild exists at this position.");
            rebuilt = new ProjectionState(generation, row.GetString(0), row.GetString(1), row.GetInt64(2), true);
            expectedDigest = row.GetString(3);
        }

        if (await DigestAsync(tx, rebuilt, head, token) != expectedDigest ||
            await DigestAsync(tx, live, head, token) != expectedDigest)
            throw new WorkspaceException("rebuildMismatch", "The rebuilt and live projection checksums differ.");

        await ExecuteAsync(tx, """
            UPDATE workspace_projection.state
               SET generation = $1, graph_name = $2, snapshot_id = $3, activation_position = $4
             WHERE singleton
            """, token, generation, rebuilt.GraphName, rebuilt.SnapshotId, rebuilt.ActivationPosition);
        await tx.CommitAsync(token);
    }

    private static async Task<IReadOnlyList<ReplayEntry>> ReadPrefixAsync(NpgsqlTransaction tx, long through,
        CancellationToken token)
    {
        await using var command = Command(tx, """
            SELECT position, event_id, event_type, schema_version, actor_issuer, actor_subject,
                   snapshot_id, aggregate_id, aggregate_revision, payload::text, occurred_at
            FROM workspace_journal.events WHERE position <= $1 ORDER BY position
            """, through);
        await using var rows = await command.ExecuteReaderAsync(token);
        var entries = new List<ReplayEntry>();
        var seen = new HashSet<Guid>();
        long expected = 1;
        while (await rows.ReadAsync(token))
        {
            var position = rows.GetInt64(0);
            if (position != expected++ || !seen.Add(rows.GetGuid(1)) || rows.GetInt32(3) != 1)
                throw InvalidJournal("Journal positions, event IDs or event schema versions are inconsistent.");
            entries.Add(new ReplayEntry(position, rows.GetString(2),
                rows.IsDBNull(4) ? null : new Actor(rows.GetString(4), rows.GetString(5)),
                rows.IsDBNull(6) ? null : rows.GetString(6), rows.IsDBNull(7) ? null : rows.GetGuid(7),
                rows.IsDBNull(8) ? null : rows.GetInt32(8), rows.GetString(9), rows.GetFieldValue<DateTimeOffset>(10)));
        }
        if (expected - 1 != through) throw InvalidJournal("The requested journal prefix is incomplete.");
        return entries;
    }

    private static async Task CheckChangeAsync(NpgsqlTransaction tx, ProjectionState state, ReplayEntry entry,
        ContributionChange change, CancellationToken token)
    {
        var after = change.After;
        if (after is null || after.Id != entry.ContributionId || after.Version != entry.Revision)
            throw InvalidJournal("Contribution event does not match its journal identity.");
        var validType = entry.Type switch
        {
            "annotationCreated" or "annotationEdited" or "annotationRemoved" => after.Annotation is not null &&
                after.Relationship is null && after.Kind == "annotation",
            "relationshipCreated" or "relationshipEdited" or "relationshipRemoved" or "codificationAssociated" =>
                after.Relationship is not null && after.Annotation is null && after.Kind == "relationship",
            _ => false
        };
        var current = await ContributionStore.FindAsync(tx, state, after.Id, token);
        var creation = entry.Type.EndsWith("Created", StringComparison.Ordinal);
        if (!validType || (current is null) != creation || current?.Removed == true ||
            Json(current) != Json(change.Before) || after.Version != (current?.Version ?? 0) + 1 ||
            after.Removed != entry.Type.EndsWith("Removed", StringComparison.Ordinal))
            throw InvalidJournal("Contribution event does not follow the previous recorded version.");
    }

    private static async Task CreateGraphAsync(NpgsqlTransaction tx, string graph, CancellationToken token)
    {
        await ExecuteAsync(tx, "SELECT ag_catalog.create_graph($1::name)", token, graph);
        // The name is generated locally from a UUID and contains only [a-z0-9_].
        // AGE 1.6 label creation takes cstring literals rather than PostgreSQL name parameters.
        await ExecuteAsync(tx, $"""
            SELECT ag_catalog.create_vlabel('{graph}', 'Entity');
            SELECT ag_catalog.create_elabel('{graph}', 'Relationship');
            GRANT USAGE ON SCHEMA {graph} TO workspace_api, workspace_publisher;
            GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA {graph} TO workspace_api, workspace_publisher;
            GRANT USAGE ON ALL SEQUENCES IN SCHEMA {graph} TO workspace_api, workspace_publisher;
            """, token);
    }

    private static async Task<string> DigestAsync(NpgsqlTransaction tx, ProjectionState state,
        long position, CancellationToken token)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        void Add(string value) => hash.AppendData(Encoding.UTF8.GetBytes(Json(value) + "\n"));
        var snapshot = state.RequireSnapshot();
        Add(snapshot);
        Add(position.ToString(System.Globalization.CultureInfo.InvariantCulture));
        await AddRowsAsync("""
            SELECT body::text FROM workspace_projection.published_records
              WHERE generation = $1 AND snapshot_id = $2 ORDER BY record_key
            """, state.Generation, snapshot);
        await AddRowsAsync("""
            SELECT body::text FROM workspace_projection.contributions
              WHERE generation = $1 ORDER BY contribution_id
            """, state.Generation);
        await AddRowsAsync("""
            SELECT to_jsonb(e)::text FROM workspace_journal.events e WHERE position <= $1 ORDER BY position
            """, position);
        await AddRowsAsync("""
            SELECT package_sha256 FROM workspace_projection.impact_models
              WHERE generation = $1 ORDER BY snapshot_id
            """, state.Generation);
        await AddRowsAsync("""
            SELECT concat_ws(' ', assessment_id, version, snapshot_id,
                             encode(sha256(frozen), 'hex'), encode(sha256(markdown), 'hex'))
              FROM workspace_projection.assessments WHERE generation = $1 ORDER BY assessment_id
            """, state.Generation);
        await AddRowsAsync("""
            SELECT concat_ws(' ', assessment_id, version, encode(sha256(frozen), 'hex'), encode(sha256(markdown), 'hex'))
              FROM workspace_projection.assessment_revisions WHERE generation = $1 ORDER BY assessment_id, version
            """, state.Generation);
        foreach (var edge in await AgeGraph.ReadActiveEdgesAsync(tx, state.GraphName, snapshot, token))
            Add(edge);
        return Convert.ToHexStringLower(hash.GetHashAndReset());

        async Task AddRowsAsync(string sql, params object?[] args)
        {
            await using var command = Command(tx, sql, args);
            await using var rows = await command.ExecuteReaderAsync(token);
            while (await rows.ReadAsync(token)) Add(rows.GetString(0));
        }
    }

    private static WorkspaceException InvalidJournal(string message) => new("invalidJournal", message);
    private sealed record SnapshotActivation(string ManifestId);
    private sealed record ReplayEntry(long Position, string Type, Actor? Actor, string? SnapshotId,
        Guid? ContributionId, int? Revision, string Payload, DateTimeOffset OccurredAt);
}
