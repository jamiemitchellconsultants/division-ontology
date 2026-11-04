---
date: 2026-11-04
slug: plan-c4-impact-assessment-pilot
title: "Plan C4 impact assessment pilot"
summary: "Plan the pilot as ten test-first tasks: pinned catalog and source loading, source graph and coverage parsing, reviewed C4 mappings and cross-source bindings, candidate search, directed impact paths, four-level projection, pinned code…"
kind: product
status: accepted
sequence: 2026-11-04T18:31:08.000Z
evidence: "https://github.com/jamiemitchellconsultants/division-ontology/pull/47; merge commit ce5437abf83247810978f4ba5e5a3dfc064f9f5c"
---

## Context

The accepted C4 impact assessment vision, B1–B15 behaviours, architecture and ADRs call for a local, pinned-source pilot with evidence-backed views and architect-authored assessments. The developer needs an independently reviewable build sequence before code is written. The current catalog contains JointBooking, whose pinned source checkout has no source graph.

## Decision

Plan the pilot as ten test-first tasks: pinned catalog and source loading, source graph and coverage parsing, reviewed C4 mappings and cross-source bindings, candidate search, directed impact paths, four-level projection, pinned code inspection, deterministic rendering, editable assessments and qualitative feedback, then CLI integration. The detailed plan fixes interfaces, schemas, fixture data, tests and a commit and push for each task. Synthetic two-source fixtures prove cross-source and four-level behaviour without inventing JointBooking facts.

## Consequences

The build phase can implement the accepted architecture in small reviewable commits after this plan is reviewed and merged. Missing source graphs and stale references remain visible gaps. Real JointBooking C4 paths depend on upstream source graph adoption. This PR changes only authored plans; generated dist/ remains separate.
