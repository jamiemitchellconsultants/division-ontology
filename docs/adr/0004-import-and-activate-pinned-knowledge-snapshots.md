# 0004 — Import and activate pinned knowledge snapshots

- **Status:** accepted
- **Date:** 2026-11-07
- **Feature:** [#25](https://github.com/jamiemitchellconsultants/division-ontology/issues/25) ([architecture](../features/runtime-knowledge-workspace/03-architecture.md))

## Context

The division catalog pins the source commits for one published ontology. The C4 pilot uses those same commits to find optional source graphs and inspect their evidence. The workspace needs a reproducible baseline, visible coverage gaps and durable identities for contributions across publication changes.

## Decision

Package each baseline as an immutable bundle identified by the division commit and manifest digest. Record source commits, artefact digests and the graph schema versions. Verify any source graph at its catalog-pinned commit and preserve its source IRIs, relationship types, evidence and coverage. Store the bundle in MinIO or S3.

Project the verified bundle into a candidate snapshot in PostgreSQL. Activate the candidate by changing the active pointer and appending its activation record in one transaction. A failed candidate leaves the current pointer intact. Resolve availability against the selected snapshot using stable source identities.

## Consequences

The same verified inputs can be replayed, and queries can name their precise baseline. The publisher needs read access to pinned source artefacts and the referenced evidence. Keep the immutable bundles referenced by activation records available for replay. Optional missing graphs produce coverage gaps. Malformed published graphs fail staging. See [ADR 0008](0008-rebuild-workspace-from-snapshots-and-events.md).

## Alternatives considered

- Load source branch heads during requests. Separate source reads can mix versions and make results hard to reproduce.
- Replace the live graph in place. A partial failure can expose an incomplete baseline and make endpoint changes harder to reconcile with contribution writes.
