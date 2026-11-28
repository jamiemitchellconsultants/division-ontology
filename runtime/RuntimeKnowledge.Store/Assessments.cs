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
            SELECT version, snapshot_id, request_id, started_from FROM workspace_projection.assessments
              WHERE generation = $1 AND assessment_id = $2 FOR UPDATE
            """, state.Generation, id))
        await using (var row = await read.ExecuteReaderAsync(token))
        {
            if (!await row.ReadAsync(token))
                throw new WorkspaceException("assessmentNotFound", "The assessment was not found.");
            var (version, snapshot, requestId) = (row.GetInt32(0), row.GetString(1), row.GetString(2));
            Guid? startedFrom = row.IsDBNull(3) ? null : row.GetGuid(3);
            await row.CloseAsync();
            RequireSameIdentity(frozenJson, requestId, startedFrom);
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
                Parse(row.GetFieldValue<byte[]>(7)), Encoding.UTF8.GetString(row.GetFieldValue<byte[]>(8)));
        }
        return found;
    }

    /// <summary>An assessment's revisions, oldest first, in keyset pages of metadata only (B22). Null when it is unknown.</summary>
    public async Task<AssessmentRevisionPage?> RevisionsAsync(Guid id, string? cursor, int limit, CancellationToken token)
    {
        if (limit is < 1 or > 50) throw new WorkspaceException("invalidLimit", "Revision list limit must be from 1 to 50.");
        var after = ReadRevisionCursor(cursor, id);
        await using var connection = await db.OpenAsync(token);
        await using var tx = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, token);
        var state = await ReadStateAsync(tx, token);
        if (await ScalarAsync(tx, """
            SELECT EXISTS (SELECT FROM workspace_projection.assessments WHERE generation = $1 AND assessment_id = $2)
            """, token, state.Generation, id) is not true) return null;
        var rows = new List<AssessmentRevisionSummary>();
        await using (var command = Command(tx, """
            SELECT version, editor_issuer, editor_subject, edited_at FROM workspace_projection.assessment_revisions
             WHERE generation = $1 AND assessment_id = $2 AND version > $3 ORDER BY version LIMIT $4
            """, state.Generation, id, after, limit + 1))
        await using (var reader = await command.ExecuteReaderAsync(token))
            while (await reader.ReadAsync(token))
                rows.Add(new AssessmentRevisionSummary(reader.GetInt32(0), new Actor(reader.GetString(1), reader.GetString(2)),
                    reader.GetFieldValue<DateTimeOffset>(3)));
        var more = rows.Count > limit;
        if (more) rows.RemoveAt(rows.Count - 1);
        return new AssessmentRevisionPage(rows, more ? WriteCursor(new JsonObject { ["v"] = 1, ["id"] = id.ToString("D"), ["after"] = rows[^1].Version }) : null);
    }

    /// <summary>One revision with its frozen content and Markdown, exactly as saved.</summary>
    public async Task<AssessmentRevision?> RevisionAsync(Guid id, int version, CancellationToken token)
    {
        await using var connection = await db.OpenAsync(token);
        await using var tx = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, token);
        var state = await ReadStateAsync(tx, token);
        await using var command = Command(tx, """
            SELECT editor_issuer, editor_subject, edited_at, frozen, markdown FROM workspace_projection.assessment_revisions
             WHERE generation = $1 AND assessment_id = $2 AND version = $3
            """, state.Generation, id, version);
        await using var row = await command.ExecuteReaderAsync(token);
        if (!await row.ReadAsync(token)) return null;
        return new AssessmentRevision(version, new Actor(row.GetString(0), row.GetString(1)), row.GetFieldValue<DateTimeOffset>(2),
            Parse(row.GetFieldValue<byte[]>(3)), Encoding.UTF8.GetString(row.GetFieldValue<byte[]>(4)));
    }

    /// <summary>Whether a saved assessment is pinned to an earlier snapshot, without reading its content. Null when unknown.</summary>
    public async Task<bool?> PinnedToEarlierSnapshotAsync(Guid id, CancellationToken token)
    {
        await using var connection = await db.OpenAsync(token);
        await using var tx = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, token);
        var state = await ReadStateAsync(tx, token);
        return await ScalarAsync(tx, """
            SELECT snapshot_id FROM workspace_projection.assessments WHERE generation = $1 AND assessment_id = $2
            """, token, state.Generation, id) is string snapshot ? snapshot != state.SnapshotId : null;
    }

    private static string WriteCursor(JsonObject body) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(body.ToJsonString())).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static JsonObject ReadCursorBody(string cursor)
    {
        var base64 = cursor.Replace('-', '+').Replace('_', '/');
        base64 = base64.PadRight(base64.Length + (4 - base64.Length % 4) % 4, '=');
        var body = JsonNode.Parse(Convert.FromBase64String(base64))!.AsObject();
        if (body["v"]!.GetValue<int>() != 1) throw new FormatException();
        return body;
    }

    private static int ReadRevisionCursor(string? cursor, Guid id)
    {
        if (cursor is null) return 0;
        try
        {
            var body = ReadCursorBody(cursor);
            if (Guid.Parse(body["id"]!.GetValue<string>()) != id) throw new FormatException();
            return body["after"]!.GetValue<int>();
        }
        catch (Exception ex) when (ex is FormatException or System.Text.Json.JsonException or InvalidOperationException
                                       or NullReferenceException or ArgumentException)
        {
            throw new WorkspaceException("invalidCursor", "The cursor is not valid for this list. Start again from the first page.");
        }
    }

    /// <summary>Saved assessments, newest first, in keyset pages (B21). Equal save times are ordered by id.</summary>
    public async Task<AssessmentPage> ListAsync(string? cursor, int limit, CancellationToken token)
    {
        if (limit is < 1 or > 50) throw new WorkspaceException("invalidLimit", "Assessment list limit must be from 1 to 50.");
        var after = ReadListCursor(cursor);
        await using var connection = await db.OpenAsync(token);
        await using var tx = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, token);
        var state = await ReadStateAsync(tx, token);
        var active = state.RequireSnapshot();
        await using var command = Command(tx, """
            SELECT assessment_id, request_id, author_issuer, author_subject, saved_at, snapshot_id
              FROM workspace_projection.assessments
             WHERE generation = $1 AND ($2::timestamptz IS NULL OR (saved_at, assessment_id) < ($2, $3))
             ORDER BY saved_at DESC, assessment_id DESC LIMIT $4
            """, state.Generation, after?.SavedAt, after?.Id ?? Guid.Empty, limit + 1);
        var rows = new List<AssessmentSummary>();
        await using (var reader = await command.ExecuteReaderAsync(token))
            while (await reader.ReadAsync(token))
            {
                var snapshot = reader.GetString(5);
                rows.Add(new AssessmentSummary(reader.GetGuid(0), reader.GetString(1), new Actor(reader.GetString(2), reader.GetString(3)),
                    reader.GetFieldValue<DateTimeOffset>(4), snapshot, snapshot != active));
            }
        var more = rows.Count > limit;
        if (more) rows.RemoveAt(rows.Count - 1);
        return new AssessmentPage(rows, more ? WriteListCursor(rows[^1]) : null);
    }

    private sealed record ListCursor(DateTimeOffset SavedAt, Guid Id);

    private static string WriteListCursor(AssessmentSummary last)
    {
        return WriteCursor(new JsonObject
        {
            ["v"] = 1,
            ["t"] = last.SavedAt.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture),
            ["id"] = last.Id.ToString("D")
        });
    }

    private static ListCursor? ReadListCursor(string? cursor)
    {
        if (cursor is null) return null;
        try
        {
            var body = ReadCursorBody(cursor);
            return new ListCursor(DateTimeOffset.Parse(body["t"]!.GetValue<string>(), System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind), Guid.Parse(body["id"]!.GetValue<string>()));
        }
        catch (Exception ex) when (ex is FormatException or System.Text.Json.JsonException or InvalidOperationException
                                       or NullReferenceException or ArgumentException)
        {
            throw new WorkspaceException("invalidCursor", "The cursor is not valid for this list. Start again from the first page.");
        }
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

    // The projection keeps the first save's request id and backlink, so every revision must carry the same ones.
    private static void RequireSameIdentity(string frozenJson, string requestId, Guid? startedFrom)
    {
        var record = JsonNode.Parse(frozenJson)?["record"] as JsonObject;
        var revisedRequest = record?["requestId"] is JsonValue r && r.TryGetValue<string>(out var text) ? text : null;
        var revisedFrom = record?["startedFrom"]?["assessmentId"] is JsonValue f && f.TryGetValue<string>(out var from)
            && Guid.TryParse(from, out var parsed) ? parsed : (Guid?)null;
        if (record?["startedFrom"] is not null && revisedFrom is null || revisedRequest != requestId || revisedFrom != startedFrom)
            throw new WorkspaceException("invalidAssessment",
                "An edit keeps the assessment's request ID and the assessment it started from.");
    }

    // B31: a second guard behind the engine's. The workspace records assessments; it never approves them.
    private static void RefuseApproval(string frozenJson)
    {
        JsonObject? record;
        try { record = JsonNode.Parse(frozenJson)?["record"] as JsonObject; }
        catch (System.Text.Json.JsonException) { throw new WorkspaceException("invalidAssessment", "The assessment content is not valid JSON."); }
        if (Claims(record))
            throw new WorkspaceException("approvalNotSupported", "Assessments record impact; approval and sign-off are not supported.");
    }

    private static bool Claims(JsonNode? node) => node switch
    {
        JsonObject o => o.Any(p => p.Key is "approved" or "approvalStatus" || p.Key.Contains("signoff", StringComparison.OrdinalIgnoreCase) ||
                                   p.Key.Contains("sign-off", StringComparison.OrdinalIgnoreCase) || Claims(p.Value)),
        JsonArray a => a.Any(Claims),
        _ => false
    };

    private static JsonObject Parse(byte[] bytes) => JsonNode.Parse(bytes)!.AsObject();

    private static WorkspaceException SnapshotChanged() =>
        new("snapshotChanged", "The workspace snapshot changed. Start again from a new search.");
}

internal sealed record AssessmentEvent(Guid AssessmentId, int Version, string RequestId, string Frozen, string Markdown, Guid? StartedFrom);
