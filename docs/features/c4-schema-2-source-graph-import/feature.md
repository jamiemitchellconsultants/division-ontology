# Import schema 2 source graphs in C4 snapshots

## Product owner: vision

### Problem

Division maintainers need to follow a source repository's feature behaviour to the code and tests that support it, using the revision pinned by the division catalog. The published C4 importer rejects the schema 2 graph produced by the Ontology Kit, and an existing kit schema 1 graph also fails evidence role validation. The three bundled dummy repositories import successfully, so their usual demonstration misses both compatibility gaps. InternalOntology #185 needs the real importer to preserve and expose the pinned JEPA-Speech design records before its owner can complete B23. [InternalOntology #185](https://github.com/jamiemitchellconsultants/InternalOntology/issues/185) [ADR 0045](https://github.com/jamiemitchellconsultants/InternalOntology/blob/5df7c1f90ed2769c3926c58d19e79e0c03bc4e07/docs/adr/0045-require-a-compatible-division-importer.md)

### Users

- Division maintainers who build and validate catalogs with existing source repositories and need to consume their graphs through the same C4 snapshot.
- Source maintainers and reviewers who use design evidence to verify the relationship between a behaviour, an architecture decision and its implementation.
- The owner of InternalOntology #185, who needs to demonstrate that one imported JEPA-Speech behaviour leads to the same code recorded in the source graph.

### Outcomes and measures

1. **Current and existing graphs import.** The real loader imports standard kit schema 1 and 2 graphs and all three bundled dummy graphs. Measure with the existing representative catalog plus a standard kit schema 1 control and a synthetic schema 2 source. Unsupported schemas and invalid evidence remain explicit failures.
2. **The imported records match the catalog pin.** The imported design nodes retain their identity, labels, status and evidence. Their assertions retain their endpoints, relationships, basis and evidence. Measure by comparing those fields against the source Turtle, including a synthetic checkout whose current revision differs from the catalog pin.
3. **A reviewer can trace a behaviour to code.** Starting from the pinned JEPA-Speech B18 behaviour, a reviewer can follow the imported implementation assertion to the Submit symbol or through the runner component to its contained files and symbols. Measure with the existing B23 comparison in a disposable catalog after the synthetic import passes. [Pinned JEPA-Speech graph](https://github.com/jamiemitchellconsultants/JEPA-Speech/blob/3bcf7e7f5926e42231ae5dbfd9275f8166cc952c/docs/source-knowledge-graph/README.md)

### Scope

- Compatibility with schema versions 1 and 2 through the existing pinned C4 snapshot import.
- Evidence role eligibility using configuration at the pinned source revision, including the standard kit evidence roles and compatibility with the shipped dummy graphs.
- Preservation of the behaviour and decision records, their incident assertions and the evidence needed by B23, including replacement and successor relationships.
- Verification with the synthetic catalog and a disposable catalog pinned to the current JEPA-Speech trial graph.

### Out of scope

- Production division rollout or registration of JEPA-Speech in a production catalog.
- A new graph view, search interface, traversal API or change to the published catalog format.
- Changes to source repositories, the Ontology Kit schema or its graph checker.
- Model-assisted evidence or semantic review of claims.

### Research

- The published importer at be3973226d7c930e84975a7ab21485d776875606 loads the three representative dummy graphs. It rejects a kit schema 1 control on “evidence role selector mismatch” and both the synthetic and JEPA-Speech schema 2 inputs on “invalid graph schema version”. The Mac and network-disabled Node 24 Docker runs reproduced the same results. [Published graph loader](https://github.com/jamiemitchellconsultants/division-ontology/blob/be3973226d7c930e84975a7ab21485d776875606/src/c4/graph.mjs)
- The snapshot entry point already reads the source graph and configuration at the catalog-pinned source commit. The compatibility change can use that boundary, with a synthetic control that leaves the checkout on a different clean revision. [Pinned snapshot loader](https://github.com/jamiemitchellconsultants/division-ontology/blob/be3973226d7c930e84975a7ab21485d776875606/src/c4/snapshot.mjs)
- The existing B23 executable compares all design nodes and incident assertions with the source Turtle, including evidence records, and compares the reachable code for a named behaviour. The analysis can turn those checks and the importer controls into reviewable behaviours for this division feature. [B23 acceptance test](https://github.com/jamiemitchellconsultants/InternalOntology/blob/5df7c1f90ed2769c3926c58d19e79e0c03bc4e07/tests/design-links-division-acceptance.test.mjs)
