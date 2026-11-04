# C4 impact assessment pilot implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task by task. Steps use checkbox (`- [ ]`) syntax for tracking. Read the detailed companion plan before each task.

**Goal:** Give an architect a reproducible, evidence-cited local C4 view and editable impact assessment for a change request at one published division catalog snapshot.

**Architecture:** A Node.js CLI reads `dist/catalog.json` and `sources.json` from a specified division commit, then reads optional source graphs and source blobs from clean source checkouts at the catalog's pinned commits. Reviewed mapping and cross-source binding registries determine presentation; search, traversal, projection, rendering and assessment modules never add graph facts. The output is deterministic JSON and Markdown/Mermaid plus an architect-authored assessment and separate pilot feedback.

**Tech Stack:** Node.js 20+, Git, existing `n3` dependency and Node test runner; no server, database, model call or source-code execution.

**Spec:** `docs/features/c4-diagrams-from-source-knowledge-graphs/01-vision.md`, `02-behaviours.md`, `03-architecture.md`; ADRs 0001–0003. Exact signatures, schemas, fixtures and test contents are in `docs/detailed-implementations/c4-diagrams-from-source-knowledge-graphs/plan.md`.

## Global constraints

- Keep `sources.json`, source ontologies, core ontology, and generated `dist/` unchanged in the build PR; C4 is optional for source admission (B15).
- Read every source fact and fallback code blob at its catalog commit. A missing graph is an unavailable C4 gap; a missing checkout, wrong remote, dirty checkout or unavailable commit is an error (B11–B13).
- Treat catalog concepts as search leads and ontology relations as vocabulary, never as source graph instance edges (B1–B2).
- Require reviewed, explicit mapping and identity evidence. Equal labels never join nodes, set boundaries, or prove no impact (B2, B5–B7, B10–B11).
- Keep direct and inferred graph assertions distinct; retain cited source paths, lines, blob IDs, basis and source commits (B10–B12).
- No command approves a change request. Feedback is qualitative and separate from graph facts (B3–B4).
- For the JointBooking pilot, only Terraform may be used as deployment evidence; enforce the source's eligible role selectors at its pinned commit.
- Each task is test first, ends in a focused commit and push, and requires a staged diff review. Run `npm test` after each task; run build and check against clean configured source checkouts if existing builder inputs change.

## File map and interfaces

| File | Responsibility |
| --- | --- |
| `src/c4/git.mjs` | Safe read-only Git operations and checkout identity verification. |
| `src/c4/snapshot.mjs` | Published catalog snapshot and source inventory/revision lock. |
| `src/c4/graph.mjs` | Source graph parser, evidence checks, coverage and gaps. |
| `src/c4/registry.mjs` | Reviewed mapping and binding schemas, pinned registry load and reference validation. |
| `src/c4/candidates.mjs` | Description and exact-IRI search leads. |
| `src/c4/paths.mjs` | Bounded directed assertion traversal with distinct binding steps. |
| `src/c4/project.mjs` | Four C4 levels, boundaries, provenance and coverage gaps. |
| `src/c4/code-evidence.mjs` | Architect-inspected, pinned code fallback and blob/line validation. |
| `src/c4/render.mjs` | Stable JSON and Markdown/Mermaid views, tables and links. |
| `src/c4/assessment.mjs` | Editable assessment validation and review rendering. |
| `src/c4/feedback.mjs` | Separate qualitative pilot feedback validation/rendering. |
| `src/c4/cli.mjs` | `search`, `paths`, `view`, `assessment-init`, `assessment-render`, `feedback-render` commands. |
| `c4/mappings.json`, `c4/bindings.json` | Authored reviewed registries; empty initial arrays, version 1. |
| `test/c4-*.test.mjs`, `test/fixtures/c4/` | Synthetic two-source graph and no-graph fixtures; complete tests in companion plan. |

The common `Snapshot` has `catalogCommit`, `catalogSha256`, catalog source IDs/commits, and loaded source graph states. Every public result carries that identity and `registryCommit`/registry digests. Source keys are `(sourceId, nodeIri)` pairs, not labels. All externally serialized arrays are sorted by stable IDs.

## Review focus

1. A clean checkout whose origin points to a different repository must fail before combining graph facts; Task 1 tests it.
2. A stale graph assertion, evidence blob, mapping or binding must become an explicit error or unresolved gap, never a supported item; Tasks 2–3 test it.
3. An empty search or path with partial coverage must report unknown rather than no impact; Tasks 4–5 test it.
4. Equal labels and an unjoined external node must remain distinct, with no invented participating source; Tasks 5–6 test it.
5. A code fallback whose blob or line range is absent at the pinned commit must be rejected; Task 7 tests it.

