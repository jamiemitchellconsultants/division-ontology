---
status: accepted
date: 2026-11-26
feature: 198
---

# 0034 — Keep assessment drafts in browser memory, bound to the snapshot

Feature: docs/features/generalise-impact-assessment/

## Context

Any reader can draft, preview and export an assessment, but only managers can save one (B17, B19). Drafts are private to their author (U13). A draft must be refused and discarded on its next action after a snapshot change (B36). The explorer already keeps its view state in the browser (ADR 0022).

## Decision

The assessment pages hold the draft record, the chosen leads and the traced paths in memory in the Blazor WebAssembly app. The draft is bound to the `snapshotId` of its first search. Every impact and assessment request carries that id. When the API answers `snapshotChanged`, the page discards the draft and explains why. Preview and export are stateless server calls, so nothing about a draft is stored on the server.

## Consequences

- Drafts never reach the server's storage, which keeps them private with no new access rules.
- A page reload loses the draft. A Markdown export taken earlier is the reader's copy.
- B36 is enforced by the server, not by trusting the browser.

## Alternatives considered

- **Store server-side drafts for each user.** That means new storage, retention and privacy rules for state the behaviours keep private.
- **Use the browser tab storage that contribution drafts use.** The draft would survive a reload, but B36 requires discarding it once the snapshot has changed, which is most of the value of keeping it.
