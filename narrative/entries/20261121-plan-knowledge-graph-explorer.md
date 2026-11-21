---
date: 2026-11-21
slug: plan-knowledge-graph-explorer
title: "Plan: Knowledge Graph Explorer"
summary: "Build in six test-first tasks: neighbourhood projection, navigation and paging, the diagram, inspection, contribution editing, and browser verification. Pin the upstream Cytoscape 3.35.0 module and license by checksum."
kind: product
status: accepted
sequence: 2026-11-21T20:10:13.000Z
evidence: "https://github.com/jamiemitchellconsultants/division-ontology/pull/167; merge commit 871bf55980ad87d85bfb2ac9e256a875d6fb5f79"
---

## Context

The accepted architecture combines one-hop traversal, a local diagram, a keyboard-accessible list, and existing contribution flows. The build needs to preserve exact record identity and view state while making paging errors, evidence, and draft recovery observable in tests. CloudFront's existing SPA rewrite uses an explicit list of workspace route prefixes and needs to include the graph route.

## Decision

Build in six test-first tasks: neighbourhood projection, navigation and paging, the diagram, inspection, contribution editing, and browser verification. Pin the upstream Cytoscape 3.35.0 module and license by checksum. Reuse published traversal and the existing workspace APIs. Every edit stays in the runtime workspace, with Turtle files in the adopting and source repositories as read-only inputs.

- [Task plan](https://github.com/jamiemitchellconsultants/division-ontology/blob/95a2ea5823a15ee8f87987fdbc4e4642e3f9af05/docs/superpowers/plans/knowledge-graph-explorer/plan.md)
- [Detailed implementation plan](https://github.com/jamiemitchellconsultants/division-ontology/blob/95a2ea5823a15ee8f87987fdbc4e4642e3f9af05/docs/detailed-implementations/knowledge-graph-explorer/plan.md)

## Consequences

The build can be reviewed one task at a time. The final browser scenario exercises a realistic handoff, additional pages, stale reload, and a separate reader's view of manager contributions. The local fixture check compares every tracked Turtle file and repository revision before and after edits. Deployment checks include direct navigation and same-origin module delivery.

---

AI-Fingerprint: sha256:d5e8da1a810d
