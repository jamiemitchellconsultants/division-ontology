# 0025 — Preserve design provenance in C4 snapshots

- **Status:** accepted
- **Date:** 2026-11-24
- **Feature:** #175 (docs/features/c4-schema-2-source-graph-import/)

## Context

B9–B13 require design node status and node evidence to survive import alongside existing assertion and evidence records. InternalOntology B23 compares those fields from the canonical graph at the catalog pin and follows both a direct code link and a component route. [InternalOntology ADR 0045](https://github.com/jamiemitchellconsultants/InternalOntology/blob/5df7c1f90ed2769c3926c58d19e79e0c03bc4e07/docs/adr/0045-require-a-compatible-division-importer.md)

## Decision

Extend behaviour and decision records in the existing graph state's node map with `designStatus` and `evidenceIris`. The status is the expanded upstream IRI. Evidence references are represented by the same IRI array convention used for assertions, with exactly one selected-document reference on a valid design node. Resolve it through the validated evidence map and enforce the status set for that kind. Keep ordinary node shapes and existing assertion and evidence conventions.

Keep replacement and succession as the recorded assertions with their original endpoints, relationship, basis and evidence. Trace B18 using incoming implementation assertions and outgoing component containment assertions from the existing snapshot records. The walkthrough and executable comparison operate on those records.

## Consequences

The caller can compare status and provenance directly with the kit graph at the source pin. Existing graph callers can continue using the established keys and fields. The imported relationships support the direct and component-mediated B23 routes, with coverage visible for branches where the graph lacks a route.

## Alternatives considered

- Drop node metadata and expose only diagram-compatible fields. The B23 comparison would lose status and the evidence supporting the behaviour or decision declaration.
- Duplicate or inline evidence objects on each node. This would introduce another evidence representation and make it harder to compare and resolve references consistently with assertions.
- Materialize direct behaviour-to-file edges when a route goes through a component. This would introduce assertions and erase the original route's intermediate evidence.
