---
date: 2026-11-07
slug: define-runtime-knowledge-workspace-architecture
title: "Define runtime knowledge workspace architecture"
summary: "- [ADR 0004](https://github.com/jamiemitchellconsultants/division-ontology/blob/b2e8ed75d151865ebaa2e608bc85f0abdb97e82e/docs/adr/0004-import-and-activate-pinned-knowledge-snapshots.md) packages pinned source artefacts and activates a…"
kind: product
status: accepted
sequence: 2026-11-07T14:07:34.000Z
evidence: "https://github.com/jamiemitchellconsultants/division-ontology/pull/54; merge commit 29bdfe6f6434f9984a680765c0ddd4a76d3ba366"
---

## Context

The workspace combines a baseline published from pinned source repositories with persistent runtime contributions. It needs reproducible graph imports, immediate traversal after a contribution is saved, and the same application contract on Mac, the home lab and AWS.

## Decision

- [ADR 0004](https://github.com/jamiemitchellconsultants/division-ontology/blob/b2e8ed75d151865ebaa2e608bc85f0abdb97e82e/docs/adr/0004-import-and-activate-pinned-knowledge-snapshots.md) packages pinned source artefacts and activates a validated baseline atomically.
- [ADR 0005](https://github.com/jamiemitchellconsultants/division-ontology/blob/b2e8ed75d151865ebaa2e608bc85f0abdb97e82e/docs/adr/0005-keep-graph-history-and-vectors-in-postgresql.md) keeps graph traversal, journal writes and vector search in PostgreSQL with Apache AGE and pgvector.
- [ADR 0006](https://github.com/jamiemitchellconsultants/division-ontology/blob/b2e8ed75d151865ebaa2e608bc85f0abdb97e82e/docs/adr/0006-configure-identity-and-embeddings-by-environment.md) selects Keycloak or Entra ID and Qwen3 or Bedrock Titan by environment.
- [ADR 0007](https://github.com/jamiemitchellconsultants/division-ontology/blob/b2e8ed75d151865ebaa2e608bc85f0abdb97e82e/docs/adr/0007-deploy-workspace-on-mac-home-lab-and-aws.md) defines full Docker Compose on Mac, LocalAI-operated home-lab deployment and Terraform on AWS with a Docker PostgreSQL instance.
- [ADR 0008](https://github.com/jamiemitchellconsultants/division-ontology/blob/b2e8ed75d151865ebaa2e608bc85f0abdb97e82e/docs/adr/0008-rebuild-workspace-from-snapshots-and-events.md) makes the contribution journal authoritative and rebuilds projections by replaying events and snapshot activations.

## Consequences

Development planning must prove that journal and AGE writes commit together, that replay preserves provenance and unavailable endpoints, and that search works with the selected model in each environment. AWS verification is simulation only. Backup and disaster recovery are tracked separately in #53.
