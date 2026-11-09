---
date: 2026-11-09
slug: prepare-supervised-workspace-acceptance
title: "Prepare supervised workspace acceptance"
summary: "Use Order Fulfilment as the synthetic pilot and run acceptance against the existing workspace. Automate preparation, publication, browser checks, restart and guarded replay. Require search to work with the configured embedding service."
kind: product
status: accepted
sequence: 2026-11-09T19:32:04.000Z
evidence: "https://github.com/jamiemitchellconsultants/division-ontology/pull/64; merge commit 2f4fa3cca679c81f5a77d5a2783d456a31d0fa62"
---

## Context

The workspace needed a repeatable acceptance procedure with evidence for review. The home lab reuses one deployment across use cases.

## Decision

Use Order Fulfilment as the synthetic pilot and run acceptance against the existing workspace. Automate preparation, publication, browser checks, restart and guarded replay. Require search to work with the configured embedding service. Accept the recorded local and home-lab evidence and defer the remaining steps to #65.

## Consequences

Repeat runs can reuse the fixture and deployment while retaining separate evidence. The operator can reset workspace data between use cases. Merging this PR closes the acceptance phase. Issue #65 tracks the deferred evidence separately.

AI-Fingerprint: sha256:e143ed4f2f82
