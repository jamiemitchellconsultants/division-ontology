---
status: accepted
date: 2026-11-28
feature: 198
---

# 0036 — Repair a missing impact model whenever a snapshot is imported

Feature: docs/features/generalise-impact-assessment/

## Context

ADR 0030 stores each snapshot's impact model with its candidate projection, in the same transaction. Staging is skipped when the snapshot's projection already exists. A worker from before this feature accepts a bundle that carries `impactInput` and stages it without a model, because it does not know the file role. If that happens during an upgrade, a newer worker never creates the model: restaging finds the snapshot already staged, and activation only moves the pointer. The active snapshot then answers `impactUnavailable` (B15, B16), and replay builds a model the live projection lacks, so cutover fails its checksum (Q5). The PR #220 review found this (item 22).

The deployments do not stop the worker before an upgrade. AWS runs each Lambda version as it is published, and the Mac and home-lab containers restart one at a time.

## Decision

Importing a snapshot always ensures its impact model. When the bundle has `impactInput` and the current generation has no model row for the snapshot, the worker builds and stores the model in the same transaction. It does this whether the snapshot is being staged for the first time, is already staged, or is being activated. Staging and activation therefore stay idempotent and repair the model, so mixed-version workers recover by themselves: the scheduled import's next run for the manifest fills the gap.

Replay builds models only for snapshots it activates, and the state checksum covers models only for snapshots activated in the journal prefix. A repaired model is counted once its snapshot is active, as replay counts it.

## Consequences

- No upgrade step is needed for the impact model, and the runbook need not order worker restarts.
- Activation reads the bundle once more when the model is missing, which costs one model build (Q1's cold-load measure). When the model is present, activation does no extra work.
- A bundle without `impactInput` still has no model, and its snapshot still answers `impactUnavailable` (ADR 0030).

## Alternatives considered

- **No mixed-version workers.** The runbook would require stopping the old worker before the new one starts. That needs no code, but it relies on an operator remembering it. Lambda's version rollout gives no such guarantee.
- **A separate backfill command.** An operator would run it after an upgrade. It has the same weakness, and it leaves the snapshot unavailable until someone notices.
