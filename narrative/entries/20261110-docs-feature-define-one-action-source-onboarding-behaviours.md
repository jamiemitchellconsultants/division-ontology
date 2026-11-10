---
date: 2026-11-10
slug: docs-feature-define-one-action-source-onboarding-behaviours
title: "docs(feature): define one-action source onboarding behaviours"
summary: "A SourceAccessGrant to a division's own read-only App registers a repository that has a committed SourceImportConfiguration; `sources.json` stays for steward-registered sources and governs when a repository is both."
kind: product
status: accepted
sequence: 2026-11-10T08:09:46.000Z
evidence: "https://github.com/jamiemitchellconsultants/division-ontology/pull/79; merge commit b060cc1dcf7a43c66f604888b27af0f819ce4e84"
---

## Context

The vision requires one owner action to join a division catalog, no steward admission approval, prompt visibility, unattended updates and source-specific import status. Today a source joins only through a steward-approved `sources.json` change, and any catalog refresh that changes source registration needs steward review. The repository is also now a template that must ship with no sources.

## Decision

A SourceAccessGrant to a division's own read-only App registers a repository that has a committed SourceImportConfiguration; `sources.json` stays for steward-registered sources and governs when a repository is both. Refreshes that only add, update or change the status of granted sources merge automatically; links and core terms keep review. Import status lives in the generated catalog. Removed access and invalid later versions keep the last published version, marked stale. All-repositories installations admit only configured repositories. The default branch head is imported, and namespace conflicts reject the new claimant. Acceptance runs in a division created with `init-division`. SourceAccessGrant, SourceImportConfiguration and SourceImportStatus are added to the core ontology.

## Consequences

The architect must design discovery of grants and removals, the SourceImportConfiguration contract with InternalOntology, a refresh classification that treats granted-source changes as automatic, and a catalog status table without time-based churn. Organisation policy for per-division Apps must be confirmed during acceptance planning. Renamed, transferred and archived repositories are out of scope. The generated catalog change for the new core terms follows in the scheduled-sync PR.

AI-Fingerprint: sha256:f853655fff7a

🤖 Generated with [Claude Code](https://claude.com/claude-code)
