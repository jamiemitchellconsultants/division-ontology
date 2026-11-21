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

- **Cursor wire encoding and size cap (revised after review; amends ADR 0019).** The payload below is JSON-serialised, deflate-compressed (`DeflateStream`), then base64-encoded. Edge references are dictionary-coded: the payload carries each distinct edge reference once in `Refs`, and every path chain is a list of indexes into it, so shared breadth-first prefixes are stored once. Payload records (all in `runtime/RuntimeKnowledge.Store/TraversalCursor.cs`, internal):

  ```csharp
  internal sealed record TraversalCursorPayload(
      int Version, string Start, string Direction, int MaxDepth, bool IncludeUnavailable,
      string Snapshot, long JournalPosition, TraversalResume Resume);
  internal sealed record TraversalResume(
      IReadOnlyList<string> Refs, IReadOnlyList<TraversalPending> Pending, TraversalCurrent? Current);
  // Refs entries: "p:" + published StorageKey, or "r:" + runtime contribution id (Guid "D").
  internal sealed record TraversalPending(IReadOnlyList<int> Path);          // indexes into Refs
  // Current node being expanded: Path is its producing chain (empty at the start node),
  // Scope is the next scope to read ("snapshot" then "runtime"), After is that scope's
  // last *processed* edge key — emitted or filtered — ("" if none).
  internal sealed record TraversalCurrent(IReadOnlyList<int> Path, string Scope, string After);
  ```

  Size bounds, applied in this order: a submitted cursor string over **262144 characters** is `invalidCursor` before decoding; decompression is capped at **8388608 bytes** of JSON (reading one byte more is `invalidCursor`, so a compression bomb cannot expand without limit); the server only ever issues a cursor whose compressed form is within the submitted-cursor cap — `Encode` asserts this, and the wide fixture in Task 2 submits every issued cursor, so an issue-side breach fails the build rather than reaching a client. `Version` is 1; any other version is malformed. A pending/current endpoint (`at`) is never stored: it is `Start` for an empty chain, else the far endpoint of the chain's last re-derived step. Visited sets are re-derived from the chain (start plus each step's far endpoint), exactly as the walk builds them today.

- **Journal head read.** Inside the walk's `RepeatableRead` transaction: `SELECT COALESCE(MAX(position), 0) FROM workspace_journal.events`. Snapshot activation and every contribution write append journal events, so one comparison covers B10's state check together with the snapshot-id comparison, and B11's any-edit rule. (No lock is taken; `LockHeadAsync` is for writers.)
- **Validation precedes start resolution (finding 5).** A continued request is validated in this order: syntactic request checks (direction, depth), then cursor decode and binding (shape, parameters, snapshot, journal head), and only then is the start entity resolved. Two consequences are pinned by tests: a snapshot refresh that removed the original start answers 409 `staleCursor` (the view check fires first), and a request whose start was changed to an unavailable entity answers 422 `invalidCursor` (the parameter check fires first) — never `entityNotFound` for a continuation whose cursor is wrong.

- **Counting and fetching examined steps (B16/B17, finding 4).** `AgeGraph.NeighborsAsync` gains a validated `limit` parameter (1–101; interpolated into the AGE `LIMIT` after range validation, since AGE does not bind it as a parameter). The walk fetches with `limit = min(101, remainingExaminedAllowance)` — never a full 101-row batch once the remaining allowance is smaller — and counts **every row returned**, emitted or filtered, into `internal int LastTraversalExaminedSteps { get; private set; }` (reset per call; Tests sees Store internals through `Scenario`). No production surface (no result field, no header, no logging) is added. Tests assert `LastTraversalExaminedSteps <= 1000` on every page and that a page's count equals the neighbour rows it actually returned, including filtered ones.

- **Named OpenAPI examples.** Today's mechanism (`RequestExample`, one value per operation) is extended, not replaced: add `RequestExamples(IReadOnlyList<(string Name, object Value)>)` metadata; the operation transformer writes `media.Examples` from it when present, falling back to the single-example behaviour otherwise. For `traverseGraph`, `OpenApiExamples` gains `TraversalFirstPage` (no cursor) and `TraversalNextPage` (same fields plus `Cursor = "opaque-continuation-cursor"` — an invented placeholder, never a real cursor). Spec tests pin the example names and that the next-page example carries a cursor field.

