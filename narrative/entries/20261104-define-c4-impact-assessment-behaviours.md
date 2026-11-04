---
date: 2026-11-04
slug: define-c4-impact-assessment-behaviours
title: "Define C4 impact assessment behaviours"
summary: "Accept the 15 observable behaviours in `02-behaviours.md`. An architect may start with a change request description alone, confirm candidate elements, follow only evidenced relationship steps, and produce a human-reviewed ImpactAssessment."
kind: product
status: accepted
sequence: 2026-11-04T14:07:47.000Z
evidence: "https://github.com/jamiemitchellconsultants/division-ontology/pull/41; merge commit 3b440ecc3c754807cd37a21e17041a8d1fca5b41"
---

## Context

The accepted vision calls for architects to assess change requests across participating repositories using all four C4 levels where source evidence permits. The upstream source graph distinguishes assertions, evidence, inferences, commits and coverage. The analyst phase needed precise rules for starting from a request, following possible impacts, showing partial views, and avoiding false assurance when facts are missing.

## Decision

Accept the 15 observable behaviours in `02-behaviours.md`. An architect may start with a change request description alone, confirm candidate elements, follow only evidenced relationship steps, and produce a human-reviewed ImpactAssessment. Context diagrams may show an evidenced external system whose repository has not joined, without presenting that repository as a participating source. C4Diagram and ImpactAssessment become division shared terms; existing source identities and equivalence governance remain intact.

## Consequences

The architect phase must choose the graph-to-C4 mapping, rendering and assessment output mechanism while preserving the specified evidence, snapshot and gap behaviours. Missing C4 levels remain visible and do not block source admission. The first pilot uses subjective architect and reviewer feedback without a numeric accuracy gate. The core term additions require division-steward review; generated catalogue changes follow in a separate sync PR.
