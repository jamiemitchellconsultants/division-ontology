---
date: 2026-11-10
slug: fix-let-a-division-copy-use-any-repository-name
title: "fix: let a division copy use any repository name"
summary: "Remove `owner` and `repositories` from the writer-token step in both workflows."
kind: product
status: accepted
sequence: 2026-11-10T17:04:16.000Z
evidence: "https://github.com/jamiemitchellconsultants/division-ontology/pull/92; merge commit 011072385fc5bc43a361a208e7a18609a52ab741"
---

## Context

Deployed acceptance of one-action source onboarding ran in a throwaway division named `onboarding-acceptance-division`. The sync run (https://github.com/jamiemitchellconsultants/onboarding-acceptance-division/actions/runs/42859953184) failed at "Create division-writer token" with `404 Not Found` because `.github/workflows/sync-catalog.yml` and `.github/workflows/review-recheck.yml` asked the token action for a repository literally named `division-ontology`. This has been so since 2026-09-26 (37e76ec), but the feature's outcome is that every division created from the template can onboard sources. The source-read token, the App setup check and the installation all worked in the same run.

## Decision

Remove `owner` and `repositories` from the writer-token step in both workflows. The token action then scopes the token to only the current repository (its documented behaviour when both are empty), which works for any repository name and is as narrow as before. The source-read token, which deliberately covers the whole installation, is unchanged. `test/workflow.test.mjs` now asserts the writer tokens carry no owner or repositories value, and that no workflow names a literal repository when minting a token.

## Consequences

Divisions may use any repository name. A division copy that already exists picks the fix up by taking these two workflow files. The acceptance run can proceed: it is repeated in the throwaway division once this merges. No behaviour, architecture or catalog output changes. `workspace-images.yml` still names its container image after the template, which is harmless and left alone.

AI-Fingerprint: sha256:29388d63dcba

🤖 Generated with [Claude Code](https://claude.com/claude-code)
