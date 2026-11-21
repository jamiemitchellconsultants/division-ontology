# 0022 — Keep paged graph view state in the browser

- **Status** accepted
- **Date** 2026-11-21
- **Feature** [#145](https://github.com/jamiemitchellconsultants/division-ontology/issues/145)

## Context

The explorer needs to restore a previous view with its direction, viewport, and loaded neighbours. The traversal API binds a continuation to its parameters, published snapshot, and journal position. A continuation can outlast an empty page. A late response or a rejected continuation must leave the user on the correct node and offer a way forward. [Behaviours B2–B5, B8–B10, B13](../features/knowledge-graph-explorer/02-behaviours.md) · [Traversal cursor ADR](0020-bind-traversal-cursors-to-snapshot-and-journal.md)

## Decision

Keep the current one-hop query, loaded records, and a Back stack in memory for the open explorer. The route encodes the entry entity. The in-page session owns focus changes, selection, and view restoration. Capture positions and viewport when leaving a successful frame. Merge subsequent pages into that frame by stable identity and follow `NextCursor` with unchanged parameters until the API signals completion.

Bind each request to its initiating query and selection. Accept only matching completions and serialize continuation within a frame. Render stale state when a continuation fails because the server view has changed. A reload starts on page one with the same query. An observed snapshot change or successful contribution invalidates older frames, which reload on return. Keep temporary read failures retryable and release view state when the explorer is left.

## Consequences

The browser can preserve the reader’s place without a server navigation session. Memory grows with the results a reader chooses to load and the successful views they keep in Back history. Page requests remain explicit, while stale detection follows the existing server responses and writes observed by the open explorer. A new entry or reload of the web app begins a new graph navigation session. The existing contribution-draft flow handles rejected edits separately.

## Alternatives considered

- **Always reload a node on Back.** It would discard loaded pages and the viewport each time, and busy-node restoration would require replaying earlier requests.
- **Store complete view history in URLs or browser storage.** The route only needs the entry identity. Persisting loaded records and continuations would enlarge the storage boundary and preserve stale state across sessions.
- **Track graph navigation on the server.** The existing cursor already carries server continuation state. A server navigation session would add persistence for presentation state.
