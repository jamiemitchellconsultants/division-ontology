---
date: 2026-11-04
slug: build-c4-impact-assessment-pilot
title: "Build C4 impact assessment pilot"
summary: "Implement the local C4 pilot: pinned snapshot and graph readers, reviewed mapping and binding registries, candidate search, directed paths, four-level projections, inspected-code evidence, deterministic reports, and editable assessment…"
kind: product
status: accepted
sequence: 2026-11-04T20:14:12.000Z
evidence: "https://github.com/jamiemitchellconsultants/division-ontology/pull/49; merge commit 2018ae25407331c2a8352ecdd505cd6e20964548"
---

## Context

Architects need a reproducible way to investigate change-request impacts across participating repositories while retaining source ownership, evidence and unknown coverage. The accepted C4 architecture and merged developer plan define a local pilot over pinned catalog and source commits.

## Decision

Implement the local C4 pilot: pinned snapshot and graph readers, reviewed mapping and binding registries, candidate search, directed paths, four-level projections, inspected-code evidence, deterministic reports, and editable assessment and feedback records. Empty production registries avoid inventing mappings. Coverage gaps and separate source identities remain visible, and code-view citations are revalidated against the recorded snapshot.

## Consequences

Architects can review evidence and record their own conclusions without an approval command. Source graphs remain optional and source-owned; JointBooking cannot supply graph-backed C4 paths until its source publishes a graph at a catalog-pinned revision. Generated catalog files are unchanged.
