---
date: 2026-11-21
slug: docs-vision-for-traversal-cursor-pagination-120
title: "docs: vision for traversal cursor pagination (#120)"
summary: "Adopt cursor pagination for traversal as a product capability: an opaque `nextCursor`, a HATEOAS `next` link whenever another page exists, pages that carry their own entity context, and cursors bound to the original traversal parameters…"
kind: product
status: accepted
sequence: 2026-11-21T09:25:31.000Z
evidence: "https://github.com/jamiemitchellconsultants/division-ontology/pull/126; merge commit 75e9b330f5a03e341ff158973e1aeefb4c2838d6"
---

## Context

`POST /api/traverse` stops at 100 paths and reports `truncated` with no way to continue, so well-connected entities — and workspace-authored relationships beyond that boundary — are unreachable through the API. The product owner accepted a vision on 21 November 2026 to make the 100-path limit a page boundary with trustworthy continuation, without disturbing existing first-page clients.

## Decision

Adopt cursor pagination for traversal as a product capability: an opaque `nextCursor`, a HATEOAS `next` link whenever another page exists, pages that carry their own entity context, and cursors bound to the original traversal parameters and a consistent snapshot/workspace view, with a clear restart-from-page-one error when that view can no longer be continued. Alternatives considered and set aside at vision level: raising or removing the 100-path limit (unbounded responses for busy nodes), and leaving truncation as-is (busy nodes stay partially invisible). Graph UI work and any change to traversal semantics stay out of scope.

## Consequences

The analyst phase inherits five measurable outcomes — every path reachable exactly once across pages, first-page compatibility, self-contained pages, trustworthy continuation with defined cursor failures, and spec-documented paging — and must turn them into behaviours, including the multi-hop frontier and cursor-invalidation cases. The architecture phase will need to decide how continuation resumes a bounded breadth-first walk without unbounded server state. Nothing about how users edit workspace data changes, and Turtle files remain read-only inputs.

---

AI-Fingerprint: sha256:65291fc94c11
