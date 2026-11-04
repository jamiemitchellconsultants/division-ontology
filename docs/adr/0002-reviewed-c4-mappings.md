# 0002 — Use reviewed C4 mappings and explicit coverage gaps

- **Status:** accepted
- **Date:** 2026-11-04
- **Feature:** [#7](https://github.com/jamiemitchellconsultants/division-ontology/issues/7) ([architecture](../features/c4-diagrams-from-source-knowledge-graphs/03-architecture.md))

## Context

The upstream source graph has stable nodes, evidence, kinds and containment assertions, but its `kit:component` kind does not distinguish a C4 container from a C4 component. Its file coverage does not establish complete architectural coverage. The accepted behaviours require correct boundaries and visible missing levels without inventing facts.

## Decision

Keep a versioned, reviewed C4 presentation registry in division ontology. Each mapped item identifies its source node, C4 level, parent boundary and supporting assertion or evidence. The projector uses only supported mappings and graph assertions. It marks missing, ambiguous or unsupported items and levels as unresolved; it never derives a boundary, relationship or no-impact conclusion from a label or an empty result.

## Consequences

Mappings can be reviewed independently of source-owned facts and the existing ontology `links/*.json`. Source graph revisions may invalidate mappings, so the pilot checks their references against the pinned commits before presenting supported views. The mapping registry is authored data and must not be mixed with generated `dist/` changes in a PR.

## Alternatives considered

- Infer levels and boundaries from node names, file paths or broad kind labels. That would present guesses as supported architecture.
- Require every source to publish all four levels before joining. That conflicts with partial-source admission and hides useful facts.
