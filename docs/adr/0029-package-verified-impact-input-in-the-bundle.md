---
status: accepted
date: 2026-11-26
feature: 198
---

# 0029 — Package verified impact input in the snapshot bundle

Feature: docs/features/generalise-impact-assessment/

## Context

The .NET engine (ADR 0028) needs nodes, assertions, evidence, coverage, C4 mappings and bindings, reviewed links and the master list, all verified at their pinned commits. Today the Node loaders do that verification: they check checkout identity, Git paths, blob hashes, line ranges, role selectors, coverage and the graph schema version (ADR 0024). The workspace bundle (ADR 0004) already holds the cited evidence bytes, but its projection lacks coverage inventory, node-level evidence, link kinds and master-list directions. B23 needs both runners to start from the same input.

## Decision

Keep verification in Node, and make the bundle the engine's only input, on the command line and in the workspace alike.

- `buildBundle` adds a file role `impactInput` (`impact-input.json`). It holds canonical JSON of the verified facts: per source, its status, schema version, nodes, assertions, evidence records and coverage; the verified C4 mappings, bindings and their gaps, with the registry commit and digests; and the catalog's reviewed links.
- It also adds the role `relationshipMasterList` when the list exists (ADR 0032).
- The manifest stays at schema version 1, because both roles are additive. `BundleReader` accepts them.
- The engine checks the manifest and object digests, then builds its model. It never parses Turtle and never reads Git.

## Consequences

- The same verified bundle bytes reach the command line and the worker, so their inputs are identical by construction.
- Graph and evidence validation stay in one place (ADR 0024), and the engine stays free of I/O.
- A bundle built before this feature has no `impactInput`. Impact routes report `impactUnavailable` for its snapshot until a new bundle is published.
- The `impactInput` schema becomes a contract between Node and .NET, and it is versioned inside the file.

## Alternatives considered

- **Parse Turtle and verify Git in .NET.** This duplicates the ADR 0024 validation boundary in a second language.
- **Derive the engine's input from the existing `projection.json`.** The projection is shaped for traversal display. Extending it would couple explorer and impact changes, and it would still need coverage and link kinds.
