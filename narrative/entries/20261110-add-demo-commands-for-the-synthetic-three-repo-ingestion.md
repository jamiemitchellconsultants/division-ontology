---
date: 2026-11-10
slug: add-demo-commands-for-the-synthetic-three-repo-ingestion
title: "Add demo commands for the synthetic three-repo ingestion"
summary: "Make the existing fixture available through `npm run demo:*` scripts and add a demo script that keeps the local workspace running until `demo:down` is run. All demo data lives outside the checkout so it is fully removable."
kind: product
status: accepted
sequence: 2026-11-10T06:34:22.000Z
evidence: "https://github.com/jamiemitchellconsultants/division-ontology/pull/75; merge commit 820acc9ac87960a17352d718178230cdacc319d5"
---

## Context

A team that creates a division repository from this template starts with no registered sources and empty C4 registries, so it has nothing to look at until a real repository has been onboarded. A synthetic three-system fixture and a workspace smoke test already existed, but the fixture was documented only under `test/fixtures/`, and the smoke test tears the workspace down as soon as its checks finish.

## Decision

Make the existing fixture available through `npm run demo:*` scripts and add a demo script that keeps the local workspace running until `demo:down` is run. All demo data lives outside the checkout so it is fully removable. Registering the synthetic repositories in this repository's real `sources.json` was rejected because it would pollute the template and its generated catalog.

## Consequences

New teams can explore the catalog, C4 paths and the workspace UI with one command and remove everything afterwards. Because the demo builds its own division repository, it does not demonstrate the reviewed registration, scheduled sync and catalog PR flow; the README points to Contribute for that. The workspace demo requires Docker and jq.

---

AI-Fingerprint: sha256:ac5c525b1bd7

🤖 Generated with [Claude Code](https://claude.com/claude-code)
