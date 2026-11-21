# Traversal cursor pagination — product vision

**Feature:** [#119](https://github.com/jamiemitchellconsultants/division-ontology/issues/119)

**Phase:** [#120 — Product owner vision](https://github.com/jamiemitchellconsultants/division-ontology/issues/120)

**Status:** Accepted by the product owner on 21 November 2026; pending review and merge of the product-owner phase pull request.

## Problem

Workspace users exploring a well-connected entity through `POST /api/traverse` only ever see the first 100 paths. The response says `truncated`, but there is no way to ask for the rest: relationships beyond that boundary — including workspace-authored relationships that sort after the published ones — are unreachable through the API, so a busy node looks smaller and less connected than it is. People and agents reading the graph cannot tell whether they have seen everything, and cannot finish the job when they have not.

## Users

- **Knowledge reader:** an authenticated user with the `ReadKnowledgeGraph` role exploring a neighbourhood of the graph — typically an architect tracing what a busy entity connects to, one or more hops away.
- **Agent or API client:** a programmatic consumer (including LLM tools generated from the OpenAPI spec) that pages through traversal results and follows the response's `next` link instead of constructing requests itself.
- **Knowledge contributor:** a `ManageKnowledgeGraph` user whose workspace-authored relationships must remain visible to readers in traversal, on equal footing with — but distinguishable from — published relationships.

## Outcomes and measures

1. **Every path is reachable.** Measure: against unchanged data, paging from a node with more than 100 published paths (plus at least one workspace-authored path beyond the first page) returns each path exactly once, and the final page is recognisably the last. A dedicated multi-hop acceptance fixture makes this exact at a branch boundary: the first page ends partway through expanding one branch of the breadth-first walk while other branches are still awaiting expansion, and the paths returned by all pages, concatenated in order, must match the fixture's expected path sequence exactly once — no branch lost, repeated, or reordered at the page break.
2. **Existing clients are undisturbed.** Measure: a first-page request behaves exactly as today, including the `truncated` field; clients that never send a cursor see no breaking change in the response shape beyond the additive `nextCursor` field.
3. **Pages are self-contained.** Measure: each page carries the entity context needed to render it (labels, provenance, availability and annotations for the entities on that page), without a follow-up request per entity.
4. **Continuation is trustworthy.** Measure: a cursor only continues the traversal it was issued for — the same start, direction, depth and unavailable-endpoint option, over the same published snapshot and relevant workspace data. If that view can no longer be continued, the client receives a clear error that tells it to restart from page one, rather than silently different results.
5. **Agents can drive paging without guesswork.** Measure: whenever another page exists, the response carries a HATEOAS `next` link that tells the client how to call the same endpoint with the cursor and the original parameters; the final page omits it. The committed OpenAPI spec documents the cursor request field, the `nextCursor` response field, the cursor errors, and first-page and next-page examples.
6. **Continuation stays cheap, at any page.** Measure: the per-request costs a user can feel — response size, response time, and the amount of graph data read to produce one page — stay bounded by the page, not by how far into the walk the page is or by how much data had to be passed over to fill it. Two fixture measures enforce this: requesting a late page must not cost proportionally to the pages before it (the fixture counts the relationships examined to serve each page and requires that count to stay within a small bound of the page size, independent of page number, which replaying earlier pages would fail); and a page surrounded by large numbers of unavailable relationships that are filtered out must still be filled without examining an arbitrarily growing number of them (the same count, measured as the number of unavailable relationships grows, stays within the stated bound). The 100-path page size bounds the number of returned paths; these measures bound the work done to find them.

## Scope

- Cursor pagination of `POST /api/traverse`: an opaque `nextCursor` in the response when another page exists, submitted back to the same endpoint to continue.
- One-hop and multi-hop traversal, both directions (`outgoing`, `incoming`, `both`), and the existing include-unavailable option, all continuable across pages with path order preserved.
- Published and workspace-authored edges remain separately identifiable across pages (published key vs runtime id, and their provenance).
- Bounded work per request: the current 100-path limit becomes the page boundary rather than a hard stop, continuation must not require unbounded server-side state, and the work to produce a page must satisfy the measurable bounds in outcome 6 however deep the page or however many unavailable relationships surround it.
- Cursor failure behaviour is defined and visible to clients: malformed cursors, cursors reused with different traversal parameters, and cursors whose underlying view has moved on (snapshot refresh or relevant workspace edit) all fail clearly and instruct the client to restart from page one.
- API contract update: regenerated committed OpenAPI spec, verified to match the generated output, with a client able to follow the response `next` link to the next page.
- Read-only traversal: paging never writes. All user edits stay in the workspace's runtime data; Turtle files in this repository and in source repositories remain read-only inputs.

## Out of scope

- Graph UI changes: no Blazor or other UI work to consume paging in this feature.
- Changes to traversal semantics themselves: which paths exist, their ordering beyond preserving it across pages, depth limits, and the meaning of `truncated` for first-page clients all stay as they are.
- Pagination of any other endpoint (inventory and history are already cursor-paginated).
- Any new editing capability, and any change to how workspace contributions are authored, edited or codified.
- Editing or regenerating Turtle ontology files in this or any source repository.

## Research

- The current traversal implementation is a breadth-first walk capped at 100 paths in [`GraphQueries.cs`](../../../runtime/RuntimeKnowledge.Store/GraphQueries.cs): it stops at the cap, sets `truncated`, and discards the remaining frontier, so continuation needs the walk's resumption behaviour redesigned rather than a new limit. The same file's inventory query is the in-repo precedent for cursor paging: its cursor binds the snapshot and page size and yields a `staleCursor` error telling the client to start a new query.
- Contribution history provides a second precedent in [`Contributions.cs`](../../../runtime/RuntimeKnowledge.Store/Contributions.cs): its cursor embeds the query's filter, and a cursor reused with a different filter fails `invalidCursor` — the pattern this feature's parameter-binding and failure definitions should be consistent with.
- The committed OpenAPI spec's `traverseGraph` operation (see [`workspace-api.json`](../../../runtime/openapi/workspace-api.json)) documents no cursor field and no cursor errors today, and its HATEOAS links cover only entity and relationship detail; the recent agent-decorators work on that spec (PR #117) established the `next`-link and operation-link conventions this feature extends.
- The feature request itself fixes the remaining constraints: preserve first-page behaviour and `truncated`, keep published and runtime edges separately identifiable, keep per-request work bounded, keep traversal read-only, and keep graph UI out of scope.
