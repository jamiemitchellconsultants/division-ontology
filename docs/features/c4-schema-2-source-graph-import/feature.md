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

## Analyst: behaviours

Behaviour references use the feature slug and a permanent id, for example c4-schema-2-source-graph-import#B1. Published ids stay assigned to their original behaviours.

### Outcome 1 — Current and existing graphs import

- **B1 — Import a standard kit schema 1 graph.** Given a catalog pins a valid standard Ontology Kit schema 1 graph with its configured evidence, when the existing C4 snapshot is loaded, then that source graph is available and its existing nodes, assertions, evidence and coverage are readable with the same identities and meanings as the source.

- **B2 — Keep the bundled graphs readable.** Given the representative catalog pins the three bundled schema 1 dummy repositories with the existing legacy evidence role IRIs, when the C4 snapshot is loaded, then Ecommerce, Warehouse and Accounting still import successfully, with the same recorded graph facts and evidence as before.

- **B3 — Import a standard kit schema 2 graph.** Given a catalog pins a valid standard Ontology Kit schema 2 graph containing a behaviour, a decision and evidence-backed links, when the C4 snapshot is loaded, then the graph is available and the design nodes and links can be read from that source's imported graph.

- **B4 — Reject unsupported or malformed graph versions.** Given a source graph is present at its catalog pin and has an unsupported version such as 0 or 3, a missing version, conflicting versions or multiple manifests, when its C4 snapshot is loaded, then it fails with a diagnostic identifying the invalid schema or manifest. Given a schema 1 manifest contains a schema 2 design kind, design status, node evidence or replacement or successor relationship, then loading rejects the graph with a schema version diagnostic.

- **B5 — Apply the evidence selector for the recorded role.** Given a schema 1 or 2 graph cites evidence with a standard kit role IRI for implementation, test, selected document, ontology or deployment, when the snapshot checks the citation, then it matches the path against the corresponding source configuration key at the catalog-pinned commit. For schema 1, each corresponding legacy role IRI without the role- prefix still resolves to its historical key. Schema 2 uses the standard kit role IRIs. A citation with an unsupported role, absent matching selector or wrong role for the path fails with an evidence-role diagnostic.

- **B6 — Keep the pinned evidence checks.** Given a citation points outside the source repository, references a missing or non-regular file, has the wrong blob id or an invalid line range, or cites a path outside covered scope, when the snapshot is loaded, then it fails on that evidence and provides no partial successful snapshot. A valid citation retains its path, role, blob id and inclusive line bounds.

- **B7 — Preserve unavailable sources.** Given the catalog pins a participating source whose graph is disabled or absent at that commit, when the snapshot is loaded, then the source remains in the catalog with an unavailable graph and the reason. The schema and evidence failure behaviours apply to present graphs.

### Outcome 2 — The imported records match the catalog pin

- **B8 — Use the catalog-pinned graph and configuration.** Given a catalog pins a source commit and the clean checkout is at a different revision where the selected-document path, declarations and selectors have changed, when the snapshot is loaded, then the imported facts and evidence come from the catalog-pinned commit and its configuration. A synthetic comparison pins the lite Widgets graph while its clean checkout is at the promoted full-mode revision.

- **B9 — Preserve the design node and assertion records.** Given valid schema 2 source Turtle declares behaviour and decision nodes and assertions incident on them, when the snapshot is loaded, then its imported node set matches the source by IRI, kind, label, design status and evidence references. Its incident assertions match by IRI, subject, object, relationship, basis and evidence references. Every reference resolves to its original imported evidence, and the ordinary source graph records and coverage remain available.

- **B10 — Reject invalid design records.** Given a schema 2 behaviour or decision has a missing, conflicting or unsupported status, a status for the wrong design kind, or missing, multiple, non-IRI, dangling or wrong-role node evidence, when the snapshot is loaded, then it fails with a diagnostic identifying the invalid design node or evidence. A valid behaviour status is current or retired. A valid decision status is proposed, accepted, superseded or deprecated. Each design node has one selected-document evidence reference. These two properties are reserved for behaviour and decision nodes.