---

### Task 1: Pin the catalog and source checkouts

**Files:** Create `src/c4/git.mjs`, `src/c4/snapshot.mjs`, `test/c4-snapshot.test.mjs`, and `test/fixtures/c4/fixture-repos.mjs`.

**Interfaces:** Produce `loadSnapshot({ divisionRoot, catalogCommit, checkouts }) -> Snapshot`; `readAtCommit(checkout, commit, path) -> string|null`; `verifyCheckout(checkout, expectedUrl, commit) -> void`. `checkouts` maps every catalog source ID to an absolute checkout path.

- [ ] Write tests for exact catalog bytes/digest and source commits, missing checkout, wrong origin, dirty checkout, unavailable commit, and a configured source without a graph.
- [ ] Run `node --test test/c4-snapshot.test.mjs`; verify the new tests fail.
- [ ] Implement read-only `git show`/`git ls-tree` using argument arrays and strict paths; read `sources.json` and `dist/catalog.json` at `catalogCommit`, verify source sets and commit IDs, then load each pinned `ontology.config.json` if present.
- [ ] Run the focused test and `npm test`; verify both pass.
- [ ] Review staged diff; commit `feat(c4): pin catalog and source snapshots`; push.

### Task 2: Parse source graphs and report evidence/coverage

**Files:** Create `src/c4/graph.mjs`, `test/c4-graph.test.mjs`; extend the graph fixture helper.

**Interfaces:** Produce `loadSourceGraph({ checkout, commit, config }) -> GraphState` and `coverageFor(graph, path) -> {status, reason, coverageIri}`. Consume Task 1's pinned reader.

- [ ] Write tests for valid node/assertion/evidence/coverage resources, missing optional graph, malformed graph, stale cited blob, inferred basis, priority coverage selection and unknown paths.
- [ ] Run the focused test and see failures.
- [ ] Parse all committed Turtle shards below `sourceGraph.root` with `n3`; accept the upstream schema version and required predicates, verify evidence against `git show` at the commit, and return explicit graph gaps.
- [ ] Run focused tests and `npm test`; review staged diff.
- [ ] Commit `feat(c4): read pinned source graphs and coverage`; push.

### Task 3: Validate reviewed C4 mappings and cross-source bindings

**Files:** Create `src/c4/registry.mjs`, `c4/mappings.json`, `c4/bindings.json`, `test/c4-registry.test.mjs`.

**Interfaces:** Produce `loadRegistries({ divisionRoot, registryCommit, snapshot }) -> RegistryState`, `mappingFor(registry, sourceId, nodeIri)`, and `bindingFrom(registry, sourceId, externalNodeIri)`. Read registries at an explicit Git commit and include its SHA plus file digests.

- [ ] Test schema version, unique source/node keys, four levels, valid parent and support references, mapped kind, stale evidence, external target participation, binding identity evidence, and equal-label non-binding.
- [ ] Verify focused tests fail; implement validation with explicit `supported`/`unresolved` outcomes and stable sorting.
- [ ] Run focused tests and `npm test`; review staged diff.
- [ ] Commit `feat(c4): validate reviewed mappings and bindings`; push.

### Task 4: Find architect-confirmed starting leads

**Files:** Create `src/c4/candidates.mjs`, `test/c4-candidates.test.mjs`.

**Interfaces:** Produce `findCandidates({ snapshot, registry, query, exactIri }) -> CandidateResult`. Each lead carries source ID, commit, match text, source-owned IRI, support status and reason.

- [ ] Test description-only graph matches, concept-only lead, exact IRI, no match, stable order and equal labels in separate sources.
- [ ] Verify focused tests fail; implement matching without choosing an impact conclusion or promoting unsupported leads to starting nodes.
- [ ] Run focused tests and `npm test`; review staged diff.
- [ ] Commit `feat(c4): find evidence-labelled candidates`; push.

### Task 5: Read directed impact paths

**Files:** Create `src/c4/paths.mjs`, `test/c4-paths.test.mjs`.

**Interfaces:** Produce `readImpactPaths({ snapshot, registry, starts, maxDepth }) -> PathResult`. Each path is an ordered list of assertion steps and separately labelled binding transitions.

- [ ] Test two-hop direction and evidence, inferred citation, explicit cross-source binding, stale binding gap, equal-label no-edge, cycle bound and empty-path unknown.
- [ ] Verify focused tests fail; implement bounded traversal from confirmed supported node keys only.
- [ ] Run focused tests and `npm test`; review staged diff.
- [ ] Commit `feat(c4): trace evidenced impact paths`; push.

