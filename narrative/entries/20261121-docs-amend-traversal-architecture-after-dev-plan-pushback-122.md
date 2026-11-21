---
date: 2026-11-21
slug: docs-amend-traversal-architecture-after-dev-plan-pushback-122
title: "docs: amend traversal architecture after dev-plan pushback (#122)"
summary: "Keep the stateless, cursor-carried frontier (the user's earlier decision stands) and make it survivable: shared-prefix dictionary coding plus compression, an issue-side decodability guarantee proven by a wide multi-page fixture…"
kind: product
status: accepted
sequence: 2026-11-21T11:03:58.000Z
evidence: "https://github.com/jamiemitchellconsultants/division-ontology/pull/136; merge commit 436360c52fcec66b8d6d1a28c0e59a85f0bcb19d"
---

## Context

Writing the developer plan stress-tested the accepted architecture and found four structural gaps: the cursor size argument did not survive a wide depth-2 walk, resumption after filtered rows was implicit, the examined budget could be overshot at fetch time by full 101-row batches, and the unsigned-cursor decision contradicted the behaviours' tamper wording. The findings were settled with the user and recorded as pushback rather than worked around in the plan.

## Decision

Keep the stateless, cursor-carried frontier (the user's earlier decision stands) and make it survivable: shared-prefix dictionary coding plus compression, an issue-side decodability guarantee proven by a wide multi-page fixture, fetch-time budget enforcement, and resume past processed rows. Keep cursors unsigned: alteration cannot exceed the holder's existing read access on a read-only endpoint, and that residual is pinned by a regression test in the amended behaviours.

## Consequences

The revised developer plan (PR #134) already encodes every one of these decisions, so the plan, the ADRs, and (once the analyst amendment merges) the behaviours describe the same system. The build must now prove the guarantees the amendments state — every issued cursor within cap across a 61-page walk, no page over its fetch allowance, no row re-examined — or raise fresh pushback. Once merged, clear the architect pushback on #119.

---

AI-Fingerprint: sha256:9436208e84b0
