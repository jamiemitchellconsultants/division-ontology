---
date: 2026-11-24
slug: docs-agents-start-here-implementation-plan
title: "docs: agents start here implementation plan"
summary: "One increment of five test-first tasks: (1) pin the document contract as failing tests in `test/onboarding-docs.test.mjs`; (2) write `docs/agents-start-here.md` to the settled outline — prompt tables with B6’s declined defaults, the…"
kind: product
status: accepted
sequence: 2026-11-24T18:15:02.000Z
evidence: "https://github.com/jamiemitchellconsultants/division-ontology/pull/193; merge commit 4675d41ccd9118a9cce4a96eb47372c594fbcdb6"
---

## Context

The design for #189 is accepted and merged (#190, ADR 0027): a new `docs/agents-start-here.md`, discovered through pointer-only hooks in `AGENTS.md` and the README, with tests extending the two existing onboarding test files. Since then the feature lifecycle kit was synced from claude-plugins (#192), which added frontmatter checks on feature artefacts and increment-based dev-plan criteria. The plan must satisfy both the accepted behaviours and the new kit without re-deciding the design.

## Decision

One increment of five test-first tasks: (1) pin the document contract as failing tests in `test/onboarding-docs.test.mjs`; (2) write `docs/agents-start-here.md` to the settled outline — prompt tables with B6’s declined defaults, the uncommitted setup-record table, create/clone, initialise-and-verify, linked setups with pending hand-offs, first-sync check, handover; (3) add the `AGENTS.md`/`README.md` pointers and the `/docs/agents-start-here.md` CODEOWNERS line; (4) pin survival and steward ownership in `test/init-division.test.mjs`; (5) verify with the full suite, the frontmatter check, and a scratch walkthrough. The plan settles that survival and CODEOWNERS transfer need no `init-division` change (its template-history list and CODEOWNERS rewrite already cover them), that the setup record is a conversation artefact committed nowhere, and that template creation uses `gh repo create --template` (verified on gh 2.104) or GitHub’s **Use this template**.

## Consequences

The build can be executed task-by-task from this plan without further decisions, and can stop safely after the single increment. Contract tests pin load-bearing phrases rather than prose, so stewards can edit wording but not silently rename a setup, a default, or an answer state. Because the plan files live under `docs/superpowers/` and `docs/detailed-implementations/`, division copies will not keep them after `init-division` — only the runbook itself carries over, as intended.

---

AI-Fingerprint: sha256:9ed1b25716e3
