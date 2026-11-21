# Traversal cursor pagination — behaviours

**Feature:** [#119](https://github.com/jamiemitchellconsultants/division-ontology/issues/119)

**Phase:** [#121 — Analyst behaviours](https://github.com/jamiemitchellconsultants/division-ontology/issues/121)

**Status:** Accepted by the user on 21 November 2026; pending review and merge of the analyst phase pull request.

**Basis:** [Accepted product vision](01-vision.md) (PR #126), the product-owner handoff on #119, and the analyst decisions recorded with the user on 21 November 2026. Each numbered outcome below matches the vision. Scenarios specify observable behaviour; they do not prescribe components or deployment.

## Behaviours

### Outcome 1 — Every path is reachable

#### B1 — Continue a busy one-hop traversal

**Given** a published entity with more than 100 outgoing published paths and at least one workspace-authored (RuntimeRelationship) path that would fall beyond the first 100, **when** a reader traverses one hop and repeatedly submits each response's `nextCursor` to the same endpoint, **then** every page returns up to 100 paths, the concatenated pages contain each expected path exactly once (published paths identified by their published key, the workspace-authored path by its runtime id), and the final page has no `nextCursor`.

#### B2 — Continue a multi-hop traversal across a branch boundary

**Given** the multi-hop acceptance fixture in which the first page ends partway through expanding one branch of the breadth-first walk while other branches are still awaiting expansion, **when** the reader pages through the whole traversal, **then** the concatenated paths from all pages match the fixture's expected path sequence exactly once, in the same order an unpaged walk would produce them.

#### B3 — Continue in each direction, with and without unavailable endpoints

**Given** traversals with direction `incoming` and `both`, and a traversal with `includeUnavailable` set over paths whose endpoints are unavailable in the current PublishedKnowledgeSnapshot, **when** the reader pages through each, **then** each behaves as in B1/B2 for its direction and option: no path is gained, lost, or reordered at a page boundary, and unavailable endpoints keep their last-known provenance marking on every page.

#### B4 — Preserve path order across pages

**Given** unchanged published and workspace data, **when** any traversal is paged through, **then** the order of paths within and across pages equals the order of the first 100-plus paths of today's unpaged walk: breadth-first, with a node's published steps before its workspace-authored steps.

### Outcome 2 — Existing clients are undisturbed

#### B5 — First page is today's behaviour plus `nextCursor`

**Given** a reader who sends a traversal request with no cursor, **when** the walk exceeds 100 paths, **then** the response contains the same first 100 paths, the same entities and gaps, and `truncated: true` as today, plus a non-empty opaque `nextCursor`. **Given** a walk that finishes within the first page, **then** the response is exactly today's, with `truncated: false` and no `nextCursor`. **Given** a first page of exactly 100 paths with unexplored expansions still pending, **then** a `nextCursor` is issued and `truncated: true`: the walk is not proven finished, and a later page settles it (amended after developer-plan pushback).

#### B6 — `truncated` on continued pages

**Given** a page after which the walk still has unexplored expansions — including a page that emitted no paths because its examined rows were all filtered out, and a remaining frontier that may yield no further path (only leaf expansions or a filtered-only tail) — **when** it is returned, **then** `truncated: true` and a `nextCursor` are present: both mean "the walk is not proven finished", not "another path is guaranteed". **Given** the final page (the walk fully explored), **then** `truncated: false` and `nextCursor` is absent, and that final page may contain zero paths. A client that only reads `truncated` can therefore still tell whether the walk is known to be complete.

### Outcome 3 — Pages are self-contained

#### B7 — Entity context per page

**Given** any page of a traversal, **when** it is returned, **then** its `entities` contain every endpoint entity appearing in that page's paths — published and workspace-authored endpoints alike — with label, provenance, availability, and that entity's active KnowledgeAnnotations, and no entity the page does not touch is required to render the page.

### Outcome 4 — Continuation is trustworthy

#### B8 — Cursor binds the traversal parameters

**Given** a `nextCursor` issued for a traversal, **when** the reader submits it with a different start, direction, `maxDepth`, or `includeUnavailable` value, **then** the request fails with status 422 and code `invalidCursor`, and the error tells the client to restart the traversal from page one with its intended parameters. Continuation validation (cursor shape, parameter binding, and the snapshot/journal view) runs before the start entity is resolved: a submitted start that was changed to an unavailable entity therefore also fails 422 `invalidCursor` here, never `entityNotFound`; and a snapshot refresh that removed the original start fails 409 `staleCursor` under B10, for the same reason (amended after developer-plan pushback).

#### B9 — Malformed cursors fail as `invalidCursor`

**Given** a cursor value that is not a well-formed traversal cursor (not decodable, wrong shape, or whose frontier references no longer resolve under the bound view), **when** it is submitted, **then** the request fails with status 422 and code `invalidCursor` with the same restart instruction, and no traversal results are returned. Cursors carry no tamper evidence by decision (amended ADR 0020): a well-formed alteration that still resolves is treated as a client-chosen continuation inside what the caller's read role could already request directly — traversal is read-only and any start is requestable — and a regression test pins that altered-frontier behaviour so the stance is deliberate, not accidental (amended after developer-plan pushback).

#### B10 — Snapshot refresh makes the cursor stale

**Given** a `nextCursor` issued under one PublishedKnowledgeSnapshot, **when** a different snapshot has been activated before the cursor is submitted, **then** the request fails with status 409 and code `staleCursor`, tells the client to restart from page one, and returns no partial continuation.

#### B11 — Any workspace contribution change makes the cursor stale

**Given** a `nextCursor`, **when** any KnowledgeAnnotation or RuntimeRelationship has been created, edited, removed, or codified in the workspace since the cursor was issued (whether or not it touches the traversed neighbourhood), **then** submitting the cursor fails with status 409 and code `staleCursor` and the restart instruction. (Decision U3: the cursor binds the workspace journal position, not a neighbourhood-scoped view.)

#### B12 — A valid cursor continues exactly once

**Given** an unchanged snapshot and workspace, **when** the reader submits a valid `nextCursor` with the parameters it was issued for, **then** the next page continues the same walk with no path repeated from earlier pages and none skipped, however many times this is repeated to the final page.

### Outcome 5 — Agents can drive paging without guesswork

#### B13 — `next` link while more pages exist

**Given** any response (first or continued) for which another page exists, **when** it is returned, **then** it carries a runtime `Link` header with the workspace `next`-page relation (the absolute HTTPS relation mapped to `traverseGraph` in the spec's `x-agent-discovery.linkRelations`) targeting `POST /api/traverse`, and the response body carries the `nextCursor` to submit with the original traversal parameters. **Given** the final page, **then** neither the `next` link nor a `nextCursor` is present.

#### B14 — Spec documents paging and keeps existing links

**Given** the committed OpenAPI spec, **when** a client generates or inspects the `traverseGraph` operation, **then** the request schema includes the optional cursor field, the response schema includes `nextCursor`, the 422 `invalidCursor` and 409 `staleCursor` responses are documented for this operation, first-page and next-page request examples are present, and the success response links include `next` (to `traverseGraph`) alongside the existing `entityDetail` and `relationshipDetail` links.

#### B15 — Following the link reaches the next page

**Given** a first-page response with a `next` link, **when** a client calls `POST /api/traverse` with the linked cursor and the original parameters, **then** it receives exactly the next page (B12), and can repeat this to the final page without constructing cursors itself.

### Outcome 6 — Continuation stays cheap, at any page

#### B16 — Late pages do not replay earlier pages

**Given** the counting fixture (relationships examined per request are observable), **when** the reader requests page N for increasing N over unchanged data, **then** the number of relationships examined to serve each page stays within the architect-stated small bound of the 100-path page size, independent of N.

#### B17 — Filtered-out unavailable relationships do not grow the work without bound

**Given** a traversal whose frontier includes a growing number of unavailable relationships that are excluded (default `includeUnavailable: false`), **when** a page is served, **then** the number of relationships examined to fill that page stays within the architect-stated bound as the number of unavailable relationships grows; the page is still filled with the next available paths in order.

#### B18 — Traversal stays read-only

**Given** any paging sequence, **when** pages are requested and cursors are submitted (valid, malformed, mismatched, or stale), **then** no published record, workspace contribution, or Turtle file in this or any source repository is created, modified, or deleted by the traversal.

## Uncertainties

| Id | Question | Decision or deferral | Basis |
| --- | --- | --- | --- |
| U1 | Is the page size fixed or client-requested? | **Decided:** fixed at the current 100 paths. The request gains only a cursor field; the cursor therefore does not bind a page size. | User decision, analyst phase, 21 November 2026. |
| U2 | How are cursor failures coded? | **Decided (amended after architect pushback):** reuse the existing codes and their existing statuses — malformed or parameter-mismatched cursor: 422 `invalidCursor`; snapshot or workspace view moved on: 409 `staleCursor`. Both tell the client to restart from page one. The original 400 wording misstated the precedent: `ProblemResponses.StatusFor` maps `invalidCursor` to 422 for history and inventory, and 400 is reserved for unreadable request bodies. | User decision, analyst phase, 21 November 2026; status corrected in amendment, 21 November 2026, following the architect's pushback and verified against `ProblemResponses.StatusFor`. |
| U3 | Which workspace edits invalidate a cursor? | **Decided:** any contribution change (annotation or relationship create, edit, remove, codification) invalidates; the cursor binds the workspace journal position. Neighbourhood-scoped validity is deferred as out of scope unless a later phase raises pushback. | User decision, analyst phase, 21 November 2026. |
| U4 | Does `truncated` keep its meaning on continued pages? | **Decided (amended after developer-plan pushback):** `truncated: true` exactly when the walk is not proven finished (a cursor is issued), on first and continued pages alike — including an exactly-100 first page with pending expansions and a filtered-only tail; the settling page may be empty. | Vision outcome 2 requires first-page compatibility; "not proven finished" is the smallest reading consistent with bounded work. |
| U5 | What does the runtime `next` link contain, given the continuation needs a request body? | **Decided:** the header names the target operation (`POST /api/traverse`) with the documented relation; the cursor itself travels in the response body's `nextCursor` field and is submitted with the original parameters (B13/B15). Encoding a body template in the header is deferred as out of scope. | Consistent with the PR #117 link conventions: headers name affordances, bodies carry values. |
| U6 | How continuation resumes the multi-hop frontier, and what the cursor or server retains | **Deferred to the architect:** the behaviours (B2, B12, B16, B17) constrain any design; the mechanism (cursor-carried frontier vs replay-safe resumption) and its bounded-work proof are architecture work, noted for the architect's handoff. | Vision scope requires bounded per-request work and no unbounded server state; mechanism is deliberately not an analyst decision. |
| U7 | Which entities belong to a page when an endpoint recurs across pages? | **Decided:** every page carries the context for the endpoints appearing in that page's own paths (B7), even if an earlier page carried the same entity; pages remain independently renderable. | Vision outcome 3 (self-contained pages). |

## Terms

- **TraversalCursor** — an opaque continuation token issued with a non-final traversal page. It binds the traversal's start, direction, depth, and unavailable-endpoint option, the PublishedKnowledgeSnapshot it was issued under, and the workspace journal position at issue. Submitting it to the same traversal endpoint continues the walk; it is not meaningful for any other traversal or endpoint.
- **TraversalPage** — one bounded page (at most 100 paths) of a traversal result, together with the entity context for the endpoints appearing in its paths. A non-final TraversalPage carries a TraversalCursor and reports `truncated: true`; the final page carries neither a cursor nor truncation.
