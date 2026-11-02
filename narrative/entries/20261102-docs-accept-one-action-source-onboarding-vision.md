---
date: 2026-11-02
slug: docs-accept-one-action-source-onboarding-vision
title: "docs: accept one-action source onboarding vision"
summary: "The source owner's enrollment action will be granting a preapproved, selected-repository, read-only GitHub App access to their repository."
kind: product
status: accepted
sequence: 2026-11-02T17:32:25.000Z
evidence: "https://github.com/jamiemitchellconsultants/division-ontology/pull/21; merge commit 90b40d0234efc9cdf95c1e09fe8865f1eee1a168"
---

## Context

An approved source repository owner currently needs several coordinated steps to join the division catalog despite having administration access to their own repository. The owner has no write access to division-ontology, and the product goal is to remove a separate registration action.

## Decision

The source owner's enrollment action will be granting a preapproved, selected-repository, read-only GitHub App access to their repository. A valid source should appear after normal sync, with all committed concepts imported and clear status for attempts, publication, failures, and revoked access. Cross-source equivalence and shared-term governance remain separate.

## Consequences

The analyst must define discovery, validation, publication, failure isolation, status, and revocation behavior consistent with one owner action. The architect must design a genuinely read-only source access path and preserve catalog provenance. This vision does not choose the implementation mechanism.

AI-Fingerprint: sha256:9e2364fd922b
