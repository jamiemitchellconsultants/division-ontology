---
date: 2026-11-21
slug: feat-cursor-pagination-for-post-api-traverse-124
title: "feat: cursor pagination for POST /api/traverse (#124)"
summary: "Implement exactly as planned, in the plan's task order, each task test-first with its own commit."
kind: product
status: accepted
sequence: 2026-11-21T13:15:02.000Z
evidence: "https://github.com/jamiemitchellconsultants/division-ontology/pull/152; merge commit 2900626ad62a53e13b8245621657d6eb8942b5b0"
---

## Context

The merged plan (PR #134) and amended behaviours and architecture (PRs #135, #136) specified paged traversal completely enough to build without re-deciding: stateless frontier-in-cursor (ADR 0019), snapshot-plus-journal binding (ADR 0020), and bounded work measured by examined rows. This pull request is that build.

## Decision

Implement exactly as planned, in the plan's task order, each task test-first with its own commit. Three rulings were recorded during execution: the Store exposes internals to the test assembly (matching the Api project's pattern) so the codec gets DB-free tests; the examined-step budget is a per-instance seam defaulting to 1000 so the counting fixtures can shrink it without touching other scenarios; and after the whole-branch review, the examined counter became per-call local state rather than instance state on the singleton `GraphQueries`, so concurrent traversals cannot share a budget.

## Consequences

Traversal clients — human or agent — can now page an entire neighbourhood exactly once, in order, with the response itself saying how to continue and when to restart. Existing clients see only the additive cursor field. The coarse any-edit cursor invalidation and the unsigned-cursor stance are live behaviour now, as decided; relaxing either needs a new cursor version and a behaviour change. The user phase (#125) verifies acceptance against the B1–B19 list.

Review fixes (per #154, in place on this branch, all test-first): (1) a frontier that exceeds the cursor cap now raises the already-declared 422 `invalidTraversal` with a restart-with-a-smaller-walk instruction instead of an unhandled 500, pinned by an oversize-frontier unit test; (2) a throwaway AGE probe showed a self-loop yields exactly one row in outgoing, incoming and both, so edge-key resume is safe as designed and the single-row assumption is pinned by `NeighbourReadsReturnASelfLoopOncePerDirection`; (3) the examined budget is now checked only before each fetch and every fetched row is processed, with a counting assertion that no row is examined on two pages; (4) examined counts travel in a per-call test-passed `TraversalCounters` object instead of shared singleton state; (5) `Index`/comment/unused-list cleanups. ADR 0019, `03-architecture.md`, and both plan documents were amended in place through their owning role skills (each marked "(Amended after dev-build review.)"); behaviours B1–B18 are unchanged.

Second-round review fixes (per #155, in place on this branch, all test-first): (1) each neighbour fetch is now also capped at the page's remaining path slots, so a busy 150-leaf walk examines exactly 150 rows across all its pages (previously 151, with row 101 counted on two pages); (2) the tautological examined-total assert is replaced by a budget-10 emitted-rows test that fails if the old per-row budget check returns (a page would emit 1 path for 10 examined rows); (3) per the review decision, the oversize-frontier case is its own behaviour B19 (not folded into B9) with B12 qualified by the cap exception, the `traverseGraph` description now names the overflow case, and an internal test-only reduced-cap seam drives the failure through `TraverseAsync` on the wide fixture; (4) the detailed plan's size-bounds paragraph no longer contradicts itself; (5) the examined budget has a single shared default via a nullable `TraversalCounters.Budget`. Artefacts were amended in place through their owning role skills (ADR 0019, `03-architecture.md`, both plans, `02-behaviours.md` for B19/B12), each marked "(Amended after dev-build review.)".

Third-round review fixes (per #156, in place on this branch, all test-first): (1) per the review decision, the path-slot fetch cap now applies only where no row can be filtered — the published scope, and the runtime scope when `includeUnavailable` is true; the filtered runtime scope fetches full batches again, so a filtered tail late in a page costs 3 neighbour reads instead of 252 (pinned by `FilteredTailLateInAPageIsReadInFullBatches` with a new test-only `NeighbourQueries` counter), at the accepted price that a page filling mid-batch there may re-read up to 100 already-counted rows on the next page; (2) B19 is traced to its components in `03-architecture.md`, both plans say B1–B19 with B19's proving tests, and B19 now follows B18; (3) the misleading `TraversalCounters` comment is gone; (4) the detailed plan states the D1 size rule once. ADR 0019, `03-architecture.md`, `02-behaviours.md` (reorder only) and both plans were amended in place through their owning role skills, each marked as amended after dev-build review.

---

AI-Fingerprint: sha256:04dd06bcd69d
