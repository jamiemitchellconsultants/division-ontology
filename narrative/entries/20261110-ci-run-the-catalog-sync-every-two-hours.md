---
date: 2026-11-10
slug: ci-run-the-catalog-sync-every-two-hours
title: "ci: run the catalog sync every two hours"
summary: "Division copies run the scheduled sync every two hours. The template keeps the sync disabled."
kind: product
status: accepted
sequence: 2026-11-10T08:19:19.000Z
evidence: "https://github.com/jamiemitchellconsultants/division-ontology/pull/81; merge commit 18299da3bb69f643daef40d2fba857589a37f8b3"
---

## Context

Division repositories created from this template inherit a catalog sync scheduled every 30 minutes. The template itself has no sources or App credentials, so the sync has no work to do here and its workflow is disabled.

## Decision

Division copies run the scheduled sync every two hours. The template keeps the sync disabled.

## Consequences

In a division, a new source version reaches the generated catalog PR up to two hours after commit rather than 30 minutes, which still meets "the next normal sync" in the one-action onboarding vision; manual dispatch remains for an immediate run. In the template, generated `dist/` changes, such as those following new core terms, are made by a separate generated-only PR rather than by the sync.

AI-Fingerprint: sha256:0db922f6f455

🤖 Generated with [Claude Code](https://claude.com/claude-code)
