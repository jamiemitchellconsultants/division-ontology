# 0001 — Run the first C4 pilot locally from pinned Git commits

- **Status:** accepted
- **Date:** 2026-11-04
- **Feature:** [#7](https://github.com/jamiemitchellconsultants/division-ontology/issues/7) ([architecture](../features/c4-diagrams-from-source-knowledge-graphs/03-architecture.md))

## Context

The accepted behaviours require reproducible C4 views, evidence inspection, architect-authored assessments and visibility of gaps. The current division builder publishes ontology concepts and source commits, not source graph instances or an interactive workspace. The first pilot needs a runtime without deciding the separate runtime knowledge workspace feature.

## Decision

Use a local Node.js CLI with read-only Git checkouts at the commits recorded in one published division catalog. Produce deterministic JSON and Markdown/Mermaid views and local, editable assessment and feedback records. Include the catalog, source, mapping and binding revisions in every output. Human review happens in the change request's existing review process.

## Consequences

The pilot can be reproduced and inspected without a hosted service, database or new approval system. The architect must have read access and local Git objects for every participating source. A source graph absent at the recorded commit makes its C4 view unavailable but leaves its catalog participation valid. A later web workspace can consume the same view model without changing the evidence rules.

## Alternatives considered

- Extend the static catalog build to publish ready-made diagrams. This would precompute every focus and level and would not suit architect-confirmed candidates or editable assessments.
- Launch a hosted graph service and web workspace first. That adds deployment, storage and access-control decisions before the pilot has established the useful views.
