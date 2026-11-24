---
date: 2026-11-24
slug: docs-license-as-polyform-noncommercial-add-a-security-policy-and-state-t
title: "docs: license as PolyForm Noncommercial, add a security policy, and state that the catalog is public"
summary: "License the repository as source-available under PolyForm Noncommercial 1.0.0. Noncommercial use, modification and sharing are allowed, and commercial use needs a separate licence."
kind: product
status: accepted
sequence: 2026-11-24T12:11:20.000Z
evidence: "https://github.com/jamiemitchellconsultants/division-ontology/pull/180; merge commit d552c69d242e3dc8ea7d63c6285d206e6fe417ce"
---

## Context

The repository is being made public. It had no licence, so publishing it would have left the code all-rights-reserved with no stated terms for readers, and it had no private route for reporting a vulnerability. Its governance also said the catalog was private because it carries every concept and description from its sources. That stops being true once the repository is public.

## Decision

License the repository as source-available under PolyForm Noncommercial 1.0.0. Noncommercial use, modification and sharing are allowed, and commercial use needs a separate licence. OSI licences such as MIT and Apache-2.0 were rejected because they allow commercial use. Security reports go through GitHub private vulnerability reporting.

The catalog is public. A private repository joins only when its owner accepts that its ontology becomes public. Agents must stop onboarding a private source when they don't have that confirmation. Earlier history, including the JointBooking catalog from September to November 2026, is published as it stands and not rewritten.

## Consequences

Forks and contributions are available for noncommercial use only. Because of this the project is not open source by the OSI definition, and some package indexes and tools will flag the licence as non-standard. Onboarding a private source now needs its owner to consent to publication, which may keep some sources out. Splitting into a private catalog is still possible later.

---

AI-Fingerprint: sha256:ce3c72d331c9

🤖 Generated with [Claude Code](https://claude.com/claude-code)
