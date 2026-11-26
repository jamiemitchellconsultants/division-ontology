---
status: accepted
date: 2026-11-26
feature: 198
---

# 0032 — Pin the relationship master list to the catalog commit

Feature: docs/features/generalise-impact-assessment/

## Context

Impact direction comes from the division's relationship master list (B6–B8). Until that list exists, today's rule applies, with a gap (B9). Results and saved assessments must identify the list they used (B10, B32). #205 will define the list's words, definitions and review process, but it has not done so yet. The engine needs a stable location and a minimum shape to build against now.

## Decision

The list lives in the division repository at `relationships/master-list.json`. It is read at the catalog commit, so the snapshot pins it with no separate commit to track. Its identity is the catalog commit and the SHA-256 of the file's bytes, or `none` when the file is absent.

The engine relies on only these fields:

- `schemaVersion`;
- `relationships[]`, where each entry has a relationship `iri` and an `impact` of `subject-to-object`, `object-to-subject`, `both` or `none`.

The engine ignores any other fields. A file that is present but invalid fails packaging, as an invalid registry does today.

## Consequences

- Stewards review changes to the list through the division's normal pull requests, and every snapshot carries the list that was current at its commit.
- #205 may add fields and decide the review rules, but it must keep this location and these four impact values, or supersede this ADR.
- With no file, the engine applies B9.

## Alternatives considered

- **Pin the list separately, with its own commit, as the C4 registry does.** That adds a third pin to every command and record, for a list that changes with the division's vocabulary.
- **Express the list in `core/ontology.ttl`.** That would change the core vocabulary's shape, which is #205's decision to make.