### Task 6: Project context, container, component and code views

**Files:** Create `src/c4/project.mjs`, `test/c4-project.test.mjs`.

**Interfaces:** Produce `projectView({ snapshot, registry, focus, level, supplement }) -> ViewModel`. The model carries supported items, assertion-backed edges, evidence references, next-level focus links and unresolved gaps.

- [ ] Test B5–B7 boundaries, B8 graph-backed code, B9 next-level focus, B10 unresolved node support, B13 missing level, B14 external context and B15 partial source contribution.
- [ ] Verify focused tests fail; implement projection from supported mappings and source assertions only.
- [ ] Run focused tests and `npm test`; review staged diff.
- [ ] Commit `feat(c4): project evidence-backed four-level views`; push.

### Task 7: Verify architect-inspected code fallback

**Files:** Create `src/c4/code-evidence.mjs`, `test/c4-code-evidence.test.mjs`; connect it in `src/c4/project.mjs`.

**Interfaces:** Produce `verifyCodeSupplement({ snapshot, focus, supplement }) -> VerifiedSupplement`. Supplement fields are source ID, repository-relative path, start/end lines, blob ID, component node key and architect reason.

- [ ] Test valid pinned source line, wrong blob, missing path, out-of-range lines, wrong component, code-only fallback and non-Terraform JointBooking deployment citation rejection.
- [ ] Verify focused tests fail; validate against the exact source commit and append only visibly architect-inspected items.
- [ ] Run focused tests and `npm test`; review staged diff.
- [ ] Commit `feat(c4): verify pinned code supplements`; push.

### Task 8: Render reproducible JSON and Markdown/Mermaid

**Files:** Create `src/c4/render.mjs`, `test/c4-render.test.mjs`.

**Interfaces:** Produce `renderView(view) -> {json, markdown}`; byte-identical output for identical `ViewModel` input.

- [ ] Test deterministic bytes, four level labels, evidence/coverage tables, tentative legend, source revision and item links; compare graph and fallback citations.
- [ ] Verify focused tests fail; render stable sorted JSON and escaped Mermaid IDs/labels without relying on graph labels as IDs.
- [ ] Run focused tests and `npm test`; review staged diff.
- [ ] Commit `feat(c4): render traceable C4 views`; push.

### Task 9: Create assessments and qualitative feedback

**Files:** Create `src/c4/assessment.mjs`, `src/c4/feedback.mjs`, `test/c4-assessment.test.mjs`, `test/c4-feedback.test.mjs`.

**Interfaces:** Produce `newAssessment({ requestId, description, snapshot, registry })`, `validateAssessment(record, context)`, `renderAssessment(record, context)`, and `renderFeedback(record, assessment)`. Records stay editable JSON; the feedback file is separate.

- [ ] Test confirmed/rejected leads and reasons, likely affected element/path evidence, unresolved gaps, stale references, snapshot mismatch, reviewer/architect qualitative fields and absence of any approval status.
- [ ] Verify focused tests fail; implement validation and deterministic Markdown rendering.
- [ ] Run focused tests and `npm test`; review staged diff.
- [ ] Commit `feat(c4): create reviewable assessments and feedback`; push.

### Task 10: Wire the local CLI and prove the pilot flow

**Files:** Create `src/c4/cli.mjs`, `test/c4-cli.test.mjs`; modify `package.json` and `README.md` only to expose and describe commands.

**Interfaces:** `npm run c4 -- <command>` invokes the six documented commands with explicit `--catalog-commit`, `--registry-commit`, `--checkout id=path` and `--output` arguments where applicable.

- [ ] Write end-to-end tests from description to candidates, confirmed paths, four views, assessment rendering and feedback; include missing JointBooking graph and a new snapshot with a changed source commit.
- [ ] Verify focused tests fail; implement CLI argument validation, deterministic file names and write-on-success output behavior.
- [ ] Run focused tests and `npm test`; run `npm run build` and `npm run check` against clean configured source checkouts if the builder inputs changed; review outputs and staged diff.
- [ ] Commit `feat(c4): expose local impact assessment pilot`; push.

## Build and review boundary

The build PR contains the code, fixture data and authored `c4/*.json` registry files, with no generated `dist/` change. A later catalog sync remains separate. The real JointBooking pilot cannot show source-backed C4 paths until that source repository publishes its source graph at a catalog-pinned commit; this is an upstream adoption dependency, not a reason to invent graph content here.
