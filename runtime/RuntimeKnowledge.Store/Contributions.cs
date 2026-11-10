using System.Data;
using System.Text.Json;
using Npgsql;
using RuntimeKnowledge.Contracts;
using static RuntimeKnowledge.Store.WorkspaceSql;

namespace RuntimeKnowledge.Store;

public sealed class ContributionStore(Db db, TimeProvider? clock = null)
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;

    public async Task<ContributionResult> ApplyAsync(Actor actor, ContributionCommand command, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(actor?.Issuer) || string.IsNullOrWhiteSpace(actor.Subject))
            throw new WorkspaceException("actorRequired", "A verified acting identity is required.");
        await using var connection = await db.OpenAsync(token);
        await using var tx = await connection.BeginTransactionAsync(token);
        await LockHeadAsync(tx, token);
        var state = await ReadStateAsync(tx, token);
        var snapshot = state.RequireSnapshot();
        if (state.WritesPaused)
            throw new WorkspaceException("writesPaused", "Workspace writes are paused for rebuild.");
        var now = _clock.GetUtcNow();
        var (type, before, after) = await PlanChangeAsync(tx, state, actor, now, command, token);
        var change = new ContributionChange(before, after);
        var position = await AppendAsync(tx, type, snapshot, after.Id, after.Version, actor, now, change, token);
        await ApplyRecordedAsync(tx, state, change, token);
        await tx.CommitAsync(token);
        return new ContributionResult(after.Id, after.Version, position);
    }

    public async Task<RuntimeContribution?> FindAsync(Guid id, CancellationToken token)
    {
        await using var connection = await db.OpenAsync(token);
        await using var tx = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, token);
        return await FindAsync(tx, await ReadStateAsync(tx, token), id, token);
    }

    public async Task<HistoryPage> HistoryAsync(Guid? contributionId, PageRequest request, CancellationToken token)
    {
        if (request.Limit is < 1 or > 100) throw new WorkspaceException("invalidLimit", "History limit must be from 1 to 100.");
        var after = ReadHistoryCursor(request.Cursor, contributionId);
        await using var connection = await db.OpenAsync(token);
        await using var tx = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, token);
        await using var command = Command(tx, """
            SELECT position, event_type, actor_issuer, actor_subject, occurred_at,
                   aggregate_id, aggregate_revision, payload::text, snapshot_id
              FROM workspace_journal.events
              WHERE position > $1 AND aggregate_id IS NOT NULL
                AND ($2::uuid IS NULL OR aggregate_id = $2)
              ORDER BY position LIMIT $3
            """, after, contributionId, request.Limit + 1);
        await using var rows = await command.ExecuteReaderAsync(token);
        var entries = new List<HistoryEntry>();
        while (await rows.ReadAsync(token))
        {
            var change = ReadJson<ContributionChange>(rows.GetString(7));
            entries.Add(new HistoryEntry(rows.GetInt64(0), rows.GetString(1), new Actor(rows.GetString(2), rows.GetString(3)),
                rows.GetFieldValue<DateTimeOffset>(4), rows.GetGuid(5), rows.GetInt32(6), change.Before, change.After, rows.GetString(8)));
        }
        var more = entries.Count > request.Limit;
        if (more) entries.RemoveAt(entries.Count - 1);
        var cursor = more ? Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(
            new HistoryCursor(contributionId, entries[^1].Position), WorkspaceJson.Options)) : null;
        return new HistoryPage(entries, cursor);
    }

    private static async Task<(string Type, RuntimeContribution? Before, RuntimeContribution After)> PlanChangeAsync(
        NpgsqlTransaction tx, ProjectionState state, Actor actor, DateTimeOffset now, ContributionCommand command,
        CancellationToken token)
    {
        var snapshot = state.RequireSnapshot();
        if (command is CreateAnnotation note)
        {
            ValidateAnnotation(note.Category, note.Text);
            var target = await RequireEndpointAsync(tx, state, note.Target, token);
            return ("annotationCreated", null, new RuntimeContribution(Guid.NewGuid(), 1, "annotation", actor, now,
                snapshot, false, new AnnotationData(note.Target, note.Category, note.Text, target.Provenance), null, []));
        }
        if (command is CreateRelationship relationship)
        {
            var data = await RelationshipAsync(tx, state, relationship.From, relationship.To, relationship.Meaning, null, token);
            return ("relationshipCreated", null, new RuntimeContribution(Guid.NewGuid(), 1, "relationship", actor, now,
                snapshot, false, null, data, []));
        }

        var (id, expected) = command switch
        {
            EditAnnotation c => (c.Id, c.ExpectedVersion),
            EditRelationship c => (c.Id, c.ExpectedVersion),
            RemoveContribution c => (c.Id, c.ExpectedVersion),
            AssociateCodification c => (c.Id, c.ExpectedVersion),
            _ => throw new WorkspaceException("invalidCommand", "Unknown contribution command.")
        };
        var current = await FindAsync(tx, state, id, token) ??
            throw new WorkspaceException("contributionNotFound", "Contribution was not found.");
        if (current.Version != expected || current.Removed)
            throw new WorkspaceException("versionConflict", "Review the current contribution before saving.", current: current);

        var revised = current with { Version = checked(current.Version + 1) };
        switch (command)
        {
            case EditAnnotation edit when current.Annotation is not null:
                ValidateAnnotation(edit.Category, edit.Text);
                return ("annotationEdited", current, revised with
                {
                    Annotation = current.Annotation with { Category = edit.Category, Text = edit.Text }
                });
            case EditRelationship edit when current.Relationship is not null:
                var data = await RelationshipAsync(tx, state, edit.From, edit.To, edit.Meaning, current.Relationship, token);
                return ("relationshipEdited", current, revised with { Relationship = data });
            case RemoveContribution:
                return (current.Kind + "Removed", current, revised with { Removed = true });
            case AssociateCodification association when current.Relationship is not null:
                var published = association.PublishedRelationship;
                var raw = published?.Kind is "assertion" or "reviewedLink" or "denotation" or "c4Binding"
                    ? await GraphQueries.ReadPublishedAsync(tx, state, published.StorageKey, token) : null;
                if (raw is null) throw new WorkspaceException("invalidAssociation", "Select a published relationship.");
                if (current.Associations.Any(link => link.PublishedRelationship == published))
                    throw new WorkspaceException("associationExists", "That codification association is already recorded.");
                var link = new CodificationAssociation(Guid.NewGuid(), published!, ReadJson<PublishedEdge>(raw).Provenance, actor, now);
                return ("codificationAssociated", current, revised with { Associations = [.. current.Associations, link] });
            default:
                throw new WorkspaceException("invalidCommand", "This command does not apply to the contribution kind.");
        }
    }

    private static async Task<RelationshipData> RelationshipAsync(NpgsqlTransaction tx, ProjectionState state,
        PublishedKey from, PublishedKey to, RelationshipMeaning meaning, RelationshipData? previous, CancellationToken token)
    {
        var fromEntity = from == previous?.From
            ? await ResolveEndpointAsync(tx, state, from, token) : await RequireEndpointAsync(tx, state, from, token);
        var toEntity = to == previous?.To
            ? await ResolveEndpointAsync(tx, state, to, token) : await RequireEndpointAsync(tx, state, to, token);
        if (fromEntity is null || toEntity is null)
            throw new WorkspaceException("endpointUnavailable", "An endpoint is unavailable.");
        if (meaning is null) throw new WorkspaceException("invalidMeaning", "Specify a relationship meaning.");

        var fromProvenance = from == previous?.From ? previous!.FromProvenance : fromEntity.Provenance;
        var toProvenance = to == previous?.To ? previous!.ToProvenance : toEntity.Provenance;
        if (meaning.PublishedType is { } type)
        {
            if (meaning.CustomLabel is not null || meaning.Explanation is not null || type.Kind != "relationshipType")
                throw new WorkspaceException("invalidMeaning", "Choose either a published type or a custom meaning.");
            var raw = await GraphQueries.ReadPublishedAsync(tx, state, type.StorageKey, token);
            if (raw is null)
            {
                if (meaning == previous?.Meaning)
                    return new RelationshipData(from, to, meaning, previous!.Label, fromProvenance, toProvenance,
                        previous.MeaningProvenance);
                throw new WorkspaceException("invalidMeaning", "Select a type available in the active snapshot.");
            }
            var definition = ReadJson<RelationshipType>(raw);
            if (!MatchesConstraint(fromEntity, definition.Domain) || !MatchesConstraint(toEntity, definition.Range))
                throw new WorkspaceException("invalidMeaning", "The selected relationship type does not apply to these endpoints.");
            return new RelationshipData(from, to, meaning, definition.Label, fromProvenance, toProvenance, definition.Provenance);
        }
        if (string.IsNullOrWhiteSpace(meaning.CustomLabel) || string.IsNullOrWhiteSpace(meaning.Explanation))
            throw new WorkspaceException("invalidMeaning", "A custom relationship needs a label and explanation.");
        return new RelationshipData(from, to, meaning, meaning.CustomLabel.Trim(), fromProvenance, toProvenance, null);
    }

    private static bool MatchesConstraint(PublishedEntity entity, string? constraint) =>
        constraint is null || entity.Key.Identifier == constraint || entity.Types.Contains(constraint, StringComparer.Ordinal);

    private static async Task<PublishedEntity> RequireEndpointAsync(NpgsqlTransaction tx, ProjectionState state,
        PublishedKey? key, CancellationToken token)
    {
        var body = key?.Kind is "node" or "concept"
            ? await GraphQueries.ReadPublishedAsync(tx, state, key.StorageKey, token) : null;
        if (body is null) throw new WorkspaceException("endpointUnavailable", "Select an available published entity.");
        return ReadJson<PublishedEntity>(body);
    }

    internal static async Task<PublishedEntity?> ResolveEndpointAsync(NpgsqlTransaction tx,
        ProjectionState state, PublishedKey key, CancellationToken token)
    {
        var raw = await GraphQueries.ReadPublishedAsync(tx, state, key.StorageKey, token);
        if (raw is null)
        {
            raw = (string?)await ScalarAsync(tx, """
                SELECT p.body::text FROM workspace_journal.events e
                JOIN workspace_projection.published_records p
                  ON p.snapshot_id = e.snapshot_id AND p.generation = $1 AND p.record_key = $2
                WHERE e.event_type = 'snapshotActivated' AND e.position <= $3
                ORDER BY e.position DESC LIMIT 1
                """, token, state.Generation, key.StorageKey, state.ActivationPosition);
        }
        if (raw is null) return null;
        if (key.Kind != "relationshipType") return ReadJson<PublishedEntity>(raw);
        var type = ReadJson<RelationshipType>(raw);
        return new PublishedEntity(type.Key, type.Label, type.Description, [], type.Provenance);
    }

    private static void ValidateAnnotation(string category, string text)
    {
        if (category is not ("businessExplanation" or "wayOfWorking" or "businessRbac") || string.IsNullOrWhiteSpace(text))
            throw new WorkspaceException("invalidAnnotation", "Select an annotation category and supply text.");
    }

    internal static async Task<RuntimeContribution?> FindAsync(NpgsqlTransaction tx, ProjectionState state,
        Guid id, CancellationToken token)
    {
        var body = (string?)await ScalarAsync(tx, """
            SELECT body::text FROM workspace_projection.contributions WHERE generation = $1 AND contribution_id = $2
            """, token, state.Generation, id);
        return body is null ? null : ReadJson<RuntimeContribution>(body);
    }

    internal static async Task<IReadOnlyList<RuntimeContribution>> AnnotationsAsync(NpgsqlTransaction tx,
        ProjectionState state, PublishedKey target, CancellationToken token)
    {
        await using var command = Command(tx, """
            SELECT body::text FROM workspace_projection.contributions
            WHERE generation = $1 AND target_key = $2 AND kind = 'annotation' AND NOT removed
            ORDER BY contribution_id
            """, state.Generation, target.StorageKey);
        await using var rows = await command.ExecuteReaderAsync(token);
        var results = new List<RuntimeContribution>();
        while (await rows.ReadAsync(token)) results.Add(ReadJson<RuntimeContribution>(rows.GetString(0)));
        return results;
    }

    internal static async Task<IReadOnlyList<RuntimeContribution>> CodifyingAsync(NpgsqlTransaction tx,
        ProjectionState state, PublishedKey published, CancellationToken token)
    {
        await using var command = Command(tx, """
            SELECT body::text FROM workspace_projection.contributions
            WHERE generation = $1 AND kind = 'relationship' AND NOT removed
              AND EXISTS (SELECT FROM jsonb_array_elements(body->'associations') AS link
                          WHERE link->'publishedRelationship' = $2::jsonb)
            ORDER BY contribution_id
            """, state.Generation, Json(published));
        await using var rows = await command.ExecuteReaderAsync(token);
        var results = new List<RuntimeContribution>();
        while (await rows.ReadAsync(token)) results.Add(ReadJson<RuntimeContribution>(rows.GetString(0)));
        return results;
    }

    internal static async Task ApplyRecordedAsync(NpgsqlTransaction tx, ProjectionState state,
        ContributionChange change, CancellationToken token)
    {
        var after = change.After;
        var graphKey = after.Id.ToString("D");
        if (change.Before is { Relationship: not null, Removed: false })
            await AgeGraph.RemoveEdgeAsync(tx, state.GraphName, graphKey, "runtime", token);
        await ExecuteAsync(tx, """
            INSERT INTO workspace_projection.contributions
                (generation, contribution_id, revision, kind, removed, target_key, body)
            VALUES ($1, $2, $3, $4, $5, $6, $7::jsonb)
            ON CONFLICT (generation, contribution_id) DO UPDATE
                SET revision = EXCLUDED.revision, removed = EXCLUDED.removed,
                    target_key = EXCLUDED.target_key, body = EXCLUDED.body
            """, token, state.Generation, after.Id, after.Version, after.Kind, after.Removed,
            after.Annotation?.Target.StorageKey, Json(after));
        if (after.Relationship is { } relationship && !after.Removed)
            await AgeGraph.AddEdgeAsync(tx, state.GraphName, graphKey, relationship.From.StorageKey,
                relationship.To.StorageKey, "runtime", token);
        if (after.Annotation is not null)
            await ExecuteAsync(tx, """
                INSERT INTO workspace_projection.contribution_index_work (generation, contribution_id, revision)
                    VALUES ($1, $2, $3) ON CONFLICT DO NOTHING
                """, token, state.Generation, after.Id, after.Version);
    }

    private static long ReadHistoryCursor(string? cursor, Guid? contributionId)
    {
        if (cursor is null) return 0;
        if (cursor.Length > 8192) throw new WorkspaceException("invalidCursor", "Invalid history cursor.");
        try
        {
            var data = JsonSerializer.Deserialize<HistoryCursor>(Convert.FromBase64String(cursor), WorkspaceJson.Options);
            if (data is not null && data.ContributionId == contributionId && data.Position > 0) return data.Position;
        }
        catch (Exception ex) when (ex is FormatException or JsonException) { }
        throw new WorkspaceException("invalidCursor", "Invalid history cursor.");
    }

    private sealed record HistoryCursor(Guid? ContributionId, long Position);
}
