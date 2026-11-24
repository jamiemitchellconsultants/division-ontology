# 0024 — Version source graph validation at the C4 boundary

- **Status:** accepted
- **Date:** 2026-11-24
- **Feature:** #175 (docs/features/c4-schema-2-source-graph-import/)

## Context

B1–B8 require one pinned snapshot entry point for standard kit schemas 1 and 2, with continued compatibility for the bundled schema 1 graphs and explicit errors for present invalid graphs. The published loader checks only for version 1 and uses the evidence role's literal suffix as a configuration key. That rejects the kit's standard role IRIs even for a valid version 1 graph. [C4 pilot boundary](0001-local-pinned-c4-pilot.md)

## Decision

Use the existing C4 graph importer as the version and evidence validation boundary. It interprets one source graph manifest at the catalog pin, supports versions 1 and 2, and uses the pinned source configuration delivered by the snapshot loader. It exposes the manifest version as a numeric `schemaVersion` for available graphs.

Resolve the five standard kit evidence role IRIs to the selector keys `implementation`, `test`, `selected-document`, `ontology` and `deployment`. Support the five corresponding unprefixed role names in the kit namespace for schema 1. Match against the selected key on the pinned configuration, then retain that key as the imported evidence record's role. Schema 2 uses the standard role IRIs.

Retain the existing checkout identity, safe Git file, blob, range, reference and coverage checks. A schema 1 manifest containing schema 2 design constructs fails the version check. An absent or disabled graph retains the existing unavailable state. Detailed interpretation of source feature documents and ADR declarations stays with the Ontology Kit producer.

## Consequences

The same C4 snapshot can import standard kit graphs and existing legacy fixtures without requiring changes in the source repositories. Changes to checkout HEAD do not change graph facts or evidence for an unchanged catalog pin. A present invalid graph fails the load, preserving the distinction between structural refusal and unavailability. The importer and its tests track the supported upstream version and role vocabulary.

## Alternatives considered

- Per-source conversion of graph Turtle or configuration before import. This would validate different bytes from those named by the catalog and require a second source of provenance.
- A separate schema 2 importer or C4 entry point. This would duplicate pinned reads, evidence checks and coverage behaviour.
- Loading any numeric schema version and mapping evidence roles by pattern. This could accept unsupported meanings and silently change evidence eligibility.
