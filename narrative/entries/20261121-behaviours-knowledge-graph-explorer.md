---
date: 2026-11-21
slug: behaviours-knowledge-graph-explorer
title: "Behaviours: Knowledge Graph Explorer"
summary: "Start with both directions and show one hop at a time. Preserve the previous view when users navigate Back, and offer the same exploration through a keyboard-accessible neighbour list."
kind: product
status: accepted
sequence: 2026-11-21T18:31:57.000Z
evidence: "https://github.com/jamiemitchellconsultants/division-ontology/pull/163; merge commit 3a8a35d296677375d16326c345be6ee32004e9ad"
---

## Context

The accepted vision calls for a graph centred on a published entity, with readable provenance and workspace-only editing. The explorer needs predictable navigation and a way to explain empty, incomplete, or stale results. The traversal API can issue a continuation after an empty page and invalidate it when the published snapshot or workspace changes.

## Decision

Start with both directions and show one hop at a time. Preserve the previous view when users navigate Back, and offer the same exploration through a keyboard-accessible neighbour list. Keep published and workspace relationships independently inspectable. On a stale continuation, mark the displayed view out of date and offer a reload from page one. Use the existing manager actions for annotations and runtime relationships. All user edits are stored only in the runtime workspace. Turtle files in the adopting and source repositories are read-only inputs.

## Consequences

The architecture must account for focus, viewport and paging state, overlapping requests, and accessible relationship selection. Acceptance will use the Order Fulfilment journey alongside busy-node, stale-page, and manager contribution cases. The scenarios preserve existing authorisation, validation, and contribution history.

---

AI-Fingerprint: sha256:8c6c150c96f8
