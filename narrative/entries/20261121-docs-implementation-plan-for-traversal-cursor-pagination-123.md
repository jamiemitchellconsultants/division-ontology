---
date: 2026-11-21
slug: docs-implementation-plan-for-traversal-cursor-pagination-123
title: "docs: implementation plan for traversal cursor pagination (#123)"
summary: "Six tasks, in dependency order: contract and cursor codec; page-bounded walk; snapshot/journal binding; examined-step budget (1000 per request); API surface, HATEOAS and the regenerated committed spec; then whole-feature verification…"
kind: product
status: accepted
sequence: 2026-11-21T11:23:52.000Z
evidence: "https://github.com/jamiemitchellconsultants/division-ontology/pull/134; merge commit 2e616efe1addeffb079e32bd28e171b0fa79e450"
---

## Context

The accepted architecture (PR #132, ADR 0019/0020) fixes what the system must do and how it is shaped, but a builder — possibly a different agent — still needs the exact payload shapes, the resumption sequence, the fixture graphs, and the test that proves each of the 18 behaviours. This plan exists so the build re-decides nothing.

## Decision

Six tasks, in dependency order: contract and cursor codec; page-bounded walk; snapshot/journal binding; examined-step budget (1000 per request); API surface, HATEOAS and the regenerated committed spec; then whole-feature verification against a B1–B18 checklist. Each task is test-first and ends in one focused commit. Native in-session execution is recommended for the build (the tasks share tight interfaces through the codec and the plan carries the design); subagent-driven execution remains available.

## Consequences

Once this plan is reviewed and merged, the build phase (#124) can start without further design questions; any gap the build finds returns as pushback to the owning phase rather than an in-code invention. The user phase (#125) then verifies acceptance against the same behaviour list the scenario tests already prove.

---

AI-Fingerprint: sha256:d6d9f5d02b4a
