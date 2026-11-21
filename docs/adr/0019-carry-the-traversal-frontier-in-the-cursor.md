# 0019 — Carry the traversal frontier in the cursor

- **Status:** accepted
- **Date:** 2026-11-21
- **Feature:** #119 (docs/features/traversal-cursor-pagination/)

## Context

Paged traversal must resume a breadth-first walk exactly where the previous page stopped — including partway through expanding one node, with other branches still queued (B2, B12) — while keeping per-request work bounded independent of page number (B16, B17) and the traversal read-only (B18). The walk's resumption state is its frontier: the pending node expansions plus the partially expanded current node. That state has to live somewhere between requests. The repository's existing paged endpoints (inventory, history) are stateless: all continuation state rides in an opaque cursor, and no server-side session is retained.

## Decision

The frontier rides in the cursor. The server retains no walk state between requests.

The cursor payload carries: a format version; the bound traversal parameters (start, direction, maxDepth, includeUnavailable); the PublishedKnowledgeSnapshot id and the workspace journal position at issue; the pending frontier; and the current node's resume point. It is dictionary-coded — each distinct edge reference (published edges by their stored key, runtime edges by contribution id) is stored once, and every path chain is a list of indexes into that dictionary, so shared breadth-first prefixes cost once — then JSON-serialised, deflate-compressed, and base64-encoded. From a chain, its endpoint, its path steps, and its visited set are all re-derived against the bound snapshot and journal position. The node being expanded when the page filled is resumed through the neighbour reader's keyset position (`r.key > after`, per scope: published first, then runtime), where `after` is the last *processed* neighbour — emitted or filtered — so resuming mid-node never re-reads a row already examined, including rows filtered out as unavailable. (Amended after developer-plan pushback: review showed the breadth-first queue can grow across pages while a node is still being expanded, and an uncompressed payload with fixture-style keys exceeded the original size cap by page 13 of a depth-2 walk.)

Two bounds keep this practical, and neither may be left to chance. **Cursor size:** decode rejects a submitted cursor whose compressed form exceeds 262144 characters, and rejects decompressed JSON over 8388608 bytes mid-stream, so a compression bomb cannot expand without limit. The encoder asserts the same compressed cap before issuing, so no server-issued cursor is ever undecodable by the server's own cap; a wide multi-page fixture that submits every issued cursor is the regression proof. The queue-growth argument from the original decision is withdrawn: pending expansions can accumulate across pages while a wide node is still being expanded, which is exactly why the encoding is dictionary-coded and compressed rather than merely capped. **Examined work:** a per-request budget of 1000 examined rows (ten times the page size) is enforced at fetch time — each neighbour fetch is limited to the remaining allowance, and every returned row counts, emitted or filtered — and exhausting it ends the page early (short or even empty page, cursor issued, resume position already past every processed row) rather than scanning without limit, which is what keeps B17 true when many runtime neighbours are filtered out as unavailable.

## Consequences

Traversal stays read-only and stateless: no new tables, sessions, expiry, or cleanup; the Lambda/API deployment gains no stateful component; any API instance can serve any page. Cursors are larger than the inventory/history cursors — bounded by the compressed and decompressed caps above (a cursor over either cap is malformed, B9) and by the encoder's issue-side assertion of the same cap. Because steps are re-derived from edge references, a cursor is only as good as its binding: snapshot or journal movement is detected at validation and reported as `staleCursor` (B10, B11) instead of resolving references against moved data. The price of statelessness is that a client that loses its cursor restarts from page one; there is no server copy to recover.

## Alternatives considered

- **Server-retained walk state** (cursor as a session id, frontier rows in Postgres with expiry): smaller cursors, but adds stored state, a lifecycle (expiry, cleanup, orphaned walks), and a stateful element to a read-only query path. Rejected by the user at the architect phase, 21 November 2026, in favour of the repository's stateless precedent.
- **Deterministic replay** (re-walk from the start, discarding the first N paths): no frontier to carry, but per-request work grows with page number, which B16 explicitly forbids and its counting measure would catch. Rejected.
- **Keyset-only resumption without a frontier** (like inventory): traversal has no single sort key — the breadth-first order is produced by the queue, not by any stored ordering — so there is nothing to keyset against except the frontier itself. Rejected as inapplicable.
