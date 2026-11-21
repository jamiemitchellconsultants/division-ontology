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
        var relationships = new List<PublishedEdge>();
        string? last = null;
        var more = false;
        await using (var command = Command(tx, """
            SELECT record_key, kind, body::text FROM workspace_projection.published_records
             WHERE generation = $1 AND snapshot_id = $2 AND record_key > $3
               AND kind IN ('concept', 'node', 'relationshipType', 'assertion', 'reviewedLink', 'denotation', 'c4Binding')
             ORDER BY record_key LIMIT $4
            """, state.Generation, snapshot, after, request.Limit + 1))
        await using (var rows = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await rows.ReadAsync(cancellationToken))
            {
                if (entities.Count + types.Count + relationships.Count == request.Limit)
                {
                    more = true;
                    break;
                }
                last = rows.GetString(0);
                var body = rows.GetString(2);
                switch (rows.GetString(1))
                {
                    case "concept" or "node":
                        entities.Add(ReadJson<PublishedEntity>(body));
                        break;
                    case "relationshipType":
                        types.Add(ReadJson<RelationshipType>(body));
                        break;
                    default:
                        relationships.Add(ReadJson<PublishedEdge>(body));
                        break;
                }
            }
        }

        var cursor = more ? Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(
            new InventoryCursor(snapshot, request.Limit, last!), WorkspaceJson.Options)) : null;
        return new InventoryPage(snapshot, entities, types, await GapsAsync(tx, state, cancellationToken), cursor, relationships);
    }

    internal int LastTraversalExaminedSteps { get; private set; }

    internal int ExaminedStepBudget { get; set; } = 1000;

    public async Task<TraversalResult> TraverseAsync(TraversalRequest request, CancellationToken cancellationToken)
    {
        if (request.Start is null || request.MaxDepth is < 1 or > 8 ||
            request.Direction is not ("outgoing" or "incoming" or "both"))
            throw new WorkspaceException("invalidTraversal", "Traversal requires a start, a valid direction and a depth from 1 to 8.");
        LastTraversalExaminedSteps = 0;
        await using var connection = await db.OpenAsync(cancellationToken);
        await using var tx = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var state = await ReadStateAsync(tx, cancellationToken);
        var snapshot = state.RequireSnapshot();
        var journalHead = (long)(await ScalarAsync(tx,
            "SELECT COALESCE(MAX(position), 0) FROM workspace_journal.events", cancellationToken) ?? 0);
        if (request.Start.Kind is not ("concept" or "node" or "relationshipType"))
            throw new WorkspaceException("invalidTraversal", "Choose a published entity to start traversal.");

        TraversalCursorPayload? resume = null;
        if (request.Cursor is not null)
            resume = TraversalCursor.Decode(request.Cursor, request, snapshot, journalHead);

        var origin = await ResolveAsync(request.Start);
        if (resume is null && (origin is null || (!request.IncludeUnavailable && !origin.Available)))
            throw new WorkspaceException("entityNotFound", "The traversal start is unavailable.");

        var result = new List<TraversalPath>();
        var reached = new Dictionary<PublishedKey, EndpointContext>();
        if (origin is not null) reached[origin.Key] = origin;
        var attempted = new List<string>();
        var queue = new Queue<WalkNode>();
        WalkNode? active = null;

        if (resume is null)
        {
            queue.Enqueue(new WalkNode(request.Start.StorageKey, [], [], new HashSet<string>(StringComparer.Ordinal) { request.Start.StorageKey }));
        }
        else
        {
            if (resume.Resume.Current is { } current)
                active = await RebuildAsync(current.Path, resume.Resume.Refs);
            foreach (var pending in resume.Resume.Pending)
                queue.Enqueue(await RebuildAsync(pending.Path, resume.Resume.Refs));
            if (active is not null && resume.Resume.Current is { } saved)
            {
                active.Scope = saved.Scope == "runtime" ? 1 : 0;
                active.After = saved.After;
            }
        }

        var exhausted = false;
        while (!exhausted)
        {
            if (result.Count >= MaxPaths || LastTraversalExaminedSteps >= ExaminedStepBudget) break;
            active ??= queue.TryDequeue(out var next) ? next : null;
            if (active is null) { exhausted = true; break; }
            if (!attempted.Contains(active.At)) attempted.Add(active.At);

            var stop = false;
            while (active.Scope < 2 && !stop)
            {
                var scope = active.Scope == 0 ? snapshot : "runtime";
                var fetchLimit = Math.Min(101, ExaminedStepBudget - LastTraversalExaminedSteps);
                if (fetchLimit <= 0) { stop = true; break; }
                var neighbors = await AgeGraph.NeighborsAsync(tx, state.GraphName, active.At, scope,
                    request.Direction, cancellationToken, active.After, fetchLimit);
                LastTraversalExaminedSteps += neighbors.Count;
                if (neighbors.Count == 0) { active.Scope++; active.After = ""; continue; }
                foreach (var (edgeKey, nextKey) in neighbors)
                {
                    active.After = edgeKey;
                    TraversalStep? step = null;
                    string? reference = null;
                    if (active.Scope == 0)
                    {
                        var raw = await ReadPublishedAsync(tx, state, edgeKey, cancellationToken) ??
                            throw new InvalidOperationException("AGE edge lacks a published record.");
                        var edge = ReadJson<PublishedEdge>(raw);
                        var codifying = await ContributionStore.CodifyingAsync(tx, state, edge.Key, cancellationToken);
                        step = new TraversalStep(edge.Key, null, edge.From, edge.To, edge.Label,
                            DirectionOf(edge.From, active.At), "published", edge.Provenance, Context: edge.Context,
                            CodifyingContributions: codifying);
                        reference = "p:" + edge.Key.StorageKey;
                    }
                    else
                    {
                        var runtime = await ContributionStore.FindAsync(tx, state, Guid.Parse(edgeKey), cancellationToken);
                        if (runtime is not { Relationship: { } relationship, Removed: false })
                            throw new InvalidOperationException("AGE edge lacks an active runtime relationship.");
                        var from = await ResolveAsync(relationship.From) ??
                            throw new InvalidOperationException("Missing recorded runtime endpoint.");
                        var to = await ResolveAsync(relationship.To) ??
                            throw new InvalidOperationException("Missing recorded runtime endpoint.");
                        var unavailable = !from.Available || !to.Available;
                        if (unavailable && !request.IncludeUnavailable) continue;
                        step = new TraversalStep(null, runtime.Id, relationship.From, relationship.To,
                            relationship.Label, DirectionOf(relationship.From, active.At), "runtime", null, unavailable,
                            !from.Available ? from.Provenance.SnapshotId : !to.Available ? to.Provenance.SnapshotId : null,
                            Contribution: runtime, FromEndpoint: from, ToEndpoint: to);
                        reference = "r:" + runtime.Id.ToString("D");
                    }
                    var steps = active.Steps.Append(step).ToArray();
                    result.Add(new TraversalPath(steps));
                    foreach (var key in new[] { step.From, step.To })
                        if (!reached.ContainsKey(key) && await ResolveAsync(key) is { } endpoint)
                            reached.Add(key, endpoint);
                    if (steps.Length < request.MaxDepth && !active.Visited.Contains(nextKey))
                    {
                        var visited = new HashSet<string>(active.Visited, StringComparer.Ordinal) { nextKey };
                        queue.Enqueue(new WalkNode(nextKey, steps, [.. active.Refs, reference], visited));
                    }
                    if (result.Count >= MaxPaths || LastTraversalExaminedSteps >= ExaminedStepBudget) { stop = true; break; }
                }
                if (!stop && neighbors.Count < fetchLimit) { active.Scope++; active.After = ""; }
            }
            if (active.Scope >= 2) active = null;
            else break;
        }

        string? nextCursor = null;
        var truncated = false;
        if (!exhausted)
        {
            var pending = new List<WalkNode>();
            if (active is not null) pending.Add(active);
            pending.AddRange(queue);
            if (pending.Count > 0 || active is not null)
            {
                nextCursor = EncodeResume(request, snapshot, journalHead, active, queue);
                truncated = true;
            }
        }
        if (result.Count == 0)
            foreach (var at in attempted)
            {
                var key = KeyFromStorage(at, request.Start);
                if (key is not null && !reached.ContainsKey(key) && await ResolveAsync(key) is { } endpoint)
                    reached.Add(key, endpoint);
            }

        var entities = new List<TraversalEntity>();
        foreach (var endpoint in reached.Values)
        {
            var notes = await ContributionStore.AnnotationsAsync(tx, state, endpoint.Key, cancellationToken);
            entities.Add(new TraversalEntity(endpoint.Key, endpoint.Label, endpoint.Provenance, endpoint.Available, notes));
        }
        return new TraversalResult(snapshot, result, await GapsAsync(tx, state, cancellationToken), truncated, entities, nextCursor);

        static string DirectionOf(PublishedKey from, string at) => from.StorageKey == at ? "outgoing" : "incoming";

        PublishedKey? KeyFromStorage(string storage, PublishedKey sample)
        {
            if (storage == sample.StorageKey) return sample;
            foreach (var entity in reached.Keys) if (entity.StorageKey == storage) return entity;
            return null;
        }

        string EncodeResume(TraversalRequest req, string snap, long journal, WalkNode? current, Queue<WalkNode> rest)
        {
            var refs = new List<string>();
            var Index = new Dictionary<string, int>(StringComparer.Ordinal);
            int RefIndex(string reference)
            {
                if (!Index.TryGetValue(reference, out var index)) { index = refs.Count; refs.Add(reference); Index[reference] = index; }
                return index;
            }
            TraversalCurrent? encodedCurrent = null;
            if (current is not null)
                encodedCurrent = new TraversalCurrent(current.Refs.Select(RefIndex).ToArray(),
                    current.Scope == 1 ? "runtime" : "snapshot", current.After);
            var pendingEncoded = rest.Select(node => new TraversalPending(node.Refs.Select(RefIndex).ToArray())).ToArray();
            // References used only by the current node must be indexed before the current chain is consumed;
            // rebuild the payload in chain order to keep indexes stable.
            return TraversalCursor.Encode(new TraversalCursorPayload(1, req.Start.StorageKey, req.Direction,
                req.MaxDepth, req.IncludeUnavailable, snap, journal,
                new TraversalResume(refs, pendingEncoded, encodedCurrent)));
        }

        async Task<WalkNode> RebuildAsync(IReadOnlyList<int> path, IReadOnlyList<string> refs)
        {
            var steps = new List<TraversalStep>();
            var references = new List<string>();
            var visited = new HashSet<string>(StringComparer.Ordinal) { request.Start.StorageKey };
            var at = request.Start.StorageKey;
            foreach (var index in path)
            {
                var reference = refs[index];
                references.Add(reference);
                TraversalStep step;
                if (reference.StartsWith("p:", StringComparison.Ordinal))
                {
                    var edgeKey = reference[2..];
                    var raw = await ReadPublishedAsync(tx, state, edgeKey, cancellationToken) ??
                        throw InvalidResume();
                    var edge = ReadJson<PublishedEdge>(raw);
                    var codifying = await ContributionStore.CodifyingAsync(tx, state, edge.Key, cancellationToken);
                    step = new TraversalStep(edge.Key, null, edge.From, edge.To, edge.Label,
                        DirectionOf(edge.From, at), "published", edge.Provenance, Context: edge.Context,
                        CodifyingContributions: codifying);
                }
                else
                {
                    if (!Guid.TryParse(reference[2..], out var id)) throw InvalidResume();
                    var runtime = await ContributionStore.FindAsync(tx, state, id, cancellationToken);
                    if (runtime is not { Relationship: { } relationship, Removed: false }) throw InvalidResume();
                    var from = await ResolveAsync(relationship.From) ?? throw InvalidResume();
                    var to = await ResolveAsync(relationship.To) ?? throw InvalidResume();
                    var unavailable = !from.Available || !to.Available;
                    step = new TraversalStep(null, runtime.Id, relationship.From, relationship.To,
                        relationship.Label, DirectionOf(relationship.From, at), "runtime", null, unavailable,
                        !from.Available ? from.Provenance.SnapshotId : !to.Available ? to.Provenance.SnapshotId : null,
                        Contribution: runtime, FromEndpoint: from, ToEndpoint: to);
                }
                steps.Add(step);
                at = step.From.StorageKey == at ? step.To.StorageKey : step.From.StorageKey;
                visited.Add(at);
            }
            return new WalkNode(at, steps, references, visited);
        }

        static WorkspaceException InvalidResume() =>
            new("invalidCursor", "The traversal cursor is not valid. Restart the traversal from the first page.");

        async Task<EndpointContext?> ResolveAsync(PublishedKey key)
        {
            var entity = await ContributionStore.ResolveEndpointAsync(tx, state, key, cancellationToken);
            return entity is null ? null : new EndpointContext(key, entity.Label, entity.Provenance,
                entity.Provenance.SnapshotId == snapshot);
        }
    }

    private sealed class WalkNode(string at, IReadOnlyList<TraversalStep> steps, IReadOnlyList<string> refs, HashSet<string> visited)
    {
        public string At { get; } = at;
        public IReadOnlyList<TraversalStep> Steps { get; } = steps;
        public IReadOnlyList<string> Refs { get; } = refs;
        public HashSet<string> Visited { get; } = visited;
        public int Scope { get; set; }
        public string After { get; set; } = "";
    }

    public async Task<EntityDetail> EntityDetailAsync(PublishedKey key, CancellationToken token)
    {
        if (key?.Kind is not ("concept" or "node" or "relationshipType"))
            throw new WorkspaceException("entityNotFound", "Select a published entity or relationship type.");
        await using var connection = await db.OpenAsync(token);
        await using var tx = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, token);
        var state = await ReadStateAsync(tx, token);
        var snapshot = state.RequireSnapshot();
        var entity = await ContributionStore.ResolveEndpointAsync(tx, state, key, token) ??
            throw new WorkspaceException("entityNotFound", "Published entity was not found.");
        return new EntityDetail(snapshot, entity, entity.Provenance.SnapshotId == snapshot,
            await ContributionStore.AnnotationsAsync(tx, state, key, token));
    }

    public async Task<RelationshipDetail> RelationshipDetailAsync(RelationshipDetailRequest request,
        CancellationToken token)
    {
        if ((request.Key is null) == (request.RuntimeId is null))
            throw new WorkspaceException("invalidRelationship", "Choose one published or runtime relationship.");
        await using var connection = await db.OpenAsync(token);
        await using var tx = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, token);
        var state = await ReadStateAsync(tx, token);
        var snapshot = state.RequireSnapshot();
        if (request.RuntimeId is Guid runtimeId)
        {
            var current = await ContributionStore.FindAsync(tx, state, runtimeId, token);
            if (current is not { Removed: false, Relationship: { } relationship })
                throw new WorkspaceException("relationshipNotFound", "Runtime relationship was not found.");
            var from = await EndpointAsync(relationship.From);
            var to = await EndpointAsync(relationship.To);
            return new RelationshipDetail(snapshot, new TraversalStep(null, runtimeId, relationship.From,
                relationship.To, relationship.Label, "outgoing", "runtime", null, !from.Available || !to.Available,
                !from.Available ? from.Provenance.SnapshotId : !to.Available ? to.Provenance.SnapshotId : null,
                Contribution: current, FromEndpoint: from, ToEndpoint: to));
        }

        var key = request.Key!;
        var raw = key.Kind is "assertion" or "reviewedLink" or "denotation" or "c4Binding"
            ? await ReadPublishedAsync(tx, state, key.StorageKey, token) : null;
        if (raw is null) throw new WorkspaceException("relationshipNotFound", "Published relationship was not found.");
        var edge = ReadJson<PublishedEdge>(raw);
        var codifying = await ContributionStore.CodifyingAsync(tx, state, key, token);
        return new RelationshipDetail(snapshot, new TraversalStep(edge.Key, null, edge.From, edge.To,
            edge.Label, "outgoing", "published", edge.Provenance, Context: edge.Context,
            CodifyingContributions: codifying));

        async Task<EndpointContext> EndpointAsync(PublishedKey endpoint)
        {
            var entity = await ContributionStore.ResolveEndpointAsync(tx, state, endpoint, token) ??
                throw new InvalidOperationException("Missing recorded runtime endpoint.");
            return new EndpointContext(endpoint, entity.Label, entity.Provenance, entity.Provenance.SnapshotId == snapshot);
        }
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

}
