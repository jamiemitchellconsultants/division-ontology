---
date: 2026-11-10
slug: docs-feature-define-one-action-source-onboarding-architecture
title: "docs(feature): define one-action source onboarding architecture"
summary: "Each division has a dedicated read-only source App (Contents and Metadata read) that owners add repositories to, separate from the division-writer App."
kind: product
status: accepted
sequence: 2026-11-10T11:22:44.000Z
evidence: "https://github.com/jamiemitchellconsultants/division-ontology/pull/85; merge commit a87341b564a57f738221a3b27499ebac3201328b"
---

## Context

The accepted behaviours need a source owner's one action to join a division catalog without write access or steward approval. The current sync App has write permission across its installation, so granting it would not be read-only. Source registration changes force steward review, one invalid source fails the whole build, and `dist/` is only a function of live sources, so an earlier valid version cannot be kept when a source turns invalid or loses access. The Analyst left open how grants are discovered (U11) and which file configures an import (U10).

## Decision

Each division has a dedicated read-only source App (Contents and Metadata read) that owners add repositories to, separate from the division-writer App. Grants are found by polling the installation during the normal sync; no webhook receiver is built (ADR 0009). A source declares its ontology path and namespace in `.division-ontology.json` on its default branch, rather than overloading InternalOntology's `ontology.config.json`, whose `ontologyPath` names the Markdown view (ADR 0010). Import status is generated into `dist/` with no timestamps, and the committed `dist/` is a build input so the last valid version is carried forward. Content failures become status, while GitHub or network failures fail the run (ADR 0011). Each source records its origin; granted-source failures are isolated while registered sources stay strict; namespace conflicts favour the existing holder; and refreshes touching only granted sources classify as automatic (ADR 0012).

## Consequences

Divisions need two Apps and two sets of credentials, and a new grant can take up to two hours to appear. Granted sources can publish their own definitions in their own namespace without review, limited by the namespace checks and by links staying steward-only. `verifyRefresh` must include the base `dist/`, and readers of `catalog.sources` such as the C4 loader and the workspace importer will see granted and stale sources. The developer must confirm how to read the installation's `repository_selection` and the Contents API volume. Offering to write `.division-ontology.json` in InternalOntology's installer is a separate change in that repository.

AI-Fingerprint: sha256:b2eeb0b6eb7c

🤖 Generated with [Claude Code](https://claude.com/claude-code)
