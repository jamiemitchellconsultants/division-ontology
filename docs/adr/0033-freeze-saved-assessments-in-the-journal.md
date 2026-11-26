---
status: accepted
date: 2026-11-26
feature: 198
---

# 0033 — Freeze saved assessments in the journal with their Markdown

Feature: docs/features/generalise-impact-assessment/

## Context

A saved assessment must open and export unchanged after a restart, after a new snapshot is activated, and after a rebuild (B34, Q5). It keeps its edit history (B22), and it can be edited only on its own snapshot (B33). The workspace journal is authoritative and replayable (ADR 0008). Writes commit the event and the projections together (ADR 0005).

## Decision

Saving or editing an assessment appends an `assessmentSaved` or `assessmentRevised` event to `workspace_journal.events`. The event carries:

- the frozen assessment: the record, lead details, cited path steps, evidence excerpts with page metadata, every gap, and the pins;
- the rendered Markdown bytes;
- the actor and the time.

The same transaction updates `workspace_projection.assessments` and `assessment_revisions`. It checks the expected version and the active snapshot, and refuses with `snapshotChanged` otherwise. Opening or exporting an assessment returns the stored frozen content and the stored Markdown, and never recomputes them. Replay copies them verbatim.

## Consequences

- A saved assessment never depends on a snapshot model still being loaded, or on the renderer staying unchanged.
- The journal grows by the size of each frozen assessment and its Markdown. That size is bounded by what the assessment cites.
- A record is saved whole or not at all (B18).

## Alternatives considered

- **Store only the record and recompute on open.** That breaks B34 once the snapshot's model is gone or the renderer changes.
- **Store the frozen JSON and re-render the Markdown on export.** A later renderer change would alter the export, which breaks Q5.