## Contracts

```csharp
// RuntimeKnowledge.Contracts/Published.cs — additive, at the end of each record
public sealed record TraversalRequest(PublishedKey Start, string Direction = "outgoing",
    int MaxDepth = 2, bool IncludeUnavailable = false, string? Cursor = null);
public sealed record TraversalResult(string SnapshotId, IReadOnlyList<TraversalPath> Paths,
    IReadOnlyList<CoverageGap> Gaps, bool Truncated, IReadOnlyList<TraversalEntity> Entities,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? NextCursor = null);

// RuntimeKnowledge.Store/TraversalCursor.cs — internal
internal static class TraversalCursor
{
    internal const int MaxCursorChars = 262144;  // compressed form; decompressed JSON is capped at 8388608 bytes
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

1. **Open and validate in the finding-5 order:** open the transaction, read state and journal head; run the syntactic request checks (direction, depth 1–8); if a cursor is present, `Decode` it against the request, snapshot, and journal head **before resolving any entity**; only then resolve the start (first page: as today; continuation: the start is needed only for entity context, never as a gate).
2. **First page (no cursor):** seed the queue with the start node exactly as today.
3. **Continued page:** `Decode` against the request, the transaction's snapshot, and the journal head. Rebuild the queue: for the cursor's `Current` (if present), re-derive its path steps by reading each edge reference (published record by key via the existing published-record read; runtime contribution via `ContributionStore.FindAsync`), rebuild its visited set, and resume it first, from `Scope`/`After`. Then rebuild each `Pending` entry the same way, in stored order. Re-derivation failures (a reference that no longer resolves under the bound view) are `invalidCursor`.
4. **Emit** as today — published scope before runtime scope per node, breadth-first — fetching each batch with the remaining-allowance limit and counting every neighbour row returned into `LastTraversalExaminedSteps`, and stopping when either 100 paths are emitted or the examined budget is reached. Runtime neighbours filtered out as unavailable still count as examined, and the current node's resume position (`Scope`/`After`) advances past **every processed row, emitted or filtered, before any stop is taken** — so a page that exhausts its budget after examining only filtered rows emits zero paths yet its cursor already points past them, and the next page advances to the next available path without re-examining a single row (finding 2).
5. **Close the page (truncation semantics, finding 3):** if any resumption state remains — the queue is non-empty **or** the current node has an unexamined scope or neighbour — encode the new cursor from the live walk state (each queue entry becomes a `TraversalPending` of reference indexes; the partially expanded node becomes `TraversalCurrent` with its next scope and last processed key) and return it as `NextCursor` with `truncated: true`, **even if this page emitted zero paths and even if the remaining frontier may yield none** (leaf-only expansions, a filtered-only tail): `truncated` and `nextCursor` mean "the walk is not proven finished". Only a fully exhausted walk (queue empty, current node fully examined in both scopes) closes with `NextCursor = null` and `truncated: false`, and such a final page may legitimately contain zero paths after earlier pages. This preserves first-page compatibility: an exactly-100-path first page with unexplored frontier returns a cursor and `truncated: true` (today's walk would have had to explore that frontier to know), and an empty first-page walk exhausts inside the first request and returns no cursor.
6. **Entities per page:** resolve only the endpoints of this page's paths (as today, with annotations), independent of earlier pages (B7/U7). A first page with no paths still carries the start entity, exactly as today's response does; an empty continuation page carries the endpoints of the nodes it attempted to expand. Both are asserted on the serialized response (finding 7): a final page's JSON has **no `nextCursor` property at all** (not a null), and the empty first page's JSON contains the start entity.

Edge reference derivation while emitting: a published step's reference is `"p:" + step.Key.Value.StorageKey` (from the `PublishedEdge` just read); a runtime step's is `"r:" + runtime.Id.ToString("D")`. On re-derivation, a published reference re-reads the edge record (yielding From/To/label/provenance exactly as a fresh read would); a runtime reference re-reads the contribution and rebuilds the step with its recorded endpoints, as `ReadStepsAsync` does today.

## Fixtures

`Scenario.cs` gains a traversal snapshot variant (new `MemoryBundles` content, added beside the existing variants, not changing them):

- A hub entity with **150 outgoing published edges** to distinct leaf entities (enough for two pages at depth 1), plus **one runtime relationship** from the hub, created through the API in the test, which orders after the published steps (B1).
- A **multi-hop chain**: hub → mid-1 and hub → mid-2, each mid with 60 outgoing published edges to leaves (depth 2 page 1 ends partway through one mid's expansion while the other mid is still queued — the B2 branch boundary). The expected full path sequence is computed in the test by the same breadth-first rules the behaviours state (published-before-runtime, key order), and concatenated pages must equal it exactly.
- **Unavailable endpoints:** published leaves present in snapshot `initial` and absent in `missing`, with runtime relationships pointing at them, for B3/B17 filtering counts.
- **Wide multi-page graph (finding 1):** a `wide` snapshot variant generated programmatically — a hub with 40 mid entities, each mid with 150 leaf edges (6,040 depth-2 paths, ~61 pages). The test submits **every** issued cursor in sequence, asserts each is within the compressed-size cap and decodes (by being accepted), and asserts the concatenated pages equal the expected breadth-first sequence exactly once.

Focused filters: codec `FullyQualifiedName~TraversalCursorTests`; paging `FullyQualifiedName~TraversalPagingTests`; HTTP end-to-end stays in `WorkspaceScenarioTests` (extended, not a new class).

## Verification checklist (Task 6)

For each behaviour, the proving test: B1 busy-node test (store) + HTTP paging (scenario); B2 branch-boundary sequence (store) and the finding-1 wide fixture (every cursor submitted, complete sequence, all cursors within cap); B3 direction/unavailable paging (store); B4 sequence equality in B2's fixture; B5 first-page parity against today's response shape (store), exact-100-with-pending-leaves (cursor + `truncated: true`, then a final page that may be empty), the filtered-only tail, and the serialized-response assertions of finding 7 (no `nextCursor` property on the final page; start entity present on an empty first page); B6 flags on continued/final pages; B7 per-page entities include a runtime-only endpoint; B8 each parameter field (codec tests + one store test), validated before start resolution (finding 5: changed-to-unavailable start → 422); B9 malformed variants plus the finding-6 regression test — a well-formed alteration of a frontier reference either fails re-derivation as `invalidCursor` or continues only within what the caller's read role could already request, with the accepted unsigned stance recorded in amended ADR 0020/B9; B10 activate-then-submit (store + HTTP), including a refresh that removes the original start → 409 (finding 5); B11 create/edit/remove/codify-then-submit (store + HTTP for one representative write); B12 repeated continuation exact-once (store); B13 `next` header present/absent (scenario); B14 spec assertions (OpenApi category) + committed-vs-served equality (existing `CommittedSpecTests`); B15 follow-the-header flow (scenario); B16 examined counts across pages 1..N flat within bound, fetched with the remaining-allowance limit so no page ever returns more rows than its allowance (store; finding 4); B17 examined counts as unavailable count grows, plus budget-stop resumption — including a page that emits zero paths on filtered rows alone and a later page reaching the next available path with no row re-examined (store; finding 2); B18 journal/snapshot/Turtle state unchanged across paging (store: journal head and snapshot identical before/after).

Commands: `dotnet build runtime/RuntimeKnowledge.slnx`; `dotnet test runtime/RuntimeKnowledge.Tests --filter Category=OpenApi`; `bash deploy/workspace/database/test-db.sh run dotnet test runtime/RuntimeKnowledge.Tests --filter "FullyQualifiedName~WorkspaceScenarioTests|FullyQualifiedName~Traversal"`; `npm test`; `node --test test/api-contract.test.mjs`; build the Api twice and compare `shasum -a 256 runtime/openapi/workspace-api.json`. Any behaviour without a green test returns to its owning task as a new failing test first.
