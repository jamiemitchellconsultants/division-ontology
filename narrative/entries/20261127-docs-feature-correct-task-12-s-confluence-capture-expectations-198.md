---
date: 2026-11-27
slug: docs-feature-correct-task-12-s-confluence-capture-expectations-198
title: "docs(feature): correct Task 12's confluence capture expectations (#198)"
summary: "The plan now guards `likelyImpacts` on a found path, which leaves it empty until B3 lands."
kind: product
status: accepted
sequence: 2026-11-27T16:17:55.000Z
evidence: "https://github.com/jamiemitchellconsultants/division-ontology/pull/225; merge commit 65c6efa1f99636d5fa407eb1c08926619f407c3d"
---

## Context

Task 12 said that the confluence capture records "steps 2 and 4 refused". In fact the engine returns exit 0 with an `unsupported-start` gap and no paths, as I1's pilot step 5 does. As a result, the scenario's `traced.paths.find(...)` returns `undefined`, and the unguarded `path.id` crashes before step 7. The executor worked around the crash, and also added `architect` to the render input. With an architect set, step 7 passes before B30 lands, so the baseline would never show B30 changing it.

## Decision

The plan now guards `likelyImpacts` on a found path, which leaves it empty until B3 lands. It also spells out what today's capture must record: steps 2–4 with exit 0 and `unsupported-start`, the view `unsupported-focus`, and step 7 with exit 1 because there is no `architect`. It says explicitly not to add `architect`. No behaviour, task or commit message changes.

## Consequences

A re-run of Task 12 no longer crashes. The confluence baseline shows B3 and B30 arriving as separate diffs in later tasks.

🤖 Generated with [Claude Code](https://claude.com/claude-code)

AI-Fingerprint: sha256:a70963db5796
