---
date: 2026-11-10
slug: feat-one-action-source-onboarding
title: "feat: one-action source onboarding"
summary: "Granted sources are built beside the strict registered path."
kind: product
status: accepted
sequence: 2026-11-10T14:29:53.000Z
evidence: "https://github.com/jamiemitchellconsultants/division-ontology/pull/89; merge commit 19f91afcbe6b24936cdc49fcfbdd69b94789d199"
---

## Context

Joining a division catalog needed a steward-approved `sources.json` change and an App with write access across its installation. Every source failure failed the whole build, `dist/` was a pure function of live sources so an earlier valid version could not be kept, and any source change forced steward review. ADRs 0009–0012 and the plan in PR 87 set the structure; this PR implements it.

## Decision

Granted sources are built beside the strict registered path. A grant provider lists the source-read App's installation (polling, no webhook receiver); each configured repository's `.division-ontology.json` and ontology are read in isolation; a classifier turns outcomes and the committed `dist/` into `current`, `invalid`, `not configured` and `access removed` rows and carries the last valid version forward as stale. Content failures become status; GitHub or network failures, and short listings, fail the run and leave `dist/` untouched. Namespaces conflict when either is a prefix of the other, and the existing holder wins. Every source records `origin`, and refreshes touching only granted sources classify as automatic. The verifier rebuilds from the base `dist/` and live grants; the C4 snapshot loader accepts granted sources; both workflows mint the source token from the source-read App, falling back to the writer App when it is unset. Docs, governance and agent instructions describe the single owner action.

## Consequences

Every catalog gains `origin`, `stale` and `importStatus`, so `npm run check` reports `dist/catalog.json` stale for `importStatus` only until the scheduled sync PR merges. Divisions need two Apps to take grants, and a grant can take up to two hours to appear. Granted sources are GitHub.com only. The GitHub client is tested against stubs only; the first live run, and acceptance of B1, B4, B6 and B14 with the organisation-policy assumption U12, belong to the user phase. A local build of a division that holds granted sources needs `DIVISION_SOURCE_APP` and `GH_SOURCE_TOKEN`. Writing `.division-ontology.json` from InternalOntology's installer is a separate change in that repository. An independent review found and this PR fixes: blank-node relabelling in carried-forward sources, published namespaces displaced by another source or a steward registration, ontologies over 1 MiB, granted statements about terms outside the namespace, case-sensitive registered ids, and unescaped reasons in the refresh body. Still open: C4 snapshots need a checkout of every source including stale ones.

AI-Fingerprint: sha256:0899d6e39504

🤖 Generated with [Claude Code](https://claude.com/claude-code)