- **B11 — Preserve retirement and succession links.** Given a schema 2 graph contains a retired behaviour with a recorded replacement or a superseded decision with a recorded successor, when the snapshot is loaded, then the original and successor nodes keep their distinct IRIs and statuses and the replacement or successor assertion keeps its recorded direction and evidence. The original implementation, decision and test links remain attached to the same nodes.

### Outcome 3 — A reviewer can trace a behaviour to code

- **B12 — Follow the direct and component-mediated traces.** Given the imported JEPA-Speech graph at 3bcf7e7f5926e42231ae5dbfd9275f8166cc952c and the reference zener-shape-tracing-motor-test#B18, when a reviewer follows incoming implementation assertions in the imported records, then one trace reaches Submit directly and another reaches MotorBlockRunner. Following the runner's outgoing containment assertions reaches its recorded files and symbols, including MotorBlockRunner.cs. The returned assertions, endpoints and evidence match the source at the pin.

- **B13 — Keep a trace grounded when links are absent.** Given an imported behaviour has no implementation assertion, or an implementing component has no recorded containment route to code, when a reviewer follows the imported records, then there is no code route for that branch of the walk. A matching name supplies no edge. The available records and recorded coverage are still visible.

### Uncertainties

| ID | Question | Decision or explicit deferral |
| --- | --- | --- |
| U1 | Which graph versions and role spellings must work? | Accept standard kit role IRIs for schemas 1 and 2. Accept the five corresponding legacy role IRIs in schema 1 for existing division graphs. Schema 2 requires the standard role IRIs. Unsupported versions are explicit errors. |
| U2 | What makes a design graph valid for import? | Validate the imported record shape, status and evidence, along with the existing graph evidence and reference checks. The producer's feature-document and ADR interpretation stays with the Ontology Kit checker. |
| U3 | How should the importer expose a status value or the evidence role name? | Preserve the source's meaning and stable identities. Field representation and integration with the existing API are deferred to the architect. The B23 comparison accepts the established role and status naming forms when they identify the same values. |
| U4 | Does B23 require a new view, traversal endpoint or production deployment? | Verify the walk through the existing imported records in the disposable catalog. New presentation, traversal interfaces and production onboarding are outside this feature. |
| U5 | What distinguishes the acceptance pin from the working checkout? | Deliberately leave the synthetic source checkout on the promoted revision after publishing its older catalog pin. The deployed case uses the existing checked JEPA-Speech graph commit. |

### Terms

Use the existing PublishedKnowledgeSnapshot term for the division baseline and the source-owned Ontology Kit terms for graph nodes, assertions, evidence, behaviour and decision records. Their IRIs and meanings already exist in the shared ontology or the upstream graph vocabulary. This feature introduces no shared ontology term.

## Architect: structure

### Components

| Component | Responsibility | Behaviours |
| --- | --- | --- |
| C4 snapshot loader, `src/c4/snapshot.mjs` | Keep the catalog and source inventory as the entry point. Verify each clean checkout, origin identity and available source commit. Read that source commit's graph configuration and pass the immutable commit and configuration to the graph importer. Return the existing snapshot shape. | B1–B8, B12 |
| C4 graph importer, `src/c4/graph.mjs` | Read the manifest version and apply its format and role rules. Validate graph structure and pinned evidence with the existing coverage checks. Return the validated source graph version and the nodes, assertions, evidence and coverage. Preserve the unavailable reason for optional missing or disabled graphs. | B1–B7, B9–B11 |
| Read-only Git access, `src/c4/git.mjs` | Resolve the catalog and source objects at explicit commits. Enforce checkout identity and safe repository-relative paths, and load regular file blobs from the pinned commit for the graph importer. | B6–B8 |
| Existing C4 snapshot records | Make the validated node and assertion maps available to current callers. Design nodes expose their status and evidence beside existing fields. The B23 comparison and reviewer walk use the same imported records to follow the actual incident assertions and containment links. | B9, B11–B13 |
| Workspace bundle producer and reader, `src/workspace/bundle.mjs` and `runtime/RuntimeKnowledge.Store/BundleReader.cs` | Pass the validated source graph version to the existing bundle manifest field. Admit valid schema 1 and 2 source graphs in the existing bundle format and retain null version metadata for unavailable graphs. | B1, B3, B7–B9 |
| Disposable acceptance harness | Use the real C4 snapshot with synthetic and JEPA-Speech catalogs. Compare the imported records with canonical Turtle and exercise malformed inputs in isolated fixtures. Use existing workspace bundle tests plus a mixed source-version fixture for the downstream metadata contract. | B1–B13 |

