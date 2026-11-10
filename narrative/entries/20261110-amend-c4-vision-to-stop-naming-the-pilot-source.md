---
date: 2026-11-10
slug: amend-c4-vision-to-stop-naming-the-pilot-source
title: "Amend C4 vision to stop naming the pilot source"
summary: "State the deployment-evidence rule as a per-source constraint and describe the Graphify research without naming the repository. The outcomes, scope and research findings are otherwise unchanged."
kind: product
status: accepted
sequence: 2026-11-10T06:05:43.000Z
evidence: "https://github.com/jamiemitchellconsultants/division-ontology/pull/67; merge commit d60bc82208502acc87f4196a359c9fa60c219309"
---

## Context

This repository is now a source-free template that each division copies. Shipped feature documents still named JointBooking as the pilot source, so every division copy would inherit a reference to a repository it does not register.

## Decision

State the deployment-evidence rule as a per-source constraint and describe the Graphify research without naming the repository. The outcomes, scope and research findings are otherwise unchanged.

## Consequences

The architecture's pilot-scope item that restates the Terraform-only rule (pushback #17) traces to this change and is amended separately. Upstream links to the InternalOntology vision still carry the original detail for anyone who needs it.

---

AI-Fingerprint: sha256:6a04b4b3adbe

🤖 Generated with [Claude Code](https://claude.com/claude-code)
