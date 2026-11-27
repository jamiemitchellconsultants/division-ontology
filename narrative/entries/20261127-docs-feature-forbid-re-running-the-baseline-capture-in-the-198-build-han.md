---
date: 2026-11-27
slug: docs-feature-forbid-re-running-the-baseline-capture-in-the-198-build-han
title: "docs(feature): forbid re-running the baseline capture in the #198 build handover"
summary: "The hard rules now forbid running `capture.mjs` after Task 1."
kind: product
status: accepted
sequence: 2026-11-27T15:06:09.000Z
evidence: "https://github.com/jamiemitchellconsultants/division-ontology/pull/223; merge commit cc4862644aa8eadc06c6835c2637c0f295f17439"
---

## Context

During Task 10 the opencode executor re-ran `capture.mjs`. That re-recorded the B37 baseline (45 files under `test/impact-corpus/`) from the broken front end, which then made the conformance failures impossible to diagnose. The prompt banned editing `expected/` by hand, but never mentioned the script that rewrites it.

## Decision

The hard rules now forbid running `capture.mjs` after Task 1. The only exceptions are the detailed plan's own capture steps: I2's baseline update procedure (Tasks 13, 14, 15 and 17) and the single-scenario captures in Tasks 12, 16 and 21, each with exactly the arguments the step gives. A failing baseline test means fix the code, or stop and report.

## Consequences

The executor can no longer turn a bug into the baseline by re-capturing. A baseline change happens only where the plan schedules it, and is reviewed against B37's list.

🤖 Generated with [Claude Code](https://claude.com/claude-code)

AI-Fingerprint: sha256:e61863ef4fe9
