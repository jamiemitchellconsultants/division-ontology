---
date: 2026-11-21
slug: vision-knowledge-graph-explorer
title: "Vision: Knowledge Graph Explorer"
summary: "Add a graph explorer centred on a published concept or node. Readers can follow directed relationships, inspect evidence and provenance, and return to their previous view. Managers can use existing contribution actions from the graph."
kind: product
status: accepted
sequence: 2026-11-21T13:20:25.000Z
evidence: "https://github.com/jamiemitchellconsultants/division-ontology/pull/153; merge commit cb48f02df11e78b3c976de6c301fa542184be342"
---

## Context

Workspace readers can traverse relationships as a list today. Following a cross-repository handoff and opening evidence can make it difficult to keep track of the current concept, previous steps and similarly named entities. The workspace already records the source and meaning of relationships and supports user-authored contributions.

## Decision

Add a graph explorer centred on a published concept or node. Readers can follow directed relationships, inspect evidence and provenance, and return to their previous view. Managers can use existing contribution actions from the graph. All user changes are stored only in the runtime workspace. Turtle files in the adopting repository and all source repositories are read-only inputs.

## Consequences

The first version uses navigation history to restore earlier views. The scope excludes a persistent route canvas and a whole-division map. The explorer depends on traversal pagination from #119 for busy nodes. The analyst will define behaviour for navigation, evidence inspection, loading additional results and workspace edits.

---

AI-Fingerprint: sha256:38095f8eaaa4
