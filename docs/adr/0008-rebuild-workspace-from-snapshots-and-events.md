# 0008 — Rebuild the workspace from snapshots and contribution events

- **Status:** accepted
- **Date:** 2026-11-07
- **Feature:** [#25](https://github.com/jamiemitchellconsultants/division-ontology/issues/25) ([architecture](../features/runtime-knowledge-workspace/03-architecture.md))

## Context

The division catalog and source graphs can be reconstructed from pinned feeder repo artefacts. Annotations, runtime relationships and codification associations originate in the workspace and must survive feeder refreshes and redeployments. The workspace must also make a successful change and its history visible immediately.

## Decision

Make an append-only PostgreSQL journal authoritative for workspace contributions. Record successful changes as versioned events with their stable identities, acting principal, affected content and original snapshot provenance. Record snapshot activations in the same committed sequence. Append the event and update current SQL and AGE projections in one transaction.

Preserve the journal across deployments and keep the immutable bundles it references available. Rebuild the graph and current contribution views from the ordered activation and contribution records, then regenerate the search index. Replay recorded outcomes and preserve unavailable endpoints, removals and last-known evidence.

## Consequences

The published projection can be replaced while preserving the workspace's authored data. Replay must understand stored event versions and check for duplicates, gaps and missing bundles. Verify that a clean rebuild from the available journal and bundles reproduces the active snapshot, contribution history and traversal before returning it to service. Backup and disaster recovery for those inputs are tracked in [follow-up #53](https://github.com/jamiemitchellconsultants/division-ontology/issues/53).

## Alternatives considered

- Store user edits only in the current graph projection. A feeder rebuild would then depend on the existing graph database to recover changes and their original order.
- Use an external event store on the live write path. This would add coordination across storage systems to preserve the atomic save and immediate traversal guarantees.
