# 0003 — Bind cross-source identities through reviewed evidence

- **Status:** accepted
- **Date:** 2026-11-04
- **Feature:** [#7](https://github.com/jamiemitchellconsultants/division-ontology/issues/7) ([architecture](../features/c4-diagrams-from-source-knowledge-graphs/03-architecture.md))

## Context

The source graph checker requires each assertion endpoint to resolve to a node in that source graph. A source can describe an external system, but that node does not automatically identify a node in another participating repository. The accepted behaviours require evidenced cross-repository paths while retaining both source identities and keeping unjoined repositories out of the source inventory.

## Decision

Use a division-owned, reviewed binding from an external source graph node to a specific node in another participating source graph. Require the source assertion and evidence for the relationship to the external node, plus evidence for the identity binding. Present both references and both node IDs in a cross-source path; never turn the path into an unqualified direct graph assertion or an ontology `exact` link.

## Consequences

An evidenced external system can appear in context even before its repository joins. A cross-repository transition becomes available only after the other source participates and the identity evidence is reviewed. Bindings must be revalidated at the pinned source commits; a stale or ambiguous binding remains a visible gap.

## Alternatives considered

- Join nodes automatically when their labels match. Equal labels do not prove identity.
- Rewrite source graphs so assertions point directly into other repositories. That would break the source graph's local reference contract and make one source responsible for another source's identifiers.
