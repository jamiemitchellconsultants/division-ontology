---
date: 2026-09-26
slug: automate-division-ontology-catalog-sync-with-trusted-merge-policy
title: "Automate division ontology catalog sync with trusted merge policy"
summary: "Run a 30-minute and manual sync in division-ontology, using scoped GitHub App installation tokens to read sources and update a fixed catalog PR. Verify generated output from trusted default-branch code before an automatic merge."
kind: product
status: accepted
sequence: 2026-09-26T20:58:33.000Z
evidence: "https://github.com/jamiemitchellconsultants/division-ontology/pull/2; merge commit 2bcb27f007865ae8f7038cdb9e3f3a499f0edd08"
---

## Context

The division catalog currently lives in one repository and needs to collect all concepts from approved related repositories. Routine source changes should arrive without repeated human instructions, while division stewards retain authority over shared meaning, links and source registration.

## Decision

Run a 30-minute and manual sync in division-ontology, using scoped GitHub App installation tokens to read sources and update a fixed catalog PR. Verify generated output from trusted default-branch code before an automatic merge. Require a current division steward approval for authored changes and linked-concept drift. Poll review decisions from the default branch and retrigger the trusted gate. Keep source onboarding and generated output in separate PRs.

## Consequences

After the one-time App and branch-rule setup, unchanged or unlinked source updates require no human action. A new private source still needs steward approval and addition to the App installation. Linked-concept changes wait for a current steward decision. Scheduled runs can fail visibly when a source is inaccessible or malformed, leaving the published catalog intact. The App has latent write permission on all installed repositories; the setup guide documents a two-App alternative.
