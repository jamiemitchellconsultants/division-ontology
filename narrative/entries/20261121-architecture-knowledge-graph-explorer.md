---
date: 2026-11-21
slug: architecture-knowledge-graph-explorer
title: "Architecture: Knowledge Graph Explorer"
summary: "Host the explorer in the existing authorized Blazor workspace and reuse its API client, contribution editor, and draft flow."
kind: product
status: accepted
sequence: 2026-11-21T19:38:15.000Z
evidence: "https://github.com/jamiemitchellconsultants/division-ontology/pull/165; merge commit 4203c4a4d365486d0629867ea49d7e9fd6d23014"
---

## Context

The accepted behaviours call for one-hop navigation with stable identity, equivalent keyboard controls, a recoverable paging state, and existing workspace contributions. The traversal and detail contracts support browsing, and the contribution API already applies workspace roles and records the changes.

## Decision

Host the explorer in the existing authorized Blazor workspace and reuse its API client, contribution editor, and draft flow. Keep the loaded neighbourhood and Back history in browser memory, and use the traversal cursor for additional pages. Share the same records and actions between a locally bundled Cytoscape diagram and native HTML controls. All user edits are stored only in the runtime workspace. Turtle files in the adopting and source repositories are read-only inputs.

- [ADR 0021 — Host the explorer in the existing workspace](https://github.com/jamiemitchellconsultants/division-ontology/blob/ed27f0c3c9991d292bb699d146b8fbcd4217a11e/docs/adr/0021-host-the-explorer-in-the-existing-workspace.md)
- [ADR 0022 — Keep paged graph view state in the browser](https://github.com/jamiemitchellconsultants/division-ontology/blob/ed27f0c3c9991d292bb699d146b8fbcd4217a11e/docs/adr/0022-keep-paged-graph-view-state-in-the-browser.md)
- [ADR 0023 — Render the graph with a local Cytoscape module and HTML controls](https://github.com/jamiemitchellconsultants/division-ontology/blob/ed27f0c3c9991d292bb699d146b8fbcd4217a11e/docs/adr/0023-render-the-graph-with-a-local-cytoscape-module-and-html-controls.md)

## Consequences

The existing static web packages carry the renderer and its license. The browser keeps view state for the open explorer and reloads data after an observed change invalidates it. The developer plan will cover the Order Fulfilment journey, keyboard and pointer interaction, continuation and stale recovery, and versioned manager contributions.

---

AI-Fingerprint: sha256:cf121adee991
