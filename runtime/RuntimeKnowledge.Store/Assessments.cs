using System.Data;
using System.Text;
using System.Text.Json.Nodes;
using Npgsql;
using RuntimeKnowledge.Contracts;
using static RuntimeKnowledge.Store.WorkspaceSql;

namespace RuntimeKnowledge.Store;

/// <summary>Saved impact assessments: journal events carrying their frozen content and Markdown (ADR 0033).
/// Content is stored as the exact UTF-8 bytes it was written with, because jsonb would reorder keys (Q5).</summary>
public sealed class AssessmentStore(Db db, TimeProvider? clock = null)
{
    public const string Saved = "assessmentSaved";
    public const string Revised = "assessmentRevised";

    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    /// <summary>Test hook, awaited just before commit, to prove a save is atomic (Q5).</summary>
    internal Func<Task>? BeforeCommit { get; set; }

    public async Task<AssessmentSaved> SaveAsync(Actor actor, string expectedSnapshot, string requestId,
        string frozenJson, string markdown, Guid? startedFrom, CancellationToken token)
    {
        RequireActor(actor);
        RefuseApproval(frozenJson);
        await using var connection = await db.OpenAsync(token);
        await using var tx = await connection.BeginTransactionAsync(token);
        await LockHeadAsync(tx, token);
        var state = await ReadWritableAsync(tx, token);
        var snapshot = state.RequireSnapshot();
        if (snapshot != expectedSnapshot) throw SnapshotChanged();
        if (startedFrom is { } earlier && await ScalarAsync(tx, """
            SELECT EXISTS (SELECT FROM workspace_projection.assessments WHERE generation = $1 AND assessment_id = $2)
            """, token, state.Generation, earlier) is not true)
            throw new WorkspaceException("assessmentNotFound", "The earlier assessment was not found.");

        var id = Guid.NewGuid();
        var now = Now();
        var payload = new AssessmentEvent(id, 1, requestId, frozenJson, markdown, startedFrom);
        var position = await AppendAsync(tx, Saved, snapshot, id, 1, actor, now, payload, token);
        await ApplyRecordedAsync(tx, state, Saved, id, 1, actor, now, snapshot, payload, token);
        if (BeforeCommit is { } hook) await hook();
        await tx.CommitAsync(token);
        return new AssessmentSaved(id, 1, position);
    }

    public async Task<AssessmentSaved> ReviseAsync(Actor actor, Guid id, int expectedVersion, string expectedSnapshot,
        string frozenJson, string markdown, CancellationToken token)
    {
        RequireActor(actor);
        RefuseApproval(frozenJson);
        await using var connection = await db.OpenAsync(token);
        await using var tx = await connection.BeginTransactionAsync(token);
        await LockHeadAsync(tx, token);
        var state = await ReadWritableAsync(tx, token);
        var active = state.RequireSnapshot();
        await using (var read = Command(tx, """
            SELECT version, snapshot_id, request_id FROM workspace_projection.assessments
              WHERE generation = $1 AND assessment_id = $2 FOR UPDATE
            """, state.Generation, id))
        await using (var row = await read.ExecuteReaderAsync(token))
        {
            if (!await row.ReadAsync(token))
                throw new WorkspaceException("assessmentNotFound", "The assessment was not found.");
            var (version, snapshot, requestId) = (row.GetInt32(0), row.GetString(1), row.GetString(2));
            await row.CloseAsync();
            if (version != expectedVersion)
                throw new WorkspaceException("versionConflict", "The assessment changed since you opened it. Reload it before editing.");
            if (snapshot != active)
                throw new WorkspaceException("snapshotMismatch",
                    "This assessment is pinned to an earlier snapshot and cannot be edited. Start a new assessment from it.");
            if (active != expectedSnapshot) throw SnapshotChanged();

            var now = Now();
            var payload = new AssessmentEvent(id, version + 1, requestId, frozenJson, markdown, null);
            var position = await AppendAsync(tx, Revised, active, id, version + 1, actor, now, payload, token);
            await ApplyRecordedAsync(tx, state, Revised, id, version + 1, actor, now, active, payload, token);
            if (BeforeCommit is { } hook) await hook();
            await tx.CommitAsync(token);
            return new AssessmentSaved(id, version + 1, position);
        }
    }

    public async Task<SavedAssessment?> GetAsync(Guid id, CancellationToken token)
    {
        await using var connection = await db.OpenAsync(token);
        await using var tx = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, token);
        var state = await ReadStateAsync(tx, token);
        SavedAssessment? found;
        await using (var command = Command(tx, """
            SELECT version, snapshot_id, request_id, author_issuer, author_subject, saved_at, updated_at,
                   frozen, markdown, started_from
              FROM workspace_projection.assessments WHERE generation = $1 AND assessment_id = $2
            """, state.Generation, id))
        await using (var row = await command.ExecuteReaderAsync(token))
        {
            if (!await row.ReadAsync(token)) return null;
            var snapshot = row.GetString(1);
            found = new SavedAssessment(id, row.GetInt32(0), snapshot, row.GetString(2),
                new Actor(row.GetString(3), row.GetString(4)), row.GetFieldValue<DateTimeOffset>(5), row.GetFieldValue<DateTimeOffset>(6),
                snapshot != state.SnapshotId, row.IsDBNull(9) ? null : row.GetGuid(9),
                Parse(row.GetFieldValue<byte[]>(7)), Encoding.UTF8.GetString(row.GetFieldValue<byte[]>(8)), []);
        }

