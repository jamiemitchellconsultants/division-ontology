---
date: 2026-11-24
slug: plan-pinned-c4-schema-2-source-graph-import-175
title: "Plan pinned C4 schema 2 source graph import (#175)"
summary: "Build the C4 version, evidence role and design provenance checks in one test-first increment so the loader only accepts schema 2 once it can preserve its records."
kind: product
status: accepted
sequence: 2026-11-24T11:49:32.000Z
evidence: "https://github.com/jamiemitchellconsultants/division-ontology/pull/178; merge commit f1dabcfecc2f2e74d78ca6a2acc21dcd23d161a7"
---

## Context

The current C4 loader handles the three representative legacy source graphs. The compatibility investigation found that it rejects a standard kit schema 1 evidence role and stops at the manifest guard for both schema 2 fixtures. The workspace producer and reader also assume version 1. [Approved design and decisions](https://github.com/jamiemitchellconsultants/division-ontology/pull/176)

## Decision

Build the C4 version, evidence role and design provenance checks in one test-first increment so the loader only accepts schema 2 once it can preserve its records. Build the workspace producer and runtime reader change together in a second increment so each source's reported version survives the existing bundle contract.

## Consequences

Each increment has focused compatibility tests and a disposable catalog check. The final build must pass the synthetic and pinned JEPA-Speech comparisons through the real C4 loader. Owner walkthroughs and approval remain in the User phase.

---

AI-Fingerprint: sha256:06670a463778
