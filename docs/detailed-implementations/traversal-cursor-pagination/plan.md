# Traversal cursor pagination — detailed implementation plan

**Inputs:** [vision](../../../features/traversal-cursor-pagination/01-vision.md), [behaviours](../../../features/traversal-cursor-pagination/02-behaviours.md) (B1–B18, U1–U7), [architecture](../../../features/traversal-cursor-pagination/03-architecture.md), ADRs [0019](../../adr/0019-carry-the-traversal-frontier-in-the-cursor.md) and [0020](../../adr/0020-bind-traversal-cursors-to-snapshot-and-journal.md), and the [task plan](../../superpowers/plans/traversal-cursor-pagination/plan.md).

**Build rule:** Start on `feature/traversal-cursor-pagination/dev-build` only after the plan PR is reviewed and merged. Execute Tasks 1–6 of the task plan in order, test first:

1. Add the named test code.
2. Run the focused command and see it fail for the stated reason.
3. Implement the stated interface.
4. Run the focused tests, `dotnet test runtime/RuntimeKnowledge.Tests --filter Category=OpenApi`, and the DB-backed suites where stated.
5. Rebuild the API (`dotnet build runtime/RuntimeKnowledge.Api`). If `runtime/openapi/workspace-api.json` changed, stage it in the same commit.
6. Review `git diff --cached` and `git diff --cached --check`.
7. Commit with the message in the task plan, and push.

DB-backed commands run as `bash deploy/workspace/database/test-db.sh run dotnet test runtime/RuntimeKnowledge.Tests --filter "<filter>" --nologo`. The local SDK must satisfy `runtime/global.json` (10.0.402, `latestPatch`). Never stage `dist/`.

## Decisions this plan settles

The four detail points the architect passed to the developer:

- **Cursor wire encoding and size cap.** Base64 of UTF-8 JSON, the repository's existing cursor encoding (`Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(payload, WorkspaceJson.Options))`). Payload records (all in `runtime/RuntimeKnowledge.Store/TraversalCursor.cs`, internal):

  ```csharp
  internal sealed record TraversalCursorPayload(
      int Version, string Start, string Direction, int MaxDepth, bool IncludeUnavailable,
      string Snapshot, long JournalPosition, TraversalResume Resume);
  internal sealed record TraversalResume(
      IReadOnlyList<TraversalPending> Pending, TraversalCurrent? Current);
  // Edge references in a path chain: "p:" + published StorageKey, or "r:" + runtime contribution id (Guid "D").
  internal sealed record TraversalPending(IReadOnlyList<string> Path);
  // Current node being expanded: Path is its producing chain (empty at the start node),
  // Scope is the next scope to read ("snapshot" then "runtime"), After is that scope's last emitted edge key ("" if none).
  internal sealed record TraversalCurrent(IReadOnlyList<string> Path, string Scope, string After);
  ```

  Decode rejects payloads whose cursor string exceeds **131072 characters** (128 KiB) before parsing (`invalidCursor`); the branch-boundary fixture (Task 2) asserts real cursors stay far below it. `Version` is 1; any other version is malformed. A pending/current endpoint (`at`) is never stored: it is `Start` for an empty chain, else the far endpoint of the chain's last re-derived step. Visited sets are re-derived from the chain (start plus each step's far endpoint), exactly as the walk builds them today.

- **Journal head read.** Inside the walk's `RepeatableRead` transaction: `SELECT COALESCE(MAX(position), 0) FROM workspace_journal.events`. Snapshot activation and every contribution write append journal events, so one comparison covers B10's state check together with the snapshot-id comparison, and B11's any-edit rule. (No lock is taken; `LockHeadAsync` is for writers.)

- **Counting examined steps (B16/B17).** `GraphQueries` counts every row returned by `AgeGraph.NeighborsAsync` during `TraverseAsync` into `internal int LastTraversalExaminedSteps { get; private set; }` (reset at the start of each call; the Tests assembly already sees Store internals through `Scenario`). Scenario/store tests assert on it after direct `Scenario.Graph.TraverseAsync` calls. No production surface (no result field, no header, no logging) is added.

- **Named OpenAPI examples.** Today's mechanism (`RequestExample`, one value per operation) is extended, not replaced: add `RequestExamples(IReadOnlyList<(string Name, object Value)>)` metadata; the operation transformer writes `media.Examples` from it when present, falling back to the single-example behaviour otherwise. For `traverseGraph`, `OpenApiExamples` gains `TraversalFirstPage` (no cursor) and `TraversalNextPage` (same fields plus `Cursor = "opaque-continuation-cursor"` — an invented placeholder, never a real cursor). Spec tests pin the example names and that the next-page example carries a cursor field.

## Contracts

```csharp
// RuntimeKnowledge.Contracts/Published.cs — additive, at the end of each record
public sealed record TraversalRequest(PublishedKey Start, string Direction = "outgoing",
    int MaxDepth = 2, bool IncludeUnavailable = false, string? Cursor = null);
public sealed record TraversalResult(string SnapshotId, IReadOnlyList<TraversalPath> Paths,
    IReadOnlyList<CoverageGap> Gaps, bool Truncated, IReadOnlyList<TraversalEntity> Entities,
    string? NextCursor = null);

// RuntimeKnowledge.Store/TraversalCursor.cs — internal
internal static class TraversalCursor
{
    internal const int MaxCursorChars = 131072;
    // Throws WorkspaceException("invalidCursor", "…restart…") on shape/parameter failures,
    // WorkspaceException("staleCursor", "…restart…") on snapshot/journal mismatch (ADR 0020 order).
    static string Encode(TraversalCursorPayload payload);
    static TraversalCursorPayload Decode(string cursor, TraversalRequest request, string snapshot, long journalHead);
}

// RuntimeKnowledge.Store/GraphQueries.cs — changed behaviour, same signature
public async Task<TraversalResult> TraverseAsync(TraversalRequest request, CancellationToken cancellationToken);
internal int LastTraversalExaminedSteps { get; private set; }  // examined neighbour rows of the last call
```