### Structure

The catalog commit selects the committed catalog and source manifest. Each catalog record selects the source commit. The snapshot loader uses the verified checkout as access to those Git objects. The same source commit supplies the configuration, graph Turtle and evidence blobs. The division catalog's format is independent of the source graph manifest's schema version.

```mermaid
flowchart LR
  Catalog["Pinned catalog and source inventory"] --> Snapshot["C4 snapshot loader"]
  Git["Verified local source Git objects"] --> Snapshot
  Snapshot -->|Source commit and pinned configuration| Import["C4 graph importer"]
  Git -->|Turtle and evidence blobs| Import
  Import --> Records["Validated C4 snapshot records"]
  Records --> Review["Pinned evidence comparison and code trace"]
  Records --> Bundle["Workspace bundle producer"]
  Bundle --> Reader["Workspace bundle reader"]
```

The importer uses the existing Turtle parser and one validated manifest for each present graph. It reads the graph shape and version before returning records, matches each evidence role to the pinned selector key through the supported vocabulary, and validates the file, blob id, range and effective coverage. It resolves design node evidence and assertion references against the validated evidence map. Validation errors leave the C4 load unsuccessful. For a disabled or absent graph, the existing unavailable state carries the reason and empty collections. [ADR 0024](../../adr/0024-version-source-graph-validation-at-the-c4-boundary.md)

The available graph state exposes a numeric `schemaVersion` from the source graph manifest. The existing maps keep their expanded IRI keys. A behaviour or decision node adds `designStatus` as an expanded upstream IRI and `evidenceIris` as an array of evidence IRIs. The approved shape requires exactly one selected-document reference. Assertions keep their existing endpoints, relationship IRI, basis and evidence references. The evidence record's `role` remains the normalized selector key, so the standard and legacy spellings have the same interpretation in schema 1. Existing node kinds and assertion records keep their established fields. [ADR 0025](../../adr/0025-preserve-design-provenance-in-c4-snapshots.md)

The workspace bundle producer reads the version from the validated graph state and uses the existing `graphSchemaVersion` field. Its runtime reader accepts 1 or 2 for available graphs and null for unavailable graphs. The bundle envelope stays at version 1. The existing workspace projection and public request and response contracts continue to use their current fields. Complete design metadata is available in the C4 graph state and the raw pinned bundle files. [ADR 0026](../../adr/0026-record-the-actual-source-graph-version-in-workspace-bundles.md)

### Deployment and runtime

