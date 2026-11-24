---
date: 2026-11-24
slug: design-c4-schema-2-source-graph-import-175
title: "Design C4 schema 2 source graph import (#175)"
summary: "Use the existing catalog-pinned C4 loader to validate schemas 1 and 2 and resolve evidence from the pinned configuration. Add design status and evidence references to its imported design records."
kind: product
status: accepted
sequence: 2026-11-24T06:26:53.000Z
evidence: "https://github.com/jamiemitchellconsultants/division-ontology/pull/176; merge commit e6bdd37cb084b980288b1e42b9e01a9a3cc28726"
---

## Context

The published division loader accepts its three legacy dummy graphs. The standard kit schema 1 control fails evidence-role matching, and the pinned synthetic and JEPA-Speech schema 2 graphs fail at the schema check. The workspace bundle producer and reader also assume graph schema 1 for each available source. [C4 version and role decision](https://github.com/jamiemitchellconsultants/division-ontology/blob/2fc5de182bd8a4df621e73379919a8a0c59d2434/docs/adr/0024-version-source-graph-validation-at-the-c4-boundary.md)

## Decision

Use the existing catalog-pinned C4 loader to validate schemas 1 and 2 and resolve evidence from the pinned configuration. Add design status and evidence references to its imported design records. Carry the actual source manifest version through the existing workspace bundle field and accept it in the current envelope. [C4 validation ADR 0024](https://github.com/jamiemitchellconsultants/division-ontology/blob/2fc5de182bd8a4df621e73379919a8a0c59d2434/docs/adr/0024-version-source-graph-validation-at-the-c4-boundary.md) [Design provenance ADR 0025](https://github.com/jamiemitchellconsultants/division-ontology/blob/2fc5de182bd8a4df621e73379919a8a0c59d2434/docs/adr/0025-preserve-design-provenance-in-c4-snapshots.md) [Workspace metadata ADR 0026](https://github.com/jamiemitchellconsultants/division-ontology/blob/2fc5de182bd8a4df621e73379919a8a0c59d2434/docs/adr/0026-record-the-actual-source-graph-version-in-workspace-bundles.md)

## Consequences

Existing graphs and bundle envelopes retain their established meaning. The importer can preserve the source data needed to compare design records and trace both B18 routes in a disposable division catalog. The developer plan will cover strict failure cases, downstream bundle validation and the pinned JEPA-Speech acceptance boundary.

AI-Fingerprint: sha256:29546816b0f6
