# Division ontology first release — design

**Date:** 2026-09-26
**Status:** Approved direction; first implementation authorized by the request to create a working private repository.

## Purpose

Create a stewarded shared vocabulary and a complete, sourced view of participating repository ontologies. Start with `jamiemitchellconsultants/JointBooking`; more repositories can be added without changing the model. The first shared term is `StaffId`, approved from JointBooking's existing definition. This release must not silently treat matching labels as equivalent concepts.

## Ownership and identity

- `core/ontology.ttl` is steward owned. A pull request to this repository is the proposal and review path for a shared term. The first release contains `StaffId` only.
- `sources.json` lists participating repositories, their repository-relative Turtle path and expected namespace. JointBooking is the sole initial source.
- `links/JointBooking.json` records an explicit `exact` link from JointBooking's `StaffId` IRI to the division `StaffId` IRI. Future `specializes` links may name a narrower local concept. Only `exact` and `specializes` are accepted.
- Source repositories remain authoritative for their own ontology. The division build reads their committed state; it never rewrites it.
- The division namespace is `https://github.com/jamiemitchellconsultants/division-ontology#`. Project IRIs remain in their own namespaces. A generated catalog displays source repository and commit beside each concept.

## Build and outputs

A Node.js CLI uses the N3 RDF parser to read Turtle. It accepts `--checkout owner/name=/absolute/path` to use a clean local checkout, or clones configured GitHub URLs into temporary directories when no checkout override is supplied. Git commands use argument arrays, never shell interpolation. Source Turtle is read from a regular blob at the recorded commit. The build rejects dirty supplied checkouts and checks their `origin` against the source manifest to keep provenance honest.

`node src/cli.mjs build` validates all inputs and writes these generated files only after the entire build succeeds:

- `dist/catalog.json`: every named concept, shared term, relation and rule, with IRI, label, description, source repository and source commit.
- `dist/catalog.md`: a readable view grouped by repository, including explicit shared links.
- `dist/combined.nq`: all source and core RDF quads in named graphs, so provenance survives machine use.

A concept is a named subject inside a source namespace with a label or RDF type. Reserved metadata and invariant subjects beginning with `_` are represented as metadata or rules rather than concepts. Every original source triple remains in the N-Quads output. Output order is stable. `node src/cli.mjs check` computes the same files without writing and fails when committed outputs are stale.

## Validation and failure policy

- Parse with a general Turtle parser. Reject malformed Turtle, missing sources, a source namespace mismatch, duplicate source IDs, uncommitted source changes, mismatched checkout origins, non-regular ontology blobs, invalid link relations, unresolved link IRIs, links between non-classes, and an `exact` link whose labels differ.
- A `specializes` link need not share the core label. Similarity and duplicate-name suggestions are advisory and outside the blocking first release.
- On validation failure, leave all previously generated output files intact. The committed catalog records each source commit, making freshness inspectable.
- A local build can use the existing JointBooking checkout. Remote cloning depends on the operator's existing GitHub access; no credential is stored in this repository.

## First release boundary

This repository publishes and checks the division core, explicit links and combined catalog. Existing repository CI is unchanged in this first release. A later Ontology Kit change can make each repository's own term check consume a pinned division-core snapshot; that integration is specified separately, because the current kit checks one local ontology document and JointBooking uses a different Turtle projection. The first release demonstrates the shared term through its explicit link and build validation, while the initial catalog includes every JointBooking concept.

## Verification

Tests cover a realistic two-source fixture, all concepts and quads, explicit link validation, namespace collisions, malformed input, dirty checkout, stale-output detection and preserved outputs on failure. A build against the clean local JointBooking checkout must succeed; a subsequent check against its generated outputs must pass. The CI workflow runs the tests without needing access to the private JointBooking repository.
