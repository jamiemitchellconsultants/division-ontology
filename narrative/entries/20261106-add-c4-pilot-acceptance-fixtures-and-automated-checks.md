---
date: 2026-11-06
slug: add-c4-pilot-acceptance-fixtures-and-automated-checks
title: "Add C4 pilot acceptance fixtures and automated checks"
summary: "For this pilot, use the automated B1–B15 comparisons as the acceptance record. Run acceptance on one M4 Mini and waive the duplicate second-machine run. Capture the participant's scope judgment separately as B4 feedback."
kind: product
status: accepted
sequence: 2026-11-06T11:25:30.000Z
evidence: "https://github.com/jamiemitchellconsultants/division-ontology/pull/51; merge commit bfe4681df63f8cabafa209a0e0181692adcd1117"
---

## Context

The pilot needed repeatable acceptance evidence for cross-system paths, pinned source evidence, incomplete coverage and assessment reporting. The original checklist called for manual output comparisons and a separate deployed run. The pilot runs as a CLI on a Mac with disposable sources.

## Decision

For this pilot, use the automated B1–B15 comparisons as the acceptance record. Run acceptance on one M4 Mini and waive the duplicate second-machine run. Capture the participant's scope judgment separately as B4 feedback.

## Consequences

Reruns produce one pass result or the discrepancies. The numbered scenarios and reports remain available for investigating failures. The reported evidence covers the synthetic pilot on one acceptance machine. The B4 rendering check uses sample data and the acceptance record separately preserves James's feedback.

AI-Fingerprint: sha256:cfad895d3329