- The C4 loader runs on Node with locked repository dependencies and local Git objects. Catalog preparation and import run against verified, clean checkouts. The import uses committed inputs and the existing in-memory snapshot model.
- The local acceptance sequence uses the three bundled dummy repositories, the standard kit schema 1 control, and the synthetic schema 2 Widgets graph. Pin the Widgets catalog at the lite revision and leave the clean checkout at the promoted full-mode revision for B8. Use separate scratch cases for rejected versions, roles, evidence and design shapes.
- The deployed trial uses a disposable division catalog that pins JEPA-Speech at 3bcf7e7f5926e42231ae5dbfd9275f8166cc952c. Compare the source-owned records through the real loader before following B18 to Submit directly and through MotorBlockRunner to contained code. Capture the importer, catalog and source commit IDs. [InternalOntology B23 contract](https://github.com/jamiemitchellconsultants/InternalOntology/blob/5df7c1f90ed2769c3926c58d19e79e0c03bc4e07/docs/adr/0045-require-a-compatible-division-importer.md)
- Local acceptance can run in network-disabled Docker with the pinned repositories mounted read-only. Verify the workspace compatibility boundary with its existing bundle producer and runtime reader tests. If an existing installation adopts schema 2 source bundles, deploy the compatible reader before publishing such bundles. The disposable trial is the release evidence for this feature.
- Keep production source registration, catalog publishing, runtime activation and source-repository changes outside trial operations. The scratch catalogs and fixtures can be rebuilt from their recorded inputs.

### Traceability

| Behaviour | Components |
| --- | --- |
| B1 | C4 snapshot loader, graph importer, standard schema 1 fixture, workspace bundle producer and reader |
| B2 | C4 snapshot loader, graph importer, representative catalog and three bundled sources |
| B3 | C4 snapshot loader, graph importer, synthetic schema 2 fixture, workspace bundle producer and reader |
| B4 | C4 snapshot loader, graph importer, invalid manifest and version fixtures |
| B5 | Graph importer, C4 snapshot loader's pinned configuration, role selector fixtures |
| B6 | Graph importer, read-only Git access, pinned evidence and coverage controls |
| B7 | C4 snapshot loader, graph importer, workspace bundle producer and reader, unavailable source fixtures |
| B8 | C4 snapshot loader, read-only Git access, promoted Widgets checkout against the older catalog pin |
| B9 | Graph importer, existing C4 snapshot records, source comparison in the acceptance harness |
| B10 | Graph importer, malformed design record fixtures |
| B11 | Graph importer, existing C4 snapshot records, replacement and succession comparison |
| B12 | C4 snapshot loader, existing C4 snapshot records, pinned JEPA-Speech B23 comparison |
| B13 | Existing C4 snapshot records, missing-route controls in the acceptance harness |

### Decisions

- [ADR 0024](../../adr/0024-version-source-graph-validation-at-the-c4-boundary.md) puts format and evidence-role rules at the pinned C4 import boundary.
- [ADR 0025](../../adr/0025-preserve-design-provenance-in-c4-snapshots.md) preserves status and evidence as additions to the existing imported node records.
- [ADR 0026](../../adr/0026-record-the-actual-source-graph-version-in-workspace-bundles.md) carries the source graph version through existing workspace bundle metadata.

## User

### Runbook

#### Deploy

Acceptance runs first on James's Mac with a clean division build and disposable Git repositories for Ecommerce, Warehouse, Accounting, and Widgets. The deployed environment is the same pinned C4 loader from the merged build PR, running on the disposable division catalog for the JEPA-Speech trial. The trial is the deployed source for this feature.

1. Obtain a clean checkout of the importer revision. Install Node.js 20 or later and Git, then run `npm ci --ignore-scripts` from the division checkout. For workspace reader checks, use the SDK selected by `runtime/global.json`.
2. Obtain a clean checkout of each source listed in the catalog. Every checkout needs the configured origin and the commit recorded in the catalog. At that source commit, enable the graph in `ontology.config.json`, set the graph root, and configure evidence selectors for the roles the graph cites.
3. For the synthetic catalogs, use `npm run demo:setup` with an absolute scratch `DEMO_DIR`. Prepare the version 1 control from the Widgets `schema-1` tag and the version 2 catalog from `linked`, then leave the clean Widgets checkout at `promoted`. For the trial use the reviewed JEPA-Speech source commit `3bcf7e7f5926e42231ae5dbfd9275f8166cc952c` with its committed ontology and source graph. Register it only in the disposable catalog. [Fixture preparation and input files](../../detailed-implementations/c4-schema-2-source-graph-import/plan.md#final-pinned-verification) · [Catalog rebuild with the real builder](https://github.com/jamiemitchellconsultants/InternalOntology/blob/5df7c1f90ed2769c3926c58d19e79e0c03bc4e07/docs/features/graph-feature-document-links/feature.md#build-the-disposable-b23-catalog)
4. Keep the importer checkout separate from the disposable catalog and the source checkouts. Record all three commit IDs and their absolute paths in the fixture input. If using a workspace deployment, update the bundle reader before publishing a bundle containing a schema 2 source. [Reader compatibility decision](../../adr/0026-record-the-actual-source-graph-version-in-workspace-bundles.md)

#### Operate

Use the same pinned catalog, clean source checkouts and reviewed importer revision for each inspection. A successful load reports the recorded source commit and an available graph for valid input. Sources with optional graphs can report unavailable with a reason. Confirm the schema version and inspect the evidence path, role, blob ID and inclusive line bounds at the source commit. In the trial, compare the imported design records and both B18 code paths with the source Turtle.

**James's Mac, Terminal.** From the division checkout, set `C4_DESIGN_FIXTURES` to the absolute directory containing `dummy-v1.json`, `kit-v1.json`, `local-b23.json` and, for the trial, `deployed-b23.json`. Set `C4_DESIGN_KIT` to the absolute directory containing the pinned InternalOntology comparison at `5df7c1f90ed2769c3926c58d19e79e0c03bc4e07`. After owner approval, run the local command. After the build PR merges, rebind the input importer fields to the clean merged checkout and repeat the checks with `C4_DESIGN_TRIAL=1`.

```sh
node --test test/c4-schema-compatibility.test.mjs test/c4-design-records.test.mjs test/c4-graph.test.mjs test/c4-cli.test.mjs test/workspace-bundle.test.mjs test/acceptance/c4-design.test.mjs
```

Run `npm test` for the repository health check. For the bundle reader, run `dotnet test RuntimeKnowledge.Tests/RuntimeKnowledge.Tests.csproj --filter FullyQualifiedName~BundleGraphSchemaTests` from the `runtime` directory. Expect the available version 1 and 2 cases and unavailable null case to pass. A completed run has zero failures and no skipped checks in the selected layer.

#### Recover

Save the diagnostic and the importer, catalog and source commit IDs. Confirm the commits exist and the checkouts are clean and have the registered origins. For schema or role diagnostics, verify the source manifest and the selector configuration at the catalog pin. For evidence diagnostics, check that the path is a covered regular file at that commit and that the blob and line range match. Ask the source owner for a corrected graph commit and publish a new reviewed catalog pin, or reproduce the last working pins in a separate clean checkout. [Evidence rules](../../adr/0024-version-source-graph-validation-at-the-c4-boundary.md)

For an unavailable graph, read the reason and confirm the source's intent at the pin. For a runtime rollback, restore the last verified bundle with versions supported by the chosen reader before rolling back the reader. Preserve the failed bundle and original pin for diagnosis. Rerun the catalog and importer checks against the same intended inputs before using the recovered output.

### User guide

- **B1–B3, import a graph.** Start from the catalog commit. A standard kit graph may use version 1 or 2. The bundled Ecommerce, Warehouse and Accounting graphs still load with their legacy version 1 evidence roles. Inspect the returned schema, record maps and coverage. A version 2 graph also exposes design nodes and links.
- **B4, diagnose a rejected schema.** Inspect the source manifest at the recorded commit. There must be one manifest and one supported version. Behaviours, decisions, design status, node evidence, replacements and successions require version 2.
- **B5, read role eligibility.** Match each citation to its own pinned selector for implementation, test, selected document, ontology or deployment. Standard roles work in both versions. The corresponding unprefixed roles work in version 1. A citation without a matching selector fails the load.
- **B6, check a citation.** Open the recorded repository-relative path at the source commit. Compare the regular file's blob ID and cited lines. Check the effective coverage for that path. A present invalid citation fails the load.
- **B7, read graph availability.** A source can be listed even when its graph is disabled or absent at the pin. Read the unavailable reason before drawing conclusions from empty graph records. An invalid present graph returns a diagnostic.
- **B8–B9, inspect design provenance.** Use the pinned source revision even when the local checkout has moved. Compare each behaviour or decision's IRI, label, kind, design status and evidence reference with the source Turtle. Resolve the evidence record and compare each incident assertion's endpoints, relationship, basis and evidence. Review ordinary records and coverage alongside the design records.
- **B10–B11, review status and history.** A behaviour is current or retired. A decision is proposed, accepted, superseded or deprecated. Each design node needs exactly one selected-document citation. Follow a replacement or succession in the recorded direction and inspect the original node's links under its original IRI.
- **B12–B13, trace a behaviour.** Find the exact stable behaviour reference and follow incoming implementation assertions. For a component implementer, follow its outgoing containment assertions to code. Inspect the evidence on the actual assertions before using the trace. A missing implementation or containment assertion leaves that route empty, even if a name resembles the behaviour or component.

### Acceptance

Follow the walkthroughs first and record pass or fail for each numbered step. Inspect the named executable cases and approve them after the walkthrough passes. Run the approved cases in the local build, then repeat with the merged importer in the disposable deployed trial. Use the synthetic controls for absent or intentionally invalid inputs in both environments and the pinned JEPA-Speech graph for the deployed B18 trace. Record the date and participant for each result.

#### B1 — Standard schema 1

1. Load the `kit-v1` catalog and locate Widgets. Expect version 1, an available graph, 7 nodes, 6 assertions, 6 evidence records and 8 coverage entries.
2. Inspect the implementation citation for `src/Invoice.cs` against the pinned Turtle. Expect its path and normalized implementation role to agree with the standard role IRI. Executable cases: `B1 imports standard kit schema 1 with readable source records` and the schema 1 standard-role cases.

#### B2 — Bundled legacy graphs

1. Load `dummy-v1` and list the three source IDs and pins. Expect Accounting, Ecommerce and Warehouse to be available at version 1.
2. Inspect each graph's 4 nodes, 3 assertions, implementation evidence in `docs/architecture.md`, and 3 coverage entries. Expect the fixture facts to match. Executable case: `B2 preserves the three legacy dummy graphs`.

#### B3 — Standard schema 2

1. Load `local-b23` and inspect Widgets. Expect version 2, with behaviour and decision nodes and their evidence-backed links.
2. Resolve one selected-document citation and one link citation in the imported evidence map. Expect both to point to pinned records. Repeat against the JEPA-Speech trial after merge. Executable cases: `B3 B8 B9 B11 B12 preserve and trace the pinned Widgets graph` and the deployed comparison.

#### B4 — Rejected schemas

1. Try the version 0, version 3, missing, conflicting and duplicate-manifest scratch inputs. Expect the schema or manifest diagnostic and no imported result.
2. Try schema 1 with a design kind, status, node evidence, replacement or succession. Expect schema-version refusal. Executable cases: the manifest and under-schema-1 cases in `test/c4-schema-compatibility.test.mjs`.

#### B5 — Evidence selector eligibility

1. Compare the five configured keys with the standard evidence roles in the synthetic controls. Expect the matching path to load and normalize to its own key. Check the version 1 legacy role control against its key.
2. Try a role with a nonmatching path, an unrecognized role and a legacy role under version 2. Expect evidence-role diagnostics. Executable cases: the standard, legacy and role-rejection cases in `test/c4-schema-compatibility.test.mjs` and the Terraform selector case in `test/c4-graph.test.mjs`.

#### B6 — Pinned evidence integrity

1. Read a valid evidence record and its pinned blob and lines. Expect the recorded role, path, blob ID and bounds to agree and the path to be covered.
2. Try unsafe, missing, non-regular, stale, out-of-range and uncovered evidence in the controls. Expect each invalid source to refuse the import with the corresponding evidence or path diagnostic. Executable cases: the path, symlink, range and coverage cases in `test/c4-schema-compatibility.test.mjs`, and the stale evidence case in `test/c4-graph.test.mjs`.

#### B7 — Unavailable graph

1. Load scratch sources with disabled and missing graphs. Expect each source to stay present with an unavailable graph and a reason.
2. Compare those results with the malformed present graph and inspect the bundle version check. Expect refusal for the malformed graph and a null per-source version for an unavailable graph. Executable cases: the optional and malformed graph cases in `test/c4-graph.test.mjs`, the published gap and mixed-version cases in `test/workspace-bundle.test.mjs`, and the bundle reader schema tests.

#### B8 — Catalog pin over checkout HEAD

1. Compare the `local-b23` Widgets source commit with its clean checkout HEAD. Expect the catalog to pin the earlier lite graph and the checkout to be at the promoted full-mode revision.
2. Inspect the imported payment declaration and its evidence path. Expect the lite `docs/features/payments/feature.md` evidence and selector from the pin. Executable cases: the Widgets comparison and `preserves design provenance and both routes from the pinned config and graph`.

#### B9 — Design record fidelity

1. Compare the design nodes and incident assertions in the import with the pinned Turtle. Expect matching IRIs, labels, kinds, statuses, endpoints, relationships and basis.
2. Resolve each cited evidence ID and check ordinary graph records and coverage. Expect the original path, role, blob and range to remain available. Executable cases: the pinned Widgets and deployed comparisons and the provenance test in `test/c4-design-records.test.mjs`.

#### B10 — Rejected design records

1. Load the controls for missing, conflicting, unknown and wrong-kind statuses and misplaced design properties. Expect refusal identifying the design record.
2. Repeat with missing, multiple, literal, dangling and wrong-role node evidence. Expect an evidence diagnostic. Executable cases: the malformed record controls in `test/c4-design-records.test.mjs`.

#### B11 — Retirement and succession

1. Follow a retired behaviour's replacement and a superseded decision's successor in Widgets. Expect distinct node IRIs and preserved statuses, direction and evidence.
2. Check the original nodes' incident links and evidence against their declarations. Expect no links to move to a different node. Executable cases: the pinned Widgets comparison and the provenance test in `test/c4-design-records.test.mjs`.

#### B12 — Direct and component routes

1. In Widgets, start at `payments#B1`. Expect one route to PayInvoice and another via Ledger to `src/Invoice.cs`.
2. In the deployed import, start at `zener-shape-tracing-motor-test#B18`. Expect one route to Submit directly and one via MotorBlockRunner to `src/JepaSpeech.Study/Motor/MotorBlockRunner.cs`. Compare the traversed assertion endpoints and evidence with the pinned source. Executable cases: the Widgets and JEPA-Speech comparisons.

#### B13 — Missing routes

1. Inspect a control behaviour without an implementation assertion. Expect an empty route and inspect the remaining recorded coverage.
2. Inspect one with an implementing component and no containment to code. Expect the component assertion to be visible and the code route to stay empty. Do not infer an edge from names. Executable case: the unlinked and partial branch checks in the provenance test in `test/c4-design-records.test.mjs`.

#### Results

On 2026-11-24, James Mitchell reported completing the local walkthrough on his Mac and supplied the review report at `/private/tmp/division-175-owner-local.ZBHAyT/review.md`. The report matched the expected outputs for the pinned catalogs and 47 controls. His reply was recorded as local walkthrough pass and approval of the mapped executable cases with no exceptions reported.

The importer was pinned at `a3ebf43952ff3e5e5ce2b05f8f4286e155ee67ef`. The legacy catalog was `98fab08570aad7bbdd2526fcfd92b52845508c55`. The standard Widgets schema 1 catalog was `abb59c5a7cad5981f6395942e38ccf088687de5a` with its source at `81e756a70dbbb42fbcb51067c75ca484504547e5`. The Widgets schema 2 catalog was `1d86e69f9dc297ab8a0b02141d2a93ae41d31232` with its source at `566621b376324272efc64e895efa20d47f29c4d1` and the clean checkout at `7c574aa521410f5ee152744d9a0ef0fac22d83f5`.

After owner review, all 75 local Node cases passed on the Mac and on each network-disabled Docker Node 20, 22 and 24 run. All eight .NET bundle reader cases passed on the Mac. No cases were skipped in either local suite. The logs are in `/private/tmp/division-c4-schema-2-feature.zZaBzZ/user-evidence`. Each local row below uses the same participant and date.

| Behaviour | Local walkthrough | Local executable | Deployed walkthrough and executable |
| --- | --- | --- | --- |
| B1 | Pass | Approved and passed | Pending build PR merge |
| B2 | Pass | Approved and passed | Pending build PR merge |
| B3 | Pass | Approved and passed | Pending build PR merge |
| B4 | Pass | Approved and passed | Pending build PR merge |
| B5 | Pass | Approved and passed | Pending build PR merge |
| B6 | Pass | Approved and passed | Pending build PR merge |
| B7 | Pass | Approved and passed | Pending build PR merge |
| B8 | Pass | Approved and passed | Pending build PR merge |
| B9 | Pass | Approved and passed | Pending build PR merge |
| B10 | Pass | Approved and passed | Pending build PR merge |
| B11 | Pass | Approved and passed | Pending build PR merge |
| B12 | Pass on Widgets | Approved and passed | Pending build PR merge |
| B13 | Pass | Approved and passed | Pending build PR merge |
