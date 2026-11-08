---
date: 2026-11-08
slug: build-the-runtime-knowledge-workspace
title: "Build the runtime knowledge workspace"
summary: "Store publication and contribution events in the durable PostgreSQL journal and preserve the content-addressed bundles referenced by it. Use separate database roles for the API, publisher and indexer."
kind: product
status: accepted
sequence: 2026-11-08T07:35:45.000Z
evidence: "https://github.com/jamiemitchellconsultants/division-ontology/pull/58; merge commit 13bf786e30dd6b924760944114810a72bf564dce"
---

## Context

The workspace combines pinned feeder publications with business contributions that need to survive an application redeploy. The approved deployment patterns share one graph and contribution model while their identity, object storage and embedding providers differ.

## Decision

Store publication and contribution events in the durable PostgreSQL journal and preserve the content-addressed bundles referenced by it. Use separate database roles for the API, publisher and indexer. In the AWS design, separate staging and activation of a manifest across bounded publisher runs while the indexer prepares the baseline.

## Consequences

A guarded cutover compares the rebuilt and active state at the same journal head before switching generations. The checked-in deployment artefacts support Mac Docker and LocalAI installation, and the AWS target has simulation evidence. Recovery procedures for damaged state and the inputs to replay remain with #53. The user phase records the live home-lab and business acceptance evidence.

---

AI-Fingerprint: sha256:416d04d25d3c
