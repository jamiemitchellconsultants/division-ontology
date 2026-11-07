---
date: 2026-11-07
slug: plan-runtime-knowledge-workspace-implementation
title: "Plan runtime knowledge workspace implementation"
summary: "Use 12 test-first tasks. Prove the PostgreSQL and AGE transaction boundary, deterministic snapshot import, atomic contribution writes and ordered replay before connecting the API, identity providers, semantic search and Blazor UI."
kind: product
status: accepted
sequence: 2026-11-07T15:19:10.000Z
evidence: "https://github.com/jamiemitchellconsultants/division-ontology/pull/56; merge commit 96fecd8bfbd5857617684c5cf556c9a76d58f62a"
---

## Context

PR #54 approved a workspace that combines pinned feeder snapshots with a PostgreSQL contribution journal, AGE traversal and pgvector search. The build needs a reviewed sequence that proves those contracts before the UI and three deployment patterns depend on them.

## Decision

Use 12 test-first tasks. Prove the PostgreSQL and AGE transaction boundary, deterministic snapshot import, atomic contribution writes and ordered replay before connecting the API, identity providers, semantic search and Blazor UI. Then assemble Mac Docker Compose, LocalAI deployment artefacts and a Terraform AWS simulation. The detailed plan specifies interfaces, fixtures, failure cases, commands and commit boundaries.

## Consequences

Development can verify the first 13 and last 11 behaviours with engineering fixtures. B14 remains a human acceptance measure with the nominated data product owner and wider-business reviewer. AWS verification remains credential-free simulation. Backup and disaster recovery remain in #53.
