---
date: 2026-11-10
slug: amend-c4-architecture-to-state-deployment-evidence-per-source
title: "Amend C4 architecture to state deployment evidence per source"
summary: "Express the Terraform-only rule as a per-source deployment-evidence constraint, matching the implemented selector configuration, and describe the no-graph case without naming a repository."
kind: product
status: accepted
sequence: 2026-11-10T06:14:24.000Z
evidence: "https://github.com/jamiemitchellconsultants/division-ontology/pull/70; merge commit 7143eef1d7c0a7039768999917904f2e6fed432c"
---

## Context

This repository is now a source-free template that each division copies. The C4 architecture still named JointBooking as the pilot source and as an example checkout, so every division copy would inherit references to a repository it does not register. The vision was amended first in #67.

## Decision

Express the Terraform-only rule as a per-source deployment-evidence constraint, matching the implemented selector configuration, and describe the no-graph case without naming a repository.

## Consequences

No plan or code change is needed: the implementation already reads deployment selectors per source. Completed developer plans under `docs/superpowers/plans/` and `docs/detailed-implementations/` still mention JointBooking as build history; `init-division` removes them from division copies.

---

AI-Fingerprint: sha256:70d38003fa70

🤖 Generated with [Claude Code](https://claude.com/claude-code)
