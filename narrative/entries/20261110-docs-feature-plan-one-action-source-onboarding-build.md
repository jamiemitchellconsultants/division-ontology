---
date: 2026-11-10
slug: docs-feature-plan-one-action-source-onboarding-build
title: "docs(feature): plan one-action source onboarding build"
summary: "Thirteen test-first tasks, each ending in a focused commit: reasons, import configuration, a fixture division with a fake grant provider, the granted-source importer, grant discovery, status classification, the catalog builder and…"
kind: product
status: accepted
sequence: 2026-11-10T12:47:19.000Z
evidence: "https://github.com/jamiemitchellconsultants/division-ontology/pull/87; merge commit 5cc28fb7a56d707f33c240ff6e02413273faad64"
---

## Context

The accepted architecture (ADRs 0009–0012) leaves the build open: how the importer, classifier and builder fit the existing `src/` code, and five points the architect passed on. Those were the installation's `repository_selection`, the Contents API volume, the consumers of `catalog.sources`, the fixture division, and how a granted source behaves when later registered. Today `buildCatalog` throws on the first concept collision and `classifyRefresh` treats any source change as steward-reviewed, so both must change without altering registered-source behaviour.

## Decision

Thirteen test-first tasks, each ending in a focused commit: reasons, import configuration, a fixture division with a fake grant provider, the granted-source importer, grant discovery, status classification, the catalog builder and renderer, CLI orchestration with the B1–B17 behaviour tests, refresh classification, the verifier, the C4 reader, the workflows and the documentation. `repository_selection` is read from the first page of `GET /installation/repositories`, so no fallback is needed (confirmed in GitHub's published API description). Namespaces overlap when either is a prefix of the other, so two sources cannot collide on a concept IRI. Only `src/c4/snapshot.mjs` compares `catalog.sources` with `sources.json`, and the workspace bundle reads through it. A granted source later registered is rebuilt strictly and its carried graph dropped. Registered sources keep today's messages and strictness. The build PR does not stage `dist/`; the scheduled sync regenerates it.

## Consequences

The build gains new modules, `origin`, `stale` and `importStatus` fields in every catalog, and two workflow changes. Until the scheduled sync regenerates `dist/`, `npm run check` reports it stale for those fields only. Granted sources are limited to GitHub.com hosts through a derived clone URL. Each sync costs one Contents API request per installed repository, which `docs/automation-setup.md` will state. Real-organisation acceptance (B1, B4, B6, B14 and the policy assumption U12) belongs to the user phase. Writing `.division-ontology.json` from InternalOntology's installer remains a separate change in that repository.

AI-Fingerprint: sha256:f7f33d79564c

🤖 Generated with [Claude Code](https://claude.com/claude-code)
