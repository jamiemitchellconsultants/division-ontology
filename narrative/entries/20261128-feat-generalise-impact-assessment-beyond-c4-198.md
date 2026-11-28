---
date: 2026-11-28
slug: feat-generalise-impact-assessment-beyond-c4-198
title: "feat: generalise impact assessment beyond C4 (#198)"
summary: "Build the plan in docs/superpowers/plans/generalise-impact-assessment/plan.md as written, increment by increment, with the C4 baseline in test/impact-corpus as the arbiter of B37."
kind: product
status: accepted
sequence: 2026-11-28T10:37:43.000Z
evidence: "https://github.com/jamiemitchellconsultants/division-ontology/pull/220; merge commit 448b3937f00ef1ba5bb9874bd1e68d6738ff26eb"
---

## Context

The impact assessment only worked on C4-mapped sources and ran on a Node engine that the workspace
could not share. The reviewed plan replaces it with one .NET engine used by both the command line
and the workspace.

## Decision

Build the plan in docs/superpowers/plans/generalise-impact-assessment/plan.md as written, increment
by increment, with the C4 baseline in test/impact-corpus as the arbiter of B37.

## Consequences

Architects need the .NET SDK to run `npm run c4`. The Node CI job installs it. The workspace gains
impact search, trace and saved assessments.