        var revisions = new List<AssessmentRevision>();
        await using (var command = Command(tx, """
            SELECT version, editor_issuer, editor_subject, edited_at, frozen, markdown
              FROM workspace_projection.assessment_revisions
              WHERE generation = $1 AND assessment_id = $2 ORDER BY version
            """, state.Generation, id))
        await using (var rows = await command.ExecuteReaderAsync(token))
            while (await rows.ReadAsync(token))
                revisions.Add(new AssessmentRevision(rows.GetInt32(0), new Actor(rows.GetString(1), rows.GetString(2)),
                    rows.GetFieldValue<DateTimeOffset>(3), Parse(rows.GetFieldValue<byte[]>(4)),
                    Encoding.UTF8.GetString(rows.GetFieldValue<byte[]>(5))));
        return found with { Revisions = revisions };
    }

    public async Task<byte[]?> MarkdownAsync(Guid id, CancellationToken token)
    {
        await using var connection = await db.OpenAsync(token);
        await using var tx = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, token);
        var state = await ReadStateAsync(tx, token);
        return await ScalarAsync(tx, """
            SELECT markdown FROM workspace_projection.assessments WHERE generation = $1 AND assessment_id = $2
            """, token, state.Generation, id) as byte[];
    }

    /// <summary>Projects a recorded assessment event, live or during replay, copying its bytes exactly.</summary>
    internal static async Task ApplyRecordedAsync(NpgsqlTransaction tx, ProjectionState state, string type,
        Guid id, int version, Actor actor, DateTimeOffset at, string snapshotId, AssessmentEvent payload, CancellationToken token)
    {
        if (payload.AssessmentId != id || payload.Version != version)
            throw new WorkspaceException("invalidJournal", "Assessment event payload differs from its record.");
        var frozen = Encoding.UTF8.GetBytes(payload.Frozen);
        var markdown = Encoding.UTF8.GetBytes(payload.Markdown);
        if (type == Saved)
            await ExecuteAsync(tx, """
                INSERT INTO workspace_projection.assessments
                    (generation, assessment_id, version, snapshot_id, request_id, author_issuer, author_subject,
                     saved_at, updated_at, frozen, markdown, started_from)
                VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $8, $9, $10, $11)
                """, token, state.Generation, id, version, snapshotId, payload.RequestId, actor.Issuer, actor.Subject,
                at, frozen, markdown, payload.StartedFrom);
        else if (await ExecuteAsync(tx, """
                UPDATE workspace_projection.assessments
                   SET version = $3, updated_at = $4, frozen = $5, markdown = $6
                 WHERE generation = $1 AND assessment_id = $2 AND version = $3 - 1
                """, token, state.Generation, id, version, at, frozen, markdown) != 1)
            throw new WorkspaceException("invalidJournal", "Assessment revision does not follow its previous version.");

        await ExecuteAsync(tx, """
            INSERT INTO workspace_projection.assessment_revisions
                (generation, assessment_id, version, editor_issuer, editor_subject, edited_at, frozen, markdown)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8)
            """, token, state.Generation, id, version, actor.Issuer, actor.Subject, at, frozen, markdown);
    }

    private static async Task<ProjectionState> ReadWritableAsync(NpgsqlTransaction tx, CancellationToken token)
    {
        var state = await ReadStateAsync(tx, token);
        if (state.WritesPaused) throw new WorkspaceException("writesPaused", "Workspace writes are paused for rebuild.");
        return state;
    }

    // The journal stores microseconds; truncating here keeps live rows equal to replayed ones.
    private DateTimeOffset Now()
    {
        var now = _clock.GetUtcNow();
        return now.AddTicks(-(now.Ticks % 10));
    }

    private static void RequireActor(Actor actor)
    {
        if (string.IsNullOrWhiteSpace(actor?.Issuer) || string.IsNullOrWhiteSpace(actor.Subject))
            throw new WorkspaceException("actorRequired", "A verified acting identity is required.");
    }

    // B31: a second guard behind the engine's. The workspace records assessments; it never approves them.
    private static void RefuseApproval(string frozenJson)
    {
        JsonObject? record;
        try { record = JsonNode.Parse(frozenJson)?["record"] as JsonObject; }
        catch (System.Text.Json.JsonException) { throw new WorkspaceException("invalidAssessment", "The assessment content is not valid JSON."); }
        if (record is not null && record.Select(p => p.Key).Any(k =>
                k is "approved" or "approvalStatus" || k.Contains("signoff", StringComparison.OrdinalIgnoreCase)))
            throw new WorkspaceException("approvalNotSupported", "Assessments record impact; approval and sign-off are not supported.");
    }

    private static JsonObject Parse(byte[] bytes) => JsonNode.Parse(bytes)!.AsObject();

    private static WorkspaceException SnapshotChanged() =>
        new("snapshotChanged", "The workspace snapshot changed. Start again from a new search.");
}

internal sealed record AssessmentEvent(Guid AssessmentId, int Version, string RequestId, string Frozen, string Markdown, Guid? StartedFrom);
