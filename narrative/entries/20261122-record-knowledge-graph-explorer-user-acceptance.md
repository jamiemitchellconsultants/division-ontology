---
date: 2026-11-22
slug: record-knowledge-graph-explorer-user-acceptance
title: "Record Knowledge Graph Explorer user acceptance"
summary: "Use one Playwright regression suite for B1–B15 with a PowerShell entry point on the home-lab operator machine."
kind: product
status: accepted
sequence: 2026-11-22T21:32:15.000Z
evidence: "https://github.com/jamiemitchellconsultants/division-ontology/pull/173; merge commit e34706e6764c7f228a700920d966270d9ec65d10"
---

## Context

The User-phase walkthroughs exercised the actual home-lab deployment, including interactions across two roles and failures that only a browser-driven scenario can reproduce reliably. Repeating the walkthrough fragments by hand required the operator to carry fixture IDs and paging state between steps.

## Decision

Use one Playwright regression suite for B1–B15 with a PowerShell entry point on the home-lab operator machine. Require the expected release and synthetic snapshot, and require explicit permission to create temporary records for write scenarios. Retain the expected and actual result for each step and a ledger of the run's record IDs and revisions.

## Consequences

An operator can rerun the full suite or select a behaviour while reusing the current terminal's fixture credentials. The runner checks the original graph and the retained checkout inputs after exact-ID cleanup. The write suite needs the synthetic home-lab fixture and two controlled accounts. Contribution history remains available after removal.

---

AI-Fingerprint: sha256:ed03c65c2e9a
