# Traversal cursor pagination implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task by task. Steps use checkbox (`- [ ]`) syntax for tracking. Read the detailed companion plan before each task.

**Goal:** Make the 100-path cap on `POST /api/traverse` a page boundary: an opaque cursor, bound to the traversal parameters, the active snapshot, and the workspace journal position, continues the breadth-first walk exactly once per path, with a HATEOAS `next` link and a fully documented, regenerated OpenAPI contract.

**Architecture:** Stateless, cursor-carried frontier (ADR 0019, as amended after review): each page encodes its pending expansions as dictionary-coded, deflate-compressed edge-reference chains plus the partially expanded node's per-scope resume position (advanced past every processed row, filtered or emitted), and re-derives steps and visited sets on submission. The cursor binds parameters, snapshot id, and journal head and validates shape → parameters → view — before the start entity is resolved (ADR 0020, as amended). Work per request is bounded by the 100-path page plus a 1000-row examined budget enforced at fetch time.

**Tech Stack:** .NET 10 (SDK pinned by `runtime/global.json`), Postgres with Apache AGE (`AgeGraph.NeighborsAsync` keyset reads), xUnit (`OpenApi` category via `OpenApiHost`; DB-backed scenario via `deploy/workspace/database/test-db.sh`), Node test runner for `test/api-contract.test.mjs`.

**Spec:** [`01-vision.md`](../../../features/traversal-cursor-pagination/01-vision.md), [`02-behaviours.md`](../../../features/traversal-cursor-pagination/02-behaviours.md) (B1–B18, U1–U7), [`03-architecture.md`](../../../features/traversal-cursor-pagination/03-architecture.md) (components and B1–B18 traceability), ADRs [0019](../../../adr/0019-carry-the-traversal-frontier-in-the-cursor.md) and [0020](../../../adr/0020-bind-traversal-cursors-to-snapshot-and-journal.md). Exact signatures, payload shapes, fixtures, and test code are in [`docs/detailed-implementations/traversal-cursor-pagination/plan.md`](../../../detailed-implementations/traversal-cursor-pagination/plan.md).

## Global constraints

- **Compatibility.** First-page requests (no cursor) return today's first 100 paths, entities, and gaps, with `truncated` unchanged in meaning; the only response change is the additive `nextCursor` field (B5, B6). Existing clients and the Blazor UI are untouched.
- **Fixed page size.** 100 paths. The request gains only a cursor field — no page-size parameter (U1).
- **Order and identity.** Breadth-first order, published steps before runtime steps within a node, is preserved across pages (B4). Published steps keep their published key; runtime steps keep their runtime id (B1).
- **Cursor failures reuse existing codes at existing statuses** via `ProblemResponses.StatusFor`: `invalidCursor` 422, `staleCursor` 409 (U2 as amended). No new error codes; no status changes to history or inventory. Continuations are validated before the start entity is resolved, so a removed start after refresh is `staleCursor` and a changed, unavailable start is `invalidCursor`. Cursors stay unsigned by decision (amended B9/ADR 0020): a well-formed alteration is a client-chosen continuation inside the caller's existing read scope, pinned by a regression test.
- **Any contribution change invalidates** a cursor (journal-head binding, U3/ADR 0020). Snapshot activation invalidates via the snapshot binding (B10).
- **Bounded work.** At most 100 paths returned and at most 1000 examined rows per request, enforced at fetch time: each neighbour fetch is limited to the smaller of the remaining allowance and the page's remaining path slots, and every row returned (emitted or filtered) counts. Exhausting the budget ends the page early with a cursor whose resume position already points past every processed row, so the next page never re-examines one. No replay of earlier pages (B16); no production telemetry — examined counts are observed in tests through a counters object each test passes in. (Amended after dev-build review.)
- **Truncation means "not proven finished".** `nextCursor` is issued (and `truncated` is true) whenever resumption state remains, even if the remaining frontier may yield no further path; a final page may therefore be empty. An empty first-page walk still exhausts in one request and returns the start entity, as today. `nextCursor` is omitted — the property absent, not null — on the final page (amended B5/B6).
- **Stateless and read-only.** No new tables, migrations, sessions, or background jobs; traversal opens only its existing `RepeatableRead` transaction and appends nothing (B18). Turtle files are never touched.
- **Spec discipline.** The spec changes in the same commits as the API code that changes it; regenerate with `dotnet build runtime/RuntimeKnowledge.Api` and commit `runtime/openapi/workspace-api.json`; examples use only invented values (`example-` sources, `https://example.test/` IRIs, a placeholder cursor string).
- **Each task** is written test-first, ends in one focused commit and push after `git diff --cached` review, and keeps `dotnet test runtime/RuntimeKnowledge.Tests --filter Category=OpenApi`, the DB-backed scenario (`bash deploy/workspace/database/test-db.sh run dotnet test runtime/RuntimeKnowledge.Tests --filter "FullyQualifiedName~WorkspaceScenarioTests|FullyQualifiedName~Traversal"`), and `npm test` green at its boundary.

