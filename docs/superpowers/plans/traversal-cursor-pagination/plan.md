# Traversal cursor pagination implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task by task. Steps use checkbox (`- [ ]`) syntax for tracking. Read the detailed companion plan before each task.

**Goal:** Make the 100-path cap on `POST /api/traverse` a page boundary: an opaque cursor, bound to the traversal parameters, the active snapshot, and the workspace journal position, continues the breadth-first walk exactly once per path, with a HATEOAS `next` link and a fully documented, regenerated OpenAPI contract.

**Architecture:** Stateless, cursor-carried frontier (ADR 0019): each page encodes its pending expansions as compact edge-reference chains plus the partially expanded node's per-scope keyset resume positions, and re-derives steps and visited sets on submission. The cursor binds parameters, snapshot id, and journal head and validates shape → parameters → view (ADR 0020), raising the existing `invalidCursor` (422) / `staleCursor` (409). Work per request is bounded by the 100-path page plus a 1000-step examined budget.

**Tech Stack:** .NET 10 (SDK pinned by `runtime/global.json`), Postgres with Apache AGE (`AgeGraph.NeighborsAsync` keyset reads), xUnit (`OpenApi` category via `OpenApiHost`; DB-backed scenario via `deploy/workspace/database/test-db.sh`), Node test runner for `test/api-contract.test.mjs`.

**Spec:** [`01-vision.md`](../../../features/traversal-cursor-pagination/01-vision.md), [`02-behaviours.md`](../../../features/traversal-cursor-pagination/02-behaviours.md) (B1–B18, U1–U7), [`03-architecture.md`](../../../features/traversal-cursor-pagination/03-architecture.md) (components and B1–B18 traceability), ADRs [0019](../../../adr/0019-carry-the-traversal-frontier-in-the-cursor.md) and [0020](../../../adr/0020-bind-traversal-cursors-to-snapshot-and-journal.md). Exact signatures, payload shapes, fixtures, and test code are in [`docs/detailed-implementations/traversal-cursor-pagination/plan.md`](../../../detailed-implementations/traversal-cursor-pagination/plan.md).

## Global constraints

- **Compatibility.** First-page requests (no cursor) return today's first 100 paths, entities, and gaps, with `truncated` unchanged in meaning; the only response change is the additive `nextCursor` field (B5, B6). Existing clients and the Blazor UI are untouched.
- **Fixed page size.** 100 paths. The request gains only a cursor field — no page-size parameter (U1).
- **Order and identity.** Breadth-first order, published steps before runtime steps within a node, is preserved across pages (B4). Published steps keep their published key; runtime steps keep their runtime id (B1).
- **Cursor failures reuse existing codes at existing statuses** via `ProblemResponses.StatusFor`: `invalidCursor` 422, `staleCursor` 409 (U2 as amended). No new error codes; no status changes to history or inventory.
- **Any contribution change invalidates** a cursor (journal-head binding, U3/ADR 0020). Snapshot activation invalidates via the snapshot binding (B10).
- **Bounded work.** At most 100 paths returned and at most 1000 examined steps (neighbour rows returned by `AgeGraph.NeighborsAsync`) per request; exhausting the examined budget ends the page early with a cursor (ADR 0019). No replay of earlier pages (B16); no production telemetry — examined counts are observed in tests through the store's internal counter.
- **Stateless and read-only.** No new tables, migrations, sessions, or background jobs; traversal opens only its existing `RepeatableRead` transaction and appends nothing (B18). Turtle files are never touched.
- **Spec discipline.** The spec changes in the same commits as the API code that changes it; regenerate with `dotnet build runtime/RuntimeKnowledge.Api` and commit `runtime/openapi/workspace-api.json`; examples use only invented values (`example-` sources, `https://example.test/` IRIs, a placeholder cursor string).
- **Each task** is written test-first, ends in one focused commit and push after `git diff --cached` review, and keeps `dotnet test runtime/RuntimeKnowledge.Tests --filter Category=OpenApi`, the DB-backed scenario (`bash deploy/workspace/database/test-db.sh run dotnet test runtime/RuntimeKnowledge.Tests --filter "FullyQualifiedName~WorkspaceScenarioTests|FullyQualifiedName~Traversal"`), and `npm test` green at its boundary.

## File map and interfaces

