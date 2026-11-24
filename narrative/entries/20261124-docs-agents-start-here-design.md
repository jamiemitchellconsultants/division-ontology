---
date: 2026-11-24
slug: docs-agents-start-here-design
title: "docs: agents start here design"
summary: "Give “Agents start here” its own document, `docs/agents-start-here.md`, pointed to from the canonical entry points, as recorded in [ADR…"
kind: product
status: accepted
sequence: 2026-11-24T18:00:13.000Z
evidence: "https://github.com/jamiemitchellconsultants/division-ontology/pull/190; merge commit 237fce3a2a74eb07d1e6403b861f849073c7c5b0"
---

## Context

This repository is a GitHub template, but an agent asked to create a division from it has no instructed path: `AGENTS.md` covers operating a division that already exists, and the creation steps live in human-facing README prose. Nothing requires the agent to ask the founder about the template’s optional setups, so it can silently skip them, enable them without consent, or report a partial setup as complete. The accepted vision and behaviours (B1–B16) require an ordered, discoverable creation path whose prompting step records every required choice and every optional setup — workspace deployment, embedding/semantic search, C4 pilot, granted sources via the source-read App, and catalogue auto-merge — each with its declined default.

## Decision

Give “Agents start here” its own document, `docs/agents-start-here.md`, pointed to from the canonical entry points, as recorded in [ADR 0027](https://github.com/jamiemitchellconsultants/division-ontology/blob/feature/agents-start-here/design/docs/adr/0027-agents-start-here-in-own-document.md). `AGENTS.md` gains only a pointer (carrying the phrase “Create a division from this template”) and the README one line; no canonical rules are copied into the new document. The prompting step and setup record live in the document as a table the agent completes in conversation and reports at handover — uncommitted and secret-free. `scripts/init-division.mjs` is unchanged; the initialiser’s CODEOWNERS rewrite transfers a new `/docs/agents-start-here.md` entry to a copy’s stewards automatically. Contract tests extend `test/onboarding-docs.test.mjs` (prompt, default, state, and link assertions) and `test/init-division.test.mjs` (survival and CODEOWNERS transfer in the existing scratch copies), so no real GitHub repository is ever created as evidence. Alternatives rejected in ADR 0027: a section inside `AGENTS.md`, a dedicated agent skill, and README-only placement.

## Consequences

A division copy inherits the document and its stewards own it, while routine agent context in `AGENTS.md` stays free of one-time creation detail. The `AGENTS.md` pointer becomes load-bearing for discoverability and is pinned by tests. The five-item optional-setup list is closed for this feature; adding a future optional feature means amending the behaviours and the document together. Unanswered optional prompts apply the declined default and are recorded as “defaulted — not answered”; administrator and consent steps are handed off as pending, never reported complete.

---

AI-Fingerprint: sha256:902f754631f9
