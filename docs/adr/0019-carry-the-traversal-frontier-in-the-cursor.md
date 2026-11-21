# 0019 — Carry the traversal frontier in the cursor

- **Status:** accepted
- **Date:** 2026-11-21
- **Feature:** #119 (docs/features/traversal-cursor-pagination/)

## Context

Paged traversal must resume a breadth-first walk exactly where the previous page stopped — including partway through expanding one node, with other branches still queued (B2, B12) — while keeping per-request work bounded independent of page number (B16, B17) and the traversal read-only (B18). The walk's resumption state is its frontier: the pending node expansions plus the partially expanded current node. That state has to live somewhere between requests. The repository's existing paged endpoints (inventory, history) are stateless: all continuation state rides in an opaque cursor, and no server-side session is retained.

## Decision

The frontier rides in the cursor. The server retains no walk state between requests.

The cursor is a base64 JSON payload (the repository's existing cursor encoding) carrying: a format version; the bound traversal parameters (start, direction, maxDepth, includeUnavailable); the PublishedKnowledgeSnapshot id and the workspace journal position at issue; the pending frontier; and the current node's resume point. Frontier entries are compact: a pending expansion is the chain of edge references that produced it (published edges by their stored key, runtime edges by contribution id), from which its endpoint, its path steps, and its visited set are all re-derived against the bound snapshot and journal position. The node being expanded when the page filled is resumed through the neighbour reader's existing keyset position (`r.key > after`, per scope: published first, then runtime), so resuming mid-node never re-reads neighbours already emitted.

Two bounds keep this practical. The frontier cannot outgrow the walk: each emitted path enqueues at most one pending expansion, so at a page boundary the frontier holds at most 101 pending expansions, each a chain of at most maxDepth (≤ 8) references. And each request examines a bounded number of steps: a per-request examined-step budget (ten times the page size) ends the page early — short page, cursor issued — rather than scanning without limit, which is what keeps B17 true when many runtime neighbours are filtered out as unavailable.

## Consequences

Traversal stays read-only and stateless: no new tables, sessions, expiry, or cleanup; the Lambda/API deployment gains no stateful component; any API instance can serve any page. Cursors are larger than the inventory/history cursors — bounded by the frontier bound above, with a traversal-specific size cap on decode (a cursor over the cap is malformed, B9). Because steps are re-derived from edge references, a cursor is only as good as its binding: snapshot or journal movement is detected at validation and reported as `staleCursor` (B10, B11) instead of resolving references against moved data. The price of statelessness is that a client that loses its cursor restarts from page one; there is no server copy to recover.

## Alternatives considered

- **Server-retained walk state** (cursor as a session id, frontier rows in Postgres with expiry): smaller cursors, but adds stored state, a lifecycle (expiry, cleanup, orphaned walks), and a stateful element to a read-only query path. Rejected by the user at the architect phase, 21 November 2026, in favour of the repository's stateless precedent.
- **Deterministic replay** (re-walk from the start, discarding the first N paths): no frontier to carry, but per-request work grows with page number, which B16 explicitly forbids and its counting measure would catch. Rejected.
- **Keyset-only resumption without a frontier** (like inventory): traversal has no single sort key — the breadth-first order is produced by the queue, not by any stored ordering — so there is nothing to keyset against except the frontier itself. Rejected as inapplicable.
