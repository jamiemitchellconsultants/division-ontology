---
date: 2026-11-21
slug: docs-behaviours-for-traversal-cursor-pagination-121
title: "docs: behaviours for traversal cursor pagination (#121)"
summary: "Fix the behaviours before the mechanism."
kind: product
status: accepted
sequence: 2026-11-21T10:17:35.000Z
evidence: "https://github.com/jamiemitchellconsultants/division-ontology/pull/128; merge commit fcf03c4a9aab2ce93341e20220a83aba5a17ec7f"
---

## Context

The accepted vision (PR #126) left the observable behaviour of paged traversal unspecified: how continuation behaves at a multi-hop branch boundary, what a cursor binds, how each cursor failure presents, and how the bounded-work outcome would ever be measured. Without those behaviours, the architecture phase would have had to invent product semantics while designing the mechanism.

## Decision

Fix the behaviours before the mechanism. Continuation preserves today's breadth-first path order with a fixed 100-path page; `truncated` means "another page exists" on every page; each page carries its own endpoints' entity context; the runtime `next` link names `POST /api/traverse` while the cursor travels in the response body's `nextCursor`. Bounded work is specified as countable fixture measures (relationships examined per page, independent of page number and of filtered-out unavailable relationships), not as an implementation prescription. The frontier-resumption mechanism is explicitly deferred to the architect (U6), constrained by B2, B12, B16 and B17.

## Consequences

The architect inherits precise constraints: a cursor bound to parameters, snapshot and journal position; two failure codes with restart semantics; an exact-sequence multi-hop fixture; and per-page work bounds that a replay-earlier-pages design would fail. Deferred items are visible rather than implicit, so a design that cannot meet them must raise pushback against these behaviours instead of quietly diverging. New shared terms `TraversalCursor` and `TraversalPage` enter the division core with evidence traced to the vision and these behaviours.

---

AI-Fingerprint: sha256:fed47712b49e