## Outstanding phase amendments

Two pushbacks from this plan's review are recorded on #119 and are answered by amendment PRs #135 (analyst) and #136 (architect), which must merge before the build starts; this plan is already written to the decisions taken with the user on 21 November 2026:

- **Analyst (B5, B6, B8, B9):** truncation/nextCursor as "not proven finished" semantics (including exactly-100-with-pending-leaves, the filtered-only tail, and a possibly empty final page); validation before start-entity resolution; B9 narrowed to malformed/unresolvable payloads with the well-formed-alteration stance pinned by a regression test.
- **Architect (ADR 0019, ADR 0020, 03-architecture.md):** dictionary-coded deflate-compressed cursor with the cap on the compressed form and an issue-side decodability guarantee; fetch-time budget enforcement from the remaining allowance; resume past every processed (including filtered) row; the unsigned-cursor decision with its read-scope rationale.

The build phase stays gated on those amendments merging as well as on this plan's review and merge.

## File map and interfaces

| File | Responsibility |
| --- | --- |
| `runtime/RuntimeKnowledge.Contracts/Published.cs` (modify) | `TraversalRequest(..., string? Cursor = null)`; `TraversalResult(..., string? NextCursor = null)` — additive optional fields only. |
| `runtime/RuntimeKnowledge.Store/TraversalCursor.cs` (create) | Cursor payload records, `Encode`/`Decode` with ADR 0020 validation order, 256 KiB compressed cap with an 8 MiB decompressed guard, `WorkspaceException`s. Internal. |
| `runtime/RuntimeKnowledge.Store/GraphQueries.cs` (modify) | Page-bounded `TraverseAsync`: start or resume, examined-step budget, per-page entity context, `nextCursor` issue. (Amended after dev-build review.) |
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
3. **The frontier is widest and the walk runs for dozens of pages.** Every server-issued cursor must remain decodable by the server's own cap; Task 2's wide fixture (6,040 depth-2 paths) submits every issued cursor and asserts each is within the compressed cap and the full sequence is exact.
4. **A page fills exactly at 100 with nothing behind it.** No cursor may be issued and `truncated` must be false (B5/B6 boundary) — Task 2 tests the exact-boundary case, not just over-100.
5. **The examined-step budget is exhausted mid-node among filtered runtime edges — before any path is emitted.** The page returns zero paths with a cursor that already points past those filtered rows; later pages reach the next available path without re-examining one and without looping (B17) — Task 4 tests the zero-emit page and its continuation.
6. **A client tampers with a cursor's frontier but keeps it well-formed, or swaps in an unavailable start.** Tamper-evidence is deliberately not provided; the regression test pins the accepted behaviour (invalidCursor on failed re-derivation, or continuation only within the caller's read scope), and the changed-start case must answer `invalidCursor`, never `entityNotFound` — Tasks 1 and 3.

---

### Task 1: Traversal contract and cursor codec

