---
date: 2026-11-24
slug: ci-refresh-open-narrative-proposals-when-main-moves
title: "ci: refresh open narrative proposals when main moves"
summary: "Install the Narrative refresh workflow as the tool documents it, pinned to the same Narrative commit as the existing narrative workflows."
kind: product
status: accepted
sequence: 2026-11-24T13:17:04.000Z
evidence: "https://github.com/jamiemitchellconsultants/division-ontology/pull/184; merge commit 86043b374b2ca1535e2aeb1ebdc37b6647a3cc94"
---

## Context

Each narrative proposal recompiles the whole of `Narrative.md`, so merging one makes every other open proposal conflict. That happened to #179 and #181 after #183 merged, and they had to be fixed by hand. The Narrative tool already has a refresh mode for this, but the repository never installed it.

## Decision

Install the Narrative refresh workflow as the tool documents it, pinned to the same Narrative commit as the existing narrative workflows. A push-only trigger was rejected because it misses a proposal that maintenance creates after the push refresh has finished. The completion trigger and manual dispatch cover that race.

## Consequences

Open proposals stay mergeable without hand-resolving `Narrative.md`. A refresh push uses `GITHUB_TOKEN`, which starts no workflows. The required `test`, `ai-fingerprint` and `merge-policy` checks on a refreshed proposal still have to be started by hand, as they already are on new proposals. Conflicts in anything other than the compiled output are still left for a person to resolve.

---

AI-Fingerprint: sha256:cab899acead0

🤖 Generated with [Claude Code](https://claude.com/claude-code)
