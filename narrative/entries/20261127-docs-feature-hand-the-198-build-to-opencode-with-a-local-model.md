---
date: 2026-11-27
slug: docs-feature-hand-the-198-build-to-opencode-with-a-local-model
title: "docs(feature): hand the #198 build to opencode with a local model"
summary: "The build is run by opencode with the local model Qwen 3.6 27B (Q6), from [build-handover-opencode.md](docs/detailed-implementations/generalise-impact-assessment/build-handover-opencode.md)."
kind: product
status: accepted
sequence: 2026-11-27T11:30:42.000Z
evidence: "https://github.com/jamiemitchellconsultants/division-ontology/pull/221; merge commit 3e3f6e6154d6600b1b73a5c3585fd46ed5ed008b"
---

## Context

The #198 plan (PR #218) left open who would execute the build: Claude through feature-dev, or another agent working from the detailed plans.

## Decision

The build is run by opencode with the local model Qwen 3.6 27B (Q6), from [build-handover-opencode.md](docs/detailed-implementations/generalise-impact-assessment/build-handover-opencode.md). The prompt keeps it to the plan: one task per commit, test first, one increment per session, a fixed BLOCKED report instead of improvising, and no merging. The developer role only verifies the result (feature-dev build step 3).

## Consequences

- Build sessions start from this prompt. Its hard rules bind the executor alongside AGENTS.md.
- The executor opens a draft build PR on its first push so CI runs, posts each increment handoff on #198, and ticks #203 at the end.
- Blocked tasks come back to a person to decide, rather than becoming pushback raised by the executor.

🤖 Generated with [Claude Code](https://claude.com/claude-code)

AI-Fingerprint: sha256:478639c32762
