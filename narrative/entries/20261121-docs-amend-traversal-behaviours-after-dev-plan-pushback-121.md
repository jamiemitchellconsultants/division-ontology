---
date: 2026-11-21
slug: docs-amend-traversal-behaviours-after-dev-plan-pushback-121
title: "docs: amend traversal behaviours after dev-plan pushback (#121)"
summary: "Adopt \"not proven finished\" semantics for `truncated` and `nextCursor`; validate continuations (shape, parameters, view) before resolving the start entity; and keep cursors unsigned, treating a well-formed alteration as a client-chosen…"
kind: product
status: accepted
sequence: 2026-11-21T10:56:55.000Z
evidence: "https://github.com/jamiemitchellconsultants/division-ontology/pull/135; merge commit 9172dbd7abf9fc3e2ddf96e0cb5dc06039bdc481"
---

## Context

Planning the build exposed three gaps in the accepted behaviours: the truncation semantics had no answer for a frontier that might yield nothing further, the validation order against start-entity resolution was unspecified, and B9 demanded tamper rejection that an unsigned cursor cannot provide. Each was settled with the user before this amendment was written.

## Decision

Adopt "not proven finished" semantics for `truncated` and `nextCursor`; validate continuations (shape, parameters, view) before resolving the start entity; and keep cursors unsigned, treating a well-formed alteration as a client-chosen continuation within the caller's existing read scope — traversal is read-only and any start is directly requestable, so no privilege is gained. Signing was considered and rejected as machinery disproportionate to a read-only continuation token.

## Consequences

The architect amendment (ADR 0019/0020) and the revised developer plan in PR #134 already encode these decisions, so this amendment brings the behaviour contract back in line with them. Later items tracing to the changes: B14/B16/B17 test specifications in the plan (cursor errors, counting, tamper regression), which the build must now satisfy exactly as amended. Once merged, clear the analyst pushback on #119.

---

AI-Fingerprint: sha256:88d35902faca
