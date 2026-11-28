---
date: 2026-11-28
slug: docs-feature-bound-traces-and-repair-models-from-the-publisher-201
title: "docs(feature): bound traces and repair models from the publisher (#201)"
summary: "- **Publisher repair pass (ADR 0036, amended).** On AWS the publisher's schedule stays enabled. Each run, after any import, rebuilds the missing model of every activated snapshot, in its own short transaction outside the journal lock."
kind: product
status: accepted
sequence: 2026-11-28T10:21:54.000Z
evidence: "https://github.com/jamiemitchellconsultants/division-ontology/pull/237; merge commit aa27bd4508dc6444957b6ae6248bd2e1cbb625a0"
---

## Context

**Repairing missing models.** ADR 0036 relied on the scheduled import running again for a manifest it had already activated. On AWS the publisher's schedule is disabled once the pending manifest is cleared. So a model that an older worker left missing for an activated snapshot is never rebuilt. Replay builds models for every activated snapshot, so the checksums then disagree. The indexer runs everywhere, but on AWS it deliberately has no bundle access.

**Bounding traces.** B40 (PR #235) bounds every trace at 500 paths. The re-review also found that the process-wide trace cache retained full trace results.

## Decision

- **Publisher repair pass (ADR 0036, amended).** On AWS the publisher's schedule stays enabled. Each run, after any import, rebuilds the missing model of every activated snapshot, in its own short transaction outside the journal lock. An idle run does one query, about $0.25 a month. On Mac and home lab, the installer re-activates on each deploy, and the operator's `publisher` command runs the same pass.
  - The user chose this over a one-off operator command in the rollout.
  - Giving the indexer bucket access was ruled out.
- **The bound in the engine (B40).** The walk stops before emitting the 501st path, and reports `paths-truncated`. The command line, the paths route and citation validation share the bound.
- **Trace reuse.** Traces are reused within a request only. No cache is kept between requests.

## Consequences

- Activated snapshots always regain their model, so no upgrade step is needed.
- The publisher Lambda runs every minute on AWS.
- A depth 6 trace is about 2 MB on the Q1 fixture. The developer measures the bounded worst-case preview against the 30 s gateway limit and the 1 GiB Lambda.

---

🤖 Generated with [Claude Code](https://claude.com/claude-code)

AI-Fingerprint: sha256:bd998fb868f9
