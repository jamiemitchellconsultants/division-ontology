---
date: 2026-11-21
slug: docs-architecture-for-traversal-cursor-pagination-122
title: "docs: architecture for traversal cursor pagination (#122)"
summary: "[ADR 0019](docs/adr/0019-carry-the-traversal-frontier-in-the-cursor.md): the frontier rides in the cursor as compact edge-reference chains — at most 101 pending expansions at a page boundary, each re-deriving its endpoint, steps, and…"
kind: product
status: accepted
sequence: 2026-11-21T10:32:09.000Z
evidence: "https://github.com/jamiemitchellconsultants/division-ontology/pull/132; merge commit 963b54a399d4ecace32b7d95382f86265a0f6e1d"
---

## Context

The accepted behaviours left the resumption mechanism open (U6): how a breadth-first walk continues exactly-once across a branch boundary without replaying earlier pages, what a cursor binds and in what order it is validated, and how the bounded-work measures (B16/B17) can be met when many neighbours are filtered out. The existing walk discards its frontier at the 100-path cap; inventory and history set a stateless-cursor precedent but have no frontier to carry.

## Decision

[ADR 0019](docs/adr/0019-carry-the-traversal-frontier-in-the-cursor.md): the frontier rides in the cursor as compact edge-reference chains — at most 101 pending expansions at a page boundary, each re-deriving its endpoint, steps, and visited set — resumed mid-node through the neighbour reader's existing keyset position; a per-request examined-step budget (ten times the page size) ends a page early instead of scanning without limit. Server-retained walk state and deterministic replay were considered and rejected (replay is exactly what B16 forbids and measures).

[ADR 0020](docs/adr/0020-bind-traversal-cursors-to-snapshot-and-journal.md): the cursor binds the traversal parameters, the snapshot id, and the workspace journal position; validation runs shape → parameter match → view freshness, so a parameter mismatch reports 422 `invalidCursor` and a moved-on view reports 409 `staleCursor`, through the existing status map with no new codes. Any contribution append invalidates outstanding cursors.

## Consequences

The developer can plan without re-deciding structure: contract additions are additive (`Cursor`/`NextCursor`), the change is confined to Contracts, Store, Api, and their tests, and deployment gains nothing stateful — no tables, migrations, or expiry jobs. The coarse any-edit invalidation is accepted deliberately (cheap, auditable); relaxing it would need a new cursor version and a behaviour change. The PR #117 link conventions extend to the traversal `next` link, and the spec must document both page examples and cursor errors.

---

AI-Fingerprint: sha256:b31d95e4a3cb