| File | Responsibility |
| --- | --- |
| `runtime/RuntimeKnowledge.Contracts/Published.cs` (modify) | `TraversalRequest(..., string? Cursor = null)`; `TraversalResult(..., string? NextCursor = null)` — additive optional fields only. |
| `runtime/RuntimeKnowledge.Store/TraversalCursor.cs` (create) | Cursor payload records, `Encode`/`Decode` with ADR 0020 validation order, 128 KiB decode cap, `WorkspaceException`s. Internal. |
| `runtime/RuntimeKnowledge.Store/GraphQueries.cs` (modify) | Page-bounded `TraverseAsync`: start or resume, examined-step budget and internal counter, per-page entity context, `nextCursor` issue. |
| `runtime/RuntimeKnowledge.Store/WorkspaceSql.cs` (read only) | `AgeGraph.NeighborsAsync` keyset reads; journal head read inside the walk's transaction (detailed plan settles the exact query). |
| `runtime/RuntimeKnowledge.Api/WorkspaceRoutes.cs` (modify) | Traverse route passes `Cursor`, declares `invalidCursor`/`staleCursor`, appends the `next` Link header when a cursor is returned. |
| `runtime/RuntimeKnowledge.Api/WorkspaceOpenApi.cs`, `OpenApiExamples.cs` (modify) | `traverseGraph`: named `firstPage`/`nextPage` examples, `next` success link, cursor text in the agent description; example-mechanism extension for named examples. |
| `runtime/openapi/workspace-api.json` (regenerate) | Committed spec, written by building the Api project. |
| `runtime/RuntimeKnowledge.Tests/TraversalCursorTests.cs` (create) | DB-free codec tests: round-trip, malformed, parameter mismatch, binding fields. |
| `runtime/RuntimeKnowledge.Tests/TraversalPagingTests.cs` (create) | DB-backed store tests: busy node, branch-boundary sequence, directions, staleness, examined-step bounds (via `Scenario`). |
| `runtime/RuntimeKnowledge.Tests/Scenario.cs` (modify) | Traversal fixture snapshot: hub with >100 published paths, a multi-hop chain, unavailable endpoints. |
| `runtime/RuntimeKnowledge.Tests/OpenApi/*.cs` (modify) | `traverseGraph` contract: fields, errors, examples, links; route-table codes for `/api/traverse`. |
| `runtime/RuntimeKnowledge.Tests/WorkspaceScenarioTests.cs`, `ScenarioRunner.cs` (modify) | HTTP end-to-end: follow `next`, busy node over HTTP, stale cursor after snapshot refresh and after a workspace edit. |

## Review focus

1. **A client replays a cursor after editing traversal parameters by one field** (e.g. only `includeUnavailable` flipped). Parameter binding must catch every field, not just start (B8) — Task 1 tests each field separately.
2. **A cursor is issued, then an unrelated annotation is added elsewhere in the workspace.** Strict invalidation must fire (B11), even though users may find it coarse — Task 3 pins it so the behaviour is deliberate, not accidental.
3. **The frontier is widest (depth 8, high branching) and the encoded cursor approaches its cap.** The walk must still issue a decodable cursor; Task 2's branch-boundary fixture measures the encoded size and asserts it is comfortably under the cap.
4. **A page fills exactly at 100 with nothing behind it.** No cursor may be issued and `truncated` must be false (B5/B6 boundary) — Task 2 tests the exact-boundary case, not just over-100.
5. **The examined-step budget is exhausted mid-node among filtered runtime edges.** The page ends early with a cursor that resumes the same node/scope at the saved keyset position — no infinite loop, no skipped available path (B17) — Task 4 tests resumption after a budget stop.

---

### Task 1: Traversal contract and cursor codec

**Files:** modify `Published.cs`; create `TraversalCursor.cs`, `TraversalCursorTests.cs`.

**Interfaces:** produces `TraversalRequest.Cursor`, `TraversalResult.NextCursor`, and the codec's `Encode(TraversalCursorState) : string` / `Decode(string, TraversalRequest, string snapshot, long journalHead) : TraversalCursorState` used by Task 2.

- [ ] Step 1: Write failing codec tests (round-trip; each parameter field mismatched; malformed base64/JSON/version/oversize; snapshot and journal mismatch raise the stale code).
- [ ] Step 2: Run `dotnet test runtime/RuntimeKnowledge.Tests --filter TraversalCursor` — fails, codec missing.
- [ ] Step 3: Implement the contract fields and the codec per the detailed plan (payload records, validation order, 128 KiB cap).
- [ ] Step 4: Re-run the focused tests, then the OpenApi category (spec not yet changed for traversal — existing tests stay green).
- [ ] Step 5: Commit `feat: traversal cursor contract and codec` and push.

