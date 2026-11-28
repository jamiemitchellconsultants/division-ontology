---
date: 2026-11-28
slug: docs-feature-bound-a-trace-at-500-paths-200
title: "docs(feature): bound a trace at 500 paths (#200)"
summary: "- Every trace, on the command line and in the workspace, returns at most its first 500 paths, in today's order. It reports one `paths-truncated` gap when more exist (B40). - Assessments cite only paths in the bounded result."
kind: product
status: accepted
sequence: 2026-11-28T10:09:19.000Z
evidence: "https://github.com/jamiemitchellconsultants/division-ontology/pull/235; merge commit 952bc864b91f3dec45f878363c7aab53acff1e21"
---

## Context

B16 promised every path, at any depth from 1 to 6. On the Q1 fixture, one trace from one start finds:
- 780 paths (2 MB) at depth 4;
- 3,905 paths (12 MB) at depth 5;
- 19,530 paths (74 MB) at depth 6.

The deployed API returns at most 6 MiB. The re-review also ran out of a 1 GiB heap previewing one 20-start trace at depth 6.

## Decision

- Every trace, on the command line and in the workspace, returns at most its first 500 paths, in today's order. It reports one `paths-truncated` gap when more exist (B40).
- Assessments cite only paths in the bounded result.
- Depths 1 to 6 stay.
- The user chose this over lowering the maximum depth to 4, and over paging paths by cursor.
- Serving larger payloads through signed S3 URLs is parked as #234.

## Consequences

- Traces at depth 5 and 6 fit the AWS response limit.
- A truncated trace says so and suggests narrowing it.
- The architecture must place the bound in the engine, so that enumeration stops at the cap, and trace B40.
- The C4 fixtures find far fewer than 500 paths, so B37 is unaffected.

---

🤖 Generated with [Claude Code](https://claude.com/claude-code)

AI-Fingerprint: sha256:f9ba4b1f8529
