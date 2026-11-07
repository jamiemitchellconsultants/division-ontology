# 0005 — Keep graph, contribution history and vectors in PostgreSQL

- **Status:** accepted
- **Date:** 2026-11-07
- **Feature:** [#25](https://github.com/jamiemitchellconsultants/division-ontology/issues/25) ([architecture](../features/runtime-knowledge-workspace/03-architecture.md))

## Context

A successful contribution must be immediately readable and traversable with its history. Runtime changes must survive missing published endpoints and preserve provenance. Semantic search needs a model-specific vector index.

## Decision

Run PostgreSQL with Apache AGE and pgvector in Docker in all three environments. Use relational tables for the durable journal and rebuildable views of contribution identity, current version and snapshot state. Use AGE as the traversal projection and pgvector for semantic retrieval. [ADR 0008](0008-rebuild-workspace-from-snapshots-and-events.md) defines the replay contract.

Validate contribution versions and the active snapshot within the write transaction. Commit the event, current state, traversal changes and index jobs together. Start with exact cosine search. Index jobs can retry independently and search filters out stale vectors using the current record version.

## Consequences

Graph and history writes share one transaction boundary. Search freshness depends on the index worker. Both extensions must be present in the pinned database image, and application verification must exercise SQL and AGE changes inside the same transaction, including rollback and concurrent activation. The home-lab 4,096-dimensional model requires exact search or a measured alternative before introducing an approximate index.

## Alternatives considered

- Separate the graph and contribution databases. This would require additional coordination to guarantee immediate traversal and history together.
- Use a dedicated vector database from the start. This adds a deployment and synchronisation dependency before measured search performance calls for it.
