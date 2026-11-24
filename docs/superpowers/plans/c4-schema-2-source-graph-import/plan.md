# C4 schema 2 source graph import Implementation Plan

> **For agentic workers:** Use superpowers:executing-plans to implement this plan task by task with test-driven development. The detailed plan contains the executable red and green steps.

**Goal:** Import pinned Ontology Kit schema 1 and 2 graphs in C4, retain design provenance, and carry the actual source graph version through workspace bundles.

**Architecture:** Extend the existing C4 importer with explicit version and role maps and validated design node fields. Preserve the pinned snapshot and graph map interfaces. Distribute the manifest version through the existing bundle metadata and validate its supported values in the runtime reader.

**Tech Stack:** Node.js 20 or later, the locked N3 dependency, local Git, .NET SDK 10.0.402 with the repository's latest-patch rule, xUnit.

**Spec:** [Merged vision, behaviours and architecture](../../../features/c4-schema-2-source-graph-import/feature.md), [ADR 0024](../../../adr/0024-version-source-graph-validation-at-the-c4-boundary.md), [ADR 0025](../../../adr/0025-preserve-design-provenance-in-c4-snapshots.md), and [ADR 0026](../../../adr/0026-record-the-actual-source-graph-version-in-workspace-bundles.md).

**Detailed steps:** [Test-first implementation plan](../../../detailed-implementations/c4-schema-2-source-graph-import/plan.md).

## Global Constraints

- Begin on `feature/c4-schema-2-source-graph-import/build` after the plan PR merges and the Dev Build gate opens. Test, review, commit and push each task separately.
- Use the existing `loadSnapshot({ divisionRoot, catalogCommit, checkouts })` entry point and its pinned source configuration. Keep imports on the local Git objects and retain the present-but-invalid and absent-or-disabled distinction.
- Version 1 supports the five legacy aliases and the five standard kit roles. Version 2 uses the five standard kit role IRIs. Preserve the existing eligibility, blob, line, regular-file, endpoint and coverage checks.
- Keep the catalog, workspace bundle envelope, projection and API shapes. Use the existing internal source graph version field in the bundle manifest. Accept available source graph versions 1 and 2 and an unavailable graph's null version.
- Use disposable catalogs and the existing JEPA-Speech pin for integration. Stage only the task's files. Verify builds and reproducible catalog outputs in scratch directories under the repository's Git and review rules.

## Review Focus

- A scope that only tests canonical schema 2 could break the three legacy schema 1 sources or reject a standard schema 1 graph. Task 1 checks both role families, allowed version literals and exact selector resolution.
- A liberal version check could import schema 2 node or link semantics declared under schema 1. Task 1 tests each new property, design kind and relationship under schema 1 as well as malformed manifests.
- A clean checkout can be ahead of the catalog pin. Task 1 checks a committed configuration and declaration path change with the catalog kept on the older revision.
- A design node can reference valid evidence of the wrong role. Task 1 distinguishes role eligibility from the selected-document requirement and checks missing, ambiguous, malformed and dangling design members.
- The workspace's version is per source. Task 2 verifies mixed versions, unavailable sources, rejected combinations and the existing bundle envelope and OpenAPI contract.

## Task 1 — Validate and preserve C4 source graphs

**Files:** Modify `src/c4/graph.mjs`. Add `test/c4-schema-compatibility.test.mjs` and `test/c4-design-records.test.mjs`. Reuse the existing C4 Git fixture and `src/c4/snapshot.mjs`.

**Interfaces:** `loadSourceGraph({ checkout, commit, config })` returns the existing graph state and numeric `schemaVersion` for an available graph. Design nodes add `designStatus` as an expanded IRI and `evidenceIris` with the one resolved document evidence IRI. Existing evidence roles expose the normalized selector key. The same snapshot maps support B12 and B13.

- [ ] Add and run the red cases from the detailed plan.
- [ ] Implement the version, role and design record rules together. Run the targeted C4 and full Node suites.
- [ ] Rebuild and verify the disposable catalog, then commit and push `feat(c4): import pinned schema 2 source graphs (#175)`.

## Task 2 — Preserve per-source workspace bundle versions

**Files:** Modify `src/workspace/bundle.mjs` and `runtime/RuntimeKnowledge.Store/BundleReader.cs`. Add `runtime/RuntimeKnowledge.Tests/BundleGraphSchemaTests.cs` and append the specified case to `test/workspace-bundle.test.mjs`.

**Interfaces:** Consume Task 1's `graphState.schemaVersion`. Keep `buildBundle`, `BundleReader.ReadAsync`, `SourceRevision` and their existing shape. `graphSchemaVersion` is the validated source version or null.

- [ ] Add the mixed-version producer and reader tests. Observe the wrong reported version and the reader's version 2 refusal.
- [ ] Use the validated source version in the producer and extend the existing reader check. Run Node and .NET focused suites and compare the regenerated API spec.
- [ ] Rebuild and verify the disposable catalog, then commit and push `fix(workspace): retain source graph versions in bundles (#175)`.
- [ ] Validate the final importer pin against the representative catalog, the standard kit schema 1 control and the synthetic and JEPA-Speech B23 comparison. Save results for the User phase.
