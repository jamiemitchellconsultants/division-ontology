# 0020 — Bind traversal cursors to the snapshot and the workspace journal position

- **Status:** accepted
- **Date:** 2026-11-21
- **Feature:** #119 (docs/features/traversal-cursor-pagination/)

## Context

A traversal cursor must only continue the walk it was issued for (B8), over the view it was issued on (B10, B11). The view has two independently moving parts: the active PublishedKnowledgeSnapshot, which changes on activation, and the workspace's runtime contributions (KnowledgeAnnotations and RuntimeRelationships), which change on any contribution write. The analyst phase decided (U2, as amended, and U3) that failures reuse the existing error codes at their existing statuses and that any contribution change invalidates a cursor. This decision fixes what the cursor binds and how validation reports each failure.

## Decision

The cursor binds four things: the traversal parameters (start, direction, maxDepth, includeUnavailable), the snapshot id, the workspace journal position current when the cursor was issued, and the frontier (ADR 0019).

Validation on submission runs in a fixed order inside the request's existing read transaction:

1. **Shape.** Undecodable, wrong-shaped, wrong-version, oversized, or internally unresolvable cursor payloads fail with 422 `invalidCursor` and the restart instruction (B9).
2. **Parameters.** A cursor whose bound parameters differ from the submitted request fails with 422 `invalidCursor` and the restart instruction (B8). Parameter mismatch is a client error about this request, not about the state of the world, so it is checked before staleness and never reported as stale.
3. **View.** A cursor whose snapshot id is no longer the active snapshot, or whose bound journal position is no longer the workspace journal head, fails with 409 `staleCursor` and the restart instruction (B10, B11). Any contribution append moves the journal head, so any annotation or relationship create, edit, removal, or codification invalidates every outstanding traversal cursor; no neighbourhood tracking is attempted.

Both failures are reported through the existing `ProblemResponses.StatusFor` mapping with no new codes and no status changes to existing endpoints. The cursor is opaque but unsigned, following the inventory and history cursors: it is a continuation token, not a credential — authentication and the read role still apply to every page request, and a tampered payload fails at step 1 or resolves to nothing at step 3's re-derivation and is reported as `invalidCursor`.

## Consequences

Clients get one predictable rule: cursors are single-view, single-walk tokens, and every failure mode tells them to restart from page one. Strict any-edit invalidation is deliberately coarse — a reader paging a neighbourhood can be invalidated by an unrelated contribution elsewhere in the workspace; that is the accepted cost (U3) of a binding that is cheap to check (one journal-head comparison) and impossible to get subtly wrong. Checking the parameter binding before staleness means a client that changes its mind about direction gets told its request is inconsistent rather than being told the world moved. Future relaxation to neighbourhood-scoped validity would need a new cursor version and a behaviour change, not a reinterpretation of this one.

## Alternatives considered

- **Neighbourhood-scoped invalidation** (cursor valid across unrelated edits): friendlier for long paging sessions, but requires tracking which contributions a walk touched and reasoning about edits one hop beyond the frontier. Deferred as out of scope in U3; rejected here for the journal position's simplicity and auditability.
- **Traversal-specific error codes** (`invalidTraversalCursor`/`staleTraversalCursor`): more precise names, but a second cursor-error vocabulary for clients alongside history and inventory. Rejected in U2 in favour of reuse.
- **Reporting parameter mismatch as `staleCursor`**: simpler validation (one check), but misdescribes a client-side inconsistency as world movement and invites a retry with the same wrong parameters. Rejected; order in the Decision makes the distinction observable.