**Files:** modify `Published.cs`; create `TraversalCursor.cs`, `TraversalCursorTests.cs`.

**Interfaces:** produces `TraversalRequest.Cursor`, `TraversalResult.NextCursor` (omitted from JSON when null), and the codec's `Encode(TraversalCursorState) : string` / `Decode(string, TraversalRequest, string snapshot, long journalHead) : TraversalCursorState` used by Task 2.

- [ ] Step 1: Write failing codec and contract tests (compressed dictionary-coded round-trip; cursor-string and decompressed-size caps; each parameter field mismatched; malformed base64/deflate/JSON/version; snapshot and journal mismatch raise the stale code; serialized final page has no `nextCursor` property; well-formed frontier alteration per the amended B9).
- [ ] Step 2: Run `dotnet test runtime/RuntimeKnowledge.Tests --filter TraversalCursor` — fails, codec missing.
- [ ] Step 3: Implement the contract fields and the codec per the detailed plan (dictionary-coded compressed payload, 256 KiB compressed / 8 MiB decompressed caps, validation order).
- [ ] Step 4: Re-run the focused tests, then the OpenApi category (spec not yet changed for traversal — existing tests stay green).
- [ ] Step 5: Commit `feat: traversal cursor contract and codec` and push.

### Task 2: Page-bounded walk

**Files:** modify `GraphQueries.cs`, `Scenario.cs`; create `TraversalPagingTests.cs`.

**Interfaces:** consumes Task 1's codec; produces a `TraverseAsync` that pages, and accepts an internal test-passed counters object (`TraversalCounters`, for Task 4's measures). (Amended after dev-build review.)

- [ ] Step 1: Extend the scenario fixtures (busy hub, multi-hop chain, unavailable endpoints, and the wide 6,040-path graph) and write failing paging tests: busy one-hop (B1), branch-boundary and wide multi-page exact sequences with every cursor submitted and within cap (B2), first-page parity, exactly-100-with-pending-leaves, filtered-only tail, and empty-first-page start entity (B5/B6/B7).
- [ ] Step 2: Run the DB-backed focused tests via `test-db.sh` — they fail on the hard 100-path stop.
- [ ] Step 3: Implement the page-bounded walk per the detailed plan (resume from frontier + keyset positions, per-page entity resolution, budget check before each fetch (initially without the shrinkable test seam)).
- [ ] Step 4: Re-run focused tests, the full scenario class, and the OpenApi category.
- [ ] Step 5: Commit `feat: page-bounded traversal walk` and push.

### Task 3: Cursor binding over HTTP-visible state

**Files:** modify `TraversalPagingTests.cs` (tests first), `GraphQueries.cs` if the journal-head read needs adjustment.

**Interfaces:** produces stale/invalid behaviour B8–B11 end to end at store level.

- [ ] Step 1: Write failing tests, all validated before start resolution: parameter mismatch and malformed cursor over the real walk; snapshot activation that removes the original start then submit → 409 (B10); changed, unavailable start → 422 (B8); contribution create/edit then submit (B11); valid continuation still succeeds (B12).
- [ ] Step 2: Run via `test-db.sh` — stale cases fail (cursor not yet bound or binding not checked against live state).
- [ ] Step 3: Bind issue/validation to the transaction's snapshot and journal head per ADR 0020.
- [ ] Step 4: Re-run focused and scenario tests.
- [ ] Step 5: Commit `feat: bind traversal cursors to snapshot and journal` and push.

### Task 4: Bounded-work budget

**Files:** modify `GraphQueries.cs`, `TraversalPagingTests.cs`.

**Interfaces:** produces the 1000-step examined budget and its counting measures (B16, B17).

- [ ] Step 1: Write failing counting tests: neighbour fetches limited to the remaining allowance with every returned row counted and no page over 1000 (B16, finding 4); examined steps per page independent of page number (B16); filling a page among growing filtered unavailable runtime edges, including a zero-emit budget page whose cursor already points past the filtered rows and a later page reaching the next available path with no row re-examined (B17, finding 2).
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
