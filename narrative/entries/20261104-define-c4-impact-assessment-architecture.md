---
date: 2026-11-04
slug: define-c4-impact-assessment-architecture
title: "Define C4 impact assessment architecture"
summary: "Accept the [architecture](https://github.com/jamiemitchellconsultants/division-ontology/blob/feature/c4-diagrams-from-source-knowledge-graphs/architect/docs/features/c4-diagrams-from-source-knowledge-graphs/03-architecture.md): a local…"
kind: product
status: accepted
sequence: 2026-11-04T14:51:50.000Z
evidence: "https://github.com/jamiemitchellconsultants/division-ontology/pull/43; merge commit 88a327f409155e0422787c33609f470812fe8ad2"
---

## Context

The accepted [C4 impact behaviours](https://github.com/jamiemitchellconsultants/division-ontology/pull/41) require evidence-backed paths across participating repositories, four C4 levels where source facts permit, reproducible source revisions, and explicit gaps. The current catalog publishes ontology concepts and source commits but does not hold source graph instances or assessments. The first pilot needs an architecture that can be evaluated before a hosted workspace is chosen.

## Decision

Accept the [architecture](https://github.com/jamiemitchellconsultants/division-ontology/blob/feature/c4-diagrams-from-source-knowledge-graphs/architect/docs/features/c4-diagrams-from-source-knowledge-graphs/03-architecture.md): a local Node.js CLI reads source graphs at the published catalog's pinned commits and creates deterministic JSON and Markdown/Mermaid views plus architect-authored, reviewable assessments. Reviewed C4 mappings determine levels and boundaries; reviewed cross-source bindings connect an evidenced external node to a participating source node without merging source identities. These decisions are recorded in [ADR 0001](https://github.com/jamiemitchellconsultants/division-ontology/blob/feature/c4-diagrams-from-source-knowledge-graphs/architect/docs/adr/0001-local-pinned-c4-pilot.md), [ADR 0002](https://github.com/jamiemitchellconsultants/division-ontology/blob/feature/c4-diagrams-from-source-knowledge-graphs/architect/docs/adr/0002-reviewed-c4-mappings.md), and [ADR 0003](https://github.com/jamiemitchellconsultants/division-ontology/blob/feature/c4-diagrams-from-source-knowledge-graphs/architect/docs/adr/0003-reviewed-cross-source-bindings.md).

## Consequences

The developer plan can use the existing catalog as the source inventory and revision lock while keeping graph facts source-owned and authored mappings separate from generated outputs. Missing graphs, unresolved mappings and absent relationships appear as gaps, not evidence of no impact. JointBooking currently has no source graph in its checkout, so source-backed C4 pilot paths remain unavailable until it publishes one. The local pilot requires read access to the participating source commits; a later workspace can consume the same view model.
