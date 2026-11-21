---
date: 2026-11-21
slug: docs-align-traversal-cursor-errors-with-the-422-precedent-121
title: "docs: align traversal cursor errors with the 422 precedent (#121)"
summary: "Amend the behaviours to the existing contract: 422 `invalidCursor` for malformed or parameter-mismatched cursors, 409 `staleCursor` for a moved-on snapshot or workspace view, both instructing restart from page one."
kind: product
status: accepted
sequence: 2026-11-21T10:25:29.000Z
evidence: "https://github.com/jamiemitchellconsultants/division-ontology/pull/130; merge commit 0db3dc7cda96400dccb3e9aad3e8070cda4c265e"
---

## Context

The architect, checking how traversal would declare its cursor errors, found that B8/B9 specified 400 `invalidCursor` while the repository's single code-to-status map assigns that code 422 for history and inventory, with 400 reserved for unreadable request bodies. The phase decision had been taken on a misstated precedent, so the architect raised pushback rather than reinterpreting the behaviours.

## Decision

Amend the behaviours to the existing contract: 422 `invalidCursor` for malformed or parameter-mismatched cursors, 409 `staleCursor` for a moved-on snapshot or workspace view, both instructing restart from page one. Rejected: giving traversal the same code at a different status (inconsistent client contract), and changing the shared status map to 400 (would alter the existing history and inventory endpoints, outside this feature's scope).

## Consequences

The architecture phase can now trace B8/B9/B14 exactly as written, reusing `ProblemResponses.StatusFor` with no changes to existing endpoints. Once this amendment merges, the pushback is cleared (`feature.mjs clear-pushback`) and the architect resumes; the cursor-carried frontier decision the user already took (stateless cursor, no server-retained walk state) is unaffected and is the first decision the resumed architecture records.

---

AI-Fingerprint: sha256:b3b13c008897
