---
date: 2026-11-24
slug: import-schema-2-source-graphs-in-c4-snapshots-175
title: "Import schema 2 source graphs in C4 snapshots (#175)"
summary: "Interpret the two supported source schemas at the existing local Git snapshot boundary. Preserve design provenance in the graph maps and carry the validated per-source schema version through the existing workspace bundle field."
kind: product
status: accepted
sequence: 2026-11-24T13:18:53.000Z
evidence: "https://github.com/jamiemitchellconsultants/division-ontology/pull/185; merge commit 52238ecfaa39e0c90ab2d68bf63b2ede5f9010f6"
---

## Context

InternalOntology #185 needs to trace a behaviour in the pinned JEPA-Speech graph to its recorded code. The published C4 loader refused schema 2 and a standard schema 1 evidence role, while the three representative legacy graphs masked both gaps. [Division feature #175](https://github.com/jamiemitchellconsultants/division-ontology/issues/175)

## Decision

Interpret the two supported source schemas at the existing local Git snapshot boundary. Preserve design provenance in the graph maps and carry the validated per-source schema version through the existing workspace bundle field. Use synthetic controls and the disposable JEPA-Speech catalog to verify the importer. [ADRs 0024–0026](https://github.com/jamiemitchellconsultants/division-ontology/pull/176)

## Consequences

Current kit graphs and existing legacy graphs use the same import path. The importer preserves design evidence and supports the original direct and component-mediated traces. A runtime must have the compatible reader before it receives a bundle with a schema 2 source. The post-merge trial will record owner acceptance from the merged importer.

---

AI-Fingerprint: sha256:c71ec951eed0
