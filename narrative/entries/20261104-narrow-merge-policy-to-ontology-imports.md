---
date: 2026-11-04
slug: narrow-merge-policy-to-ontology-imports
title: "Narrow merge policy to ontology imports"
summary: "Require the check's current steward approval for source registration changes and linked-concept drift. Verified routine refreshes pass automatically; other changes pass the check."
kind: product
status: accepted
sequence: 2026-11-04T08:42:00.000Z
evidence: "https://github.com/jamiemitchellconsultants/division-ontology/pull/37; merge commit b43a529a24193252fc0ee8d230789ba95e411707"
---

## Context

The existing gate is broader than needed for external ontology imports and blocks unrelated changes. The main branch ruleset has no blanket required approval or required code-owner approval.

## Decision

Require the check's current steward approval for source registration changes and linked-concept drift. Verified routine refreshes pass automatically; other changes pass the check.

## Consequences

Changes to trusted policy and workflow files also pass this check without steward approval. CODEOWNERS still requests reviews, and repository governance continues to call for applicable reviews, but the ruleset does not enforce those approvals. Incomplete pull-request file lists fail closed.

AI-Fingerprint: sha256:89e389e0a19b
