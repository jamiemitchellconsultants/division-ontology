---
date: 2026-11-17
slug: fix-skip-the-source-read-token-when-a-generated-refresh-reads-no-source
title: "fix: skip the source-read token when a generated refresh reads no source"
summary: "Skip the source-read token when a generated refresh has nothing to read: no registered sources and no source-read App configured. The policy still verifies the candidate `dist/` against a fresh build from trusted default-branch code."
kind: product
status: accepted
sequence: 2026-11-17T19:57:22.000Z
evidence: "https://github.com/jamiemitchellconsultants/division-ontology/pull/103; merge commit 258d5cecc2336c623c6a3a96b16faa978ac34106"
---

## Context

The template has no registered sources and no working sync App. Its `dist/`-only PRs failed `merge-policy` at "Create source-read token" with `Integration not found`, before the policy ran, so PR #100 needed an administrator override. The fresh build in that case reads no source repository, so the token served no purpose.

## Decision

Skip the source-read token when a generated refresh has nothing to read: no registered sources and no source-read App configured. The policy still verifies the candidate `dist/` against a fresh build from trusted default-branch code. Rejected: recreating a sync App for a template that ships with no sources; documenting a manual admin-merge procedure; leaving the override in place.

## Consequences

Template `dist/`-only PRs can pass `merge-policy` without an App or an admin override. Divisions are unchanged, since each registers sources or sets the source App. The new path is first exercised by the next template `dist/`-only PR; this PR's own check runs `main`'s code and does not reach the token step. A stale template `dist/` can still go unnoticed until someone opens a refresh PR.

---

🤖 Generated with [Claude Code](https://claude.com/claude-code)

AI-Fingerprint: sha256:792dfe3ac2b9
