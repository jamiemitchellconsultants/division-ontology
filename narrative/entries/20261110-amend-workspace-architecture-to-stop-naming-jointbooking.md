---
date: 2026-11-10
slug: amend-workspace-architecture-to-stop-naming-jointbooking
title: "Amend workspace architecture to stop naming JointBooking"
summary: "Describe the engineering inputs as the division catalog plus the sources a division registers, with the synthetic fixtures for demonstration."
kind: product
status: accepted
sequence: 2026-11-10T06:11:35.000Z
evidence: "https://github.com/jamiemitchellconsultants/division-ontology/pull/68; merge commit 7195e2ffdfa638a7e2289003649dfa2e0cc398db"
---

## Context

This repository is now a source-free template that each division copies. The workspace architecture still described JointBooking as the registered source and linked to JointBooking-specific designs, so every division copy would inherit a false statement and links into a repository it does not use.

## Decision

Describe the engineering inputs as the division catalog plus the sources a division registers, with the synthetic fixtures for demonstration. Remove the two precedent links that name JointBooking rather than rewording them, because the remaining precedent and ADRs carry the decisions.

## Consequences

The runbook, user guide and acceptance record do not depend on JointBooking being registered. One developer-plan checklist item (`docs/detailed-implementations/runtime-knowledge-workspace/plan.md`) still cites LocalAI's JointBooking installation boundary; it is completed build history, and `init-division` removes that folder from division copies. The removed links remain in git history for anyone tracing the original precedent.

---

AI-Fingerprint: sha256:f76fdbfcd1b6

🤖 Generated with [Claude Code](https://claude.com/claude-code)
