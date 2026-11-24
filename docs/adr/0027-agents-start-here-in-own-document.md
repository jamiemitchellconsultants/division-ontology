---
status: accepted
date: 2026-11-24
feature: 189
---

# 0027 — Give “Agents start here” its own document, pointed to from the canonical entry points

Feature: docs/features/agents-start-here/

## Context

An agent asked to create a division from this template has no instructed path: `AGENTS.md` is the canonical instruction file every agent tool is pointed at, but it covers operating a division that already exists, and the template-creation steps live in human-facing README prose. Behaviours B1, B5, and B13 require the creation path to be discoverable where agents already look, to present required and optional setup prompts, and to point onward without restating canonical rules. `AGENTS.md` also records why pointers must stay thin: a stale copy is worse than no copy, because an agent cannot tell which one is current.

## Decision

Put the creation runbook in its own document, `docs/agents-start-here.md`, and make it discoverable with pointer-only hooks: a short “Creating a division from this template” pointer in `AGENTS.md` (carrying the agent request phrase “Create a division from this template”) and one line in the README’s “Minimal instructions for an agent”. The document contains the ordered creation path, the prompting step, and the setup-record form; for every setup or ongoing operation it links to the existing canonical document rather than copying its content. A `/docs/agents-start-here.md` line in `.github/CODEOWNERS`, beside the `/AGENTS.md` line, makes the copy’s stewards own it after initialisation.

## Consequences

The creation path can grow — prompts, defaults, stop conditions — without lengthening the instruction file every agent loads for routine work, and ongoing rules keep exactly one home. The pointer in `AGENTS.md` becomes load-bearing: if it is removed, the document is undiscoverable, so the contract tests pin both pointers as well as the document’s content. The document must survive `init-division`, which it does because it is not in the initialiser’s template-history list, and its CODEOWNERS ownership transfers automatically because the initialiser rewrites every CODEOWNERS entry. Reviewers of the pointer lines should reject any wording that begins to restate rules instead of pointing.

## Alternatives considered

- Add the creation path as a section of `AGENTS.md`. Every agent would carry one-time creation instructions in its routine context, and the file’s operating instructions and creation runbook would drift at different rates. Rejected.
- Ship a dedicated agent skill under `.agents/skills/`. Heavier machinery than a document warrants, and the project’s skills are persona-scoped; the behaviours settle for pointer discoverability (U6). Rejected for this feature.
- Put the runbook in the README only. The README is human-facing and is not the file agent tools are pointed at, so an agent given the founder’s request would still have no instructed path. Rejected.