Constants in `GraphQueries`: `MaxPaths` stays 100 (the page size); add `MaxExaminedSteps = 1000` (10× page size, ADR 0019). `PendingPath` keeps its shape for the live walk; conversion to/from `TraversalPending`/`TraversalCurrent` happens only at page boundaries.

## Walk resumption, step by step

1. **Validate the request** as today (start, depth 1–8, direction), resolve the start, open the transaction, read state and journal head.
2. **First page (no cursor):** seed the queue with the start node exactly as today.
3. **Continued page:** `Decode` against the request, the transaction's snapshot, and the journal head. Rebuild the queue: for the cursor's `Current` (if present), re-derive its path steps by reading each edge reference (published record by key via the existing published-record read; runtime contribution via `ContributionStore.FindAsync`), rebuild its visited set, and resume it first, from `Scope`/`After`. Then rebuild each `Pending` entry the same way, in stored order. Re-derivation failures (a reference that no longer resolves under the bound view) are `invalidCursor`.
4. **Emit** as today — published scope before runtime scope per node, breadth-first — counting every neighbour row returned into `LastTraversalExaminedSteps`, and stopping when either 100 paths are emitted or the examined budget is reached. Runtime neighbours filtered out as unavailable still count as examined.
5. **Close the page:** if the queue is non-empty or the current node has unexamined scopes/neighbours, encode the new cursor from the live `PendingPath` queue (each queue entry becomes a `TraversalPending` of edge references; the partially expanded node becomes `TraversalCurrent` with its next scope and `after` key) and return it as `NextCursor` with `truncated: true`; otherwise `NextCursor = null`, `truncated: false`.
6. **Entities per page:** resolve only the endpoints of this page's paths (as today, with annotations), independent of earlier pages (B7/U7).

Edge reference derivation while emitting: a published step's reference is `"p:" + step.Key.Value.StorageKey` (from the `PublishedEdge` just read); a runtime step's is `"r:" + runtime.Id.ToString("D")`. On re-derivation, a published reference re-reads the edge record (yielding From/To/label/provenance exactly as a fresh read would); a runtime reference re-reads the contribution and rebuilds the step with its recorded endpoints, as `ReadStepsAsync` does today.

## Fixtures

`Scenario.cs` gains a traversal snapshot variant (new `MemoryBundles` content, added beside the existing variants, not changing them):

- A hub entity with **150 outgoing published edges** to distinct leaf entities (enough for two pages at depth 1), plus **one runtime relationship** from the hub, created through the API in the test, which orders after the published steps (B1).
- A **multi-hop chain**: hub → mid-1 and hub → mid-2, each mid with 60 outgoing published edges to leaves (depth 2 page 1 ends partway through one mid's expansion while the other mid is still queued — the B2 branch boundary). The expected full path sequence is computed in the test by the same breadth-first rules the behaviours state (published-before-runtime, key order), and concatenated pages must equal it exactly.
- **Unavailable endpoints:** published leaves present in snapshot `initial` and absent in `missing`, with runtime relationships pointing at them, for B3/B17 filtering counts.

Focused filters: codec `FullyQualifiedName~TraversalCursorTests`; paging `FullyQualifiedName~TraversalPagingTests`; HTTP end-to-end stays in `WorkspaceScenarioTests` (extended, not a new class).

## Verification checklist (Task 6)

For each behaviour, the proving test: B1 busy-node test (store) + HTTP paging (scenario); B2 branch-boundary sequence (store); B3 direction/unavailable paging (store); B4 sequence equality in B2's fixture; B5 first-page parity against today's response shape (store) and exact-100 boundary; B6 flags on continued/final pages; B7 per-page entities include a runtime-only endpoint; B8 each parameter field (codec tests + one store test); B9 malformed variants (codec tests); B10 activate-then-submit (store + HTTP); B11 create/edit/remove/codify-then-submit (store + HTTP for one representative write); B12 repeated continuation exact-once (store); B13 `next` header present/absent (scenario); B14 spec assertions (OpenApi category) + committed-vs-served equality (existing `CommittedSpecTests`); B15 follow-the-header flow (scenario); B16 examined counts across pages 1..N flat within bound (store); B17 examined counts as unavailable count grows, plus budget-stop resumption (store); B18 journal/snapshot/Turtle state unchanged across paging (store: journal head and snapshot identical before/after).

Commands: `dotnet build runtime/RuntimeKnowledge.slnx`; `dotnet test runtime/RuntimeKnowledge.Tests --filter Category=OpenApi`; `bash deploy/workspace/database/test-db.sh run dotnet test runtime/RuntimeKnowledge.Tests --filter "FullyQualifiedName~WorkspaceScenarioTests|FullyQualifiedName~Traversal"`; `npm test`; `node --test test/api-contract.test.mjs`; build the Api twice and compare `shasum -a 256 runtime/openapi/workspace-api.json`. Any behaviour without a green test returns to its owning task as a new failing test first.
