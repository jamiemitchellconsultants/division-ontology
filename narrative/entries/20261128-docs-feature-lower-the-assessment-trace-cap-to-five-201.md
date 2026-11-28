---
date: 2026-11-28
slug: docs-feature-lower-the-assessment-trace-cap-to-five-201
title: "docs(feature): lower the assessment trace cap to five (#201)"
summary: "- A record lists at most 5 traces, each with at most 20 starts. That puts the worst case at about 10 s. The owner chose this over keeping 20 traces and accepting that worst-case previews fail on AWS."
kind: product
status: accepted
sequence: 2026-11-28T09:07:51.000Z
evidence: "https://github.com/jamiemitchellconsultants/division-ontology/pull/231; merge commit fd95392e3a8e093f910f7d3264de2463ba67dbf6"
---

## Context

ADR 0035 lets a record list the traces its cited paths came from, and validation re-traces each one. Task 42 measured the worst case on the Q1 fixture. A first preview of 20 traces of 20 starts took 37.8 s. That was after each trace had been limited to one run per request. One trace of 20 starts takes about 1.9 s. The time is inside the ADR's budget of 100 s, but above the 30 s that API Gateway allows on AWS.

## Decision

- A record lists at most 5 traces, each with at most 20 starts. That puts the worst case at about 10 s. The owner chose this over keeping 20 traces and accepting that worst-case previews fail on AWS.
- A draft at 5 traces does not take paths from a sixth. The page says so, and the assessor starts another assessment for the rest. This is the architect's call.

## Consequences

- The engine refuses more than 5 traces as `invalidAssessment`.
- The draft page stops at 5 traces.
- The Q1 performance test measures previews at 5 traces of 20 starts.

---

🤖 Generated with [Claude Code](https://claude.com/claude-code)

AI-Fingerprint: sha256:c89bdd1248e9
