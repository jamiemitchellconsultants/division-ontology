---
date: 2026-11-28
slug: docs-feature-require-build-commit-messages-to-match-their-diffs-198
title: "docs(feature): require build commit messages to match their diffs (#198)"
summary: "Section 7 of the prompt now requires the executor to find every change it describes in the staged diff before committing, and to check `git show --stat HEAD` afterwards."
kind: product
status: accepted
sequence: 2026-11-28T09:25:14.000Z
evidence: "https://github.com/jamiemitchellconsultants/division-ontology/pull/227; merge commit 950e80d163f830a5df491e34dd457e25a8c7dbde"
---

## Context

The opencode executor pushed two commits whose messages described changes that were not in the diff. In f64e5f0 the message says the scenario's `architect` field and `try/catch` were removed, but the commit only deletes one expected file. As a result, the corpus disagreed with itself, and the PR body's narrative and the task tracking, which both read commit subjects, could not be trusted.

## Decision

Section 7 of the prompt now requires the executor to find every change it describes in the staged diff before committing, and to check `git show --stat HEAD` afterwards. Section 9 adds a stop condition: a pushed commit whose message does not match its diff is reported, not amended. `AI-Fingerprint:` lines are banned from commit messages, because they belong only in the PR body.

## Consequences

The executor's commit messages can be read as a record of what changed. A mismatch now stops the build instead of passing unnoticed.

🤖 Generated with [Claude Code](https://claude.com/claude-code)

AI-Fingerprint: sha256:7fea5a9f8613