### Task 2: Page-bounded walk

**Files:** modify `GraphQueries.cs`, `Scenario.cs`; create `TraversalPagingTests.cs`.

**Interfaces:** consumes Task 1's codec; produces a `TraverseAsync` that pages, and `GraphQueries.LastTraversalExaminedSteps` (internal, for Task 4's measures).

- [ ] Step 1: Extend the scenario fixture (hub with >100 published paths, multi-hop chain, an unavailable endpoint) and write failing paging tests: busy one-hop (B1), branch-boundary exact sequence (B2), first-page parity and exact-100 boundary (B5/B6), per-page entities (B7).
- [ ] Step 2: Run the DB-backed focused tests via `test-db.sh` — they fail on the hard 100-path stop.
- [ ] Step 3: Implement the page-bounded walk per the detailed plan (resume from frontier + keyset positions, per-page entity resolution, budget counter at its initial no-op bound).
- [ ] Step 4: Re-run focused tests, the full scenario class, and the OpenApi category.
- [ ] Step 5: Commit `feat: page-bounded traversal walk` and push.

### Task 3: Cursor binding over HTTP-visible state

**Files:** modify `TraversalPagingTests.cs` (tests first), `GraphQueries.cs` if the journal-head read needs adjustment.

**Interfaces:** produces stale/invalid behaviour B8–B11 end to end at store level.

- [ ] Step 1: Write failing tests: parameter mismatch and malformed cursor over the real walk; snapshot activation then submit (B10); contribution create/edit then submit (B11); valid continuation still succeeds (B12).
- [ ] Step 2: Run via `test-db.sh` — stale cases fail (cursor not yet bound or binding not checked against live state).
- [ ] Step 3: Bind issue/validation to the transaction's snapshot and journal head per ADR 0020.
- [ ] Step 4: Re-run focused and scenario tests.
- [ ] Step 5: Commit `feat: bind traversal cursors to snapshot and journal` and push.

### Task 4: Bounded-work budget

**Files:** modify `GraphQueries.cs`, `TraversalPagingTests.cs`.

**Interfaces:** produces the 1000-step examined budget and its counting measures (B16, B17).

- [ ] Step 1: Write failing counting tests: examined steps per page independent of page number (B16); filling a page among growing filtered unavailable runtime edges, including resume after a budget stop (B17).
- [ ] Step 2: Run via `test-db.sh` — counts grow or the page stalls without the budget/resume behaviour.
- [ ] Step 3: Enforce the examined-step budget with early page end + cursor at the saved keyset position.
- [ ] Step 4: Re-run focused, scenario, and OpenApi tests; record the measured counts in the commit message.
- [ ] Step 5: Commit `feat: bound traversal work per page` and push.

### Task 5: API surface, HATEOAS, and the committed spec

**Files:** modify `WorkspaceRoutes.cs`, `WorkspaceOpenApi.cs`, `OpenApiExamples.cs`, OpenApi tests, `ScenarioRunner.cs`, `WorkspaceScenarioTests.cs`; regenerate `runtime/openapi/workspace-api.json`.

**Interfaces:** produces the HTTP behaviour of B13–B15 and the documented contract of B14.

- [ ] Step 1: Write failing OpenApi tests (cursor/nextCursor fields, `invalidCursor`/`staleCursor` on the operation, named examples, `next` link beside the existing links, route-table codes) and failing scenario assertions (runtime `next` header present/absent, following it over HTTP, stale cursor over HTTP after refresh and after edit).
- [ ] Step 2: Run the OpenApi category and the scenario — new assertions fail.
- [ ] Step 3: Implement the route, header, declared errors, named examples, and agent-table text; rebuild the API to regenerate the spec and stage it.
- [ ] Step 4: Re-run the OpenApi category, scenario, `node --test test/api-contract.test.mjs`, and `npm test`; verify the spec is byte-stable across a second build.
- [ ] Step 5: Commit `feat: expose traversal paging in the API and spec` and push.

### Task 6: Whole-feature verification

**Files:** none (verification only; fixes return to the owning task).

- [ ] Step 1: Run the full verification set from the detailed plan (solution build, OpenApi category, DB-backed scenario + traversal tests, `npm test`, `node --test test/api-contract.test.mjs`, spec byte-stability, `git diff` review against B1–B18).
- [ ] Step 2: Walk the B1–B18 checklist in the detailed plan and record which test proves each behaviour; anything unproven returns to its owning task as a new failing test.
- [ ] Step 3: Commit any verification fixes under their owning task's message pattern and push.
