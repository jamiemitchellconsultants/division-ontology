---
date: 2026-11-10
slug: fix-fetch-the-division-repository-with-the-writer-token-not-the-source-t
title: "fix: fetch the division repository with the writer token, not the source token"
summary: "The finalize step authenticates its git fetch with the division-writer token, which is installed on the division repository. The source-read token is for reading source repositories only: it clones sources and lists the installation."
kind: product
status: accepted
sequence: 2026-11-10T17:22:57.000Z
evidence: "https://github.com/jamiemitchellconsultants/division-ontology/pull/94; merge commit 5895f562a611193b9d4dbb9c8a2bd5f39de1e250"
---

## Context

Rerunning deployed acceptance after the first fix, sync run 42862969971 (https://github.com/jamiemitchellconsultants/onboarding-acceptance-division/actions/runs/42862969971) got through both token steps, the catalog build and the creation of refresh PR #3, with `REFRESH_MODE: automatic`. It then failed in "Finalize PR fingerprint and merge mode": `git fetch origin` on the division repository used the source-read token and got `Repository not found`. ADR 0009 keeps the source-read App off the division repository. Under the earlier single-App setup the same token happened to cover it, so splitting the Apps introduced this regression, and nothing in the local tests exercises a step that only runs in GitHub Actions. The refresh PR was left open with the placeholder fingerprint.

## Decision

The finalize step authenticates its git fetch with the division-writer token, which is installed on the division repository. The source-read token is for reading source repositories only: it clones sources and lists the installation. I traced every step of the three workflows by which token it uses; this was the only mismatch. `test/workflow.test.mjs` now asserts that the finalize step's git credential is the writer token and that no step running git fetch or push uses the source token, while the build step keeps the source token. ADR 0009 gets a dated amendment stating the rule instead of being rewritten.

## Consequences

Private divisions can finalize their refresh PR and request auto-merge. An existing division takes this by copying `.github/workflows/sync-catalog.yml` and `test/workflow.test.mjs`. The acceptance run is repeated in the throwaway division once this merges. The environment variable that git-askpass.sh reads is still called `GH_SOURCE_TOKEN` even where it carries the writer token, which a comment now explains; renaming it would touch the askpass script and its test and is left for later. Nothing else about behaviour, the catalog or the architecture changes.

AI-Fingerprint: sha256:c33c34200909

🤖 Generated with [Claude Code](https://claude.com/claude-code)
