---
date: 2026-11-22
slug: build-knowledge-graph-explorer
title: "Build Knowledge Graph Explorer"
summary: "Ship one authorized Blazor graph route over the existing traversal and detail APIs. The local Cytoscape module and native neighbour controls share stable node and edge identities."
kind: product
status: accepted
sequence: 2026-11-22T09:41:08.000Z
evidence: "https://github.com/jamiemitchellconsultants/division-ontology/pull/171; merge commit bc29ce0dc5be7e0ca1d7d360cbf4d9dd7ffd8225"
---

## Context

The workspace provided search, traversal, and contribution editing on separate views. The accepted graph explorer behaviours require a reader to trace a handoff, inspect evidence, and return to a loaded neighbourhood while preserving the source-qualified identity of each record.

## Decision

Ship one authorized Blazor graph route over the existing traversal and detail APIs. The local Cytoscape module and native neighbour controls share stable node and edge identities. Use the existing workspace contribution API and tab draft store for graph edits, reauthentication, and version review. When detail inspection observes a new snapshot, cancel any in-flight continuation and restart a pending first-page navigation using its original query.

## Consequences

The graph can show published and workspace relationships together, retain its viewport through paging, and recover through page-one reload after an observed change. Source links use an optional public repository map and commit-pinned citations. The runtime workspace holds all contribution writes. The Mac smoke uses disposable journal records, and graph writes in the home-lab verification harness require explicit opt-in.

---

AI-Fingerprint: sha256:1be3aef2119d
