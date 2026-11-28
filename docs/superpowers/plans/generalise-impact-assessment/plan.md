# Generalise impact assessment beyond C4: implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to carry out this plan one task at a time. Steps use checkbox (`- [ ]`) syntax for tracking. Before each task, read that task in the detailed companion plan for its increment. Stop at the end of every increment.

**Goal:** Assess any validated source graph, with or without C4 mappings. The assessment follows the meaning of each relationship from the master list, crosses sources only through reviewed links, and shows page evidence. Architects use it on the command line; analysts and readers use it in the signed-in workspace. Both paths use one .NET engine, so the Markdown they produce is byte-identical.

**Architecture:** Node checks and packages the inputs; .NET assesses and renders ([03-architecture.md](../../../features/generalise-impact-assessment/03-architecture.md), ADRs [0028](../../../adr/0028-run-one-dotnet-impact-engine.md)–[0034](../../../adr/0034-keep-assessment-drafts-in-browser-memory.md)).
- The existing Node loaders verify the checkouts, graphs, evidence and registries, then write an `impactInput` file into a bundle.
- The new `runtime/RuntimeKnowledge.Impact` library builds an immutable model from that bundle. It runs search, paths, views and assessments, and renders Markdown.
- `npm run c4` runs the library through `runtime/RuntimeKnowledge.Impact.Cli`.
- The workspace worker stores the model for each snapshot in PostgreSQL. The API serves it from memory to signed-in readers.
- Saved assessments are journal events that carry their frozen content and their Markdown.

**Tech stack:**
- Node 20 with `n3`, and the Node test runner.
- .NET 10, with the SDK pinned by `runtime/global.json`.
- xUnit and bUnit, and PostgreSQL with Apache AGE through `deploy/workspace/database/test-db.sh`.
- Playwright 1.63.0, plus `@axe-core/playwright` 4.10.2 (new, dev only).

**Spec:**
- [01-vision.md](../../../features/generalise-impact-assessment/01-vision.md)
- [02-behaviours.md](../../../features/generalise-impact-assessment/02-behaviours.md): B1–B3 and B5–B38 (B4 is retired), Q1–Q7, U1–U20
- [03-architecture.md](../../../features/generalise-impact-assessment/03-architecture.md)

Exact signatures, payloads, fixtures and test code are in the detailed plans under [`docs/detailed-implementations/generalise-impact-assessment/`](../../../detailed-implementations/generalise-impact-assessment/), one file per increment.

**Who runs the build:** opencode with the local model Qwen 3.6 27B (Q6), from the handover prompt [build-handover-opencode.md](../../../detailed-implementations/generalise-impact-assessment/build-handover-opencode.md). It runs one increment per session and stops at each boundary. The developer role (feature-dev, build step 3) then checks that every task has its commit, that every increment ended with a handoff, and that the tests pass.

## Increments

Each increment delivers its behaviours end to end. It leaves `npm test`, `dotnet test` and CI green, and the workspace deployable. Every behaviour id belongs to exactly one increment. Some behaviours have both a command-line part and a workspace part. Such a behaviour is placed in the increment that finishes it, and earlier increments build its command-line part as enabling work.

| Increment | Behaviours | Quality attributes to respect | Tasks | Detailed plan |
| --- | --- | --- | --- | --- |
| **I1. C4 parity on the .NET engine, command line** (walking skeleton) | B9, B14, B24, B37 | Q3, Q4 | 1–11 | [i1-c4-parity.md](../../../detailed-implementations/generalise-impact-assessment/i1-c4-parity.md) |
| **I2. Evidenced graphs and documents, command line** | B1, B2, B3, B5, B25, B26, B27, B38 | Q3, Q4 | 12–17 | [i2-evidenced-graphs.md](../../../detailed-implementations/generalise-impact-assessment/i2-evidenced-graphs.md) |
| **I3. Relationship meaning and crossings, command line** | B6, B7, B8, B10, B11, B12, B13 | Q3 | 18–21 | [i3-meaning-and-crossings.md](../../../detailed-implementations/generalise-impact-assessment/i3-meaning-and-crossings.md) |
| **I4. Workspace search and trace** | B15, B16, B28, B29 | Q1, Q2, Q6, Q7 | 22–27 | [i4-workspace-trace.md](../../../detailed-implementations/generalise-impact-assessment/i4-workspace-trace.md) |
| **I5. Workspace drafts, saves and sharing** | B17, B18, B19, B20, B22, B23, B30, B31, B32, B33, B34, B36 | Q2, Q3, Q4, Q5, Q6 | 28–33 | [i5-workspace-assessments.md](../../../detailed-implementations/generalise-impact-assessment/i5-workspace-assessments.md) |
| **I6. History and reuse** | B21, B35 | Q2, Q6 | 34–35 | [i6-history-and-reuse.md](../../../detailed-implementations/generalise-impact-assessment/i6-history-and-reuse.md) |
| **I7. Review amendments** (PR #220 review; behaviours amended in PR #228, architecture in PR #230) | B39 | Q1, Q3, Q4, Q5 | 36–42 | [i7-review-amendments.md](../../../detailed-implementations/generalise-impact-assessment/i7-review-amendments.md) |

Ordering notes:
- **I1 is the walking skeleton.** Sign-in, CI and the deployment baseline already exist. The skeleton proves the riskiest structural change first: the new engine, the bundle input and the CLI handover, all checked against a fixed baseline of today's outputs.
- **B5 and B27 are P2, but they are in I2.** They change the same view and evidence code as I2's P1 behaviours, and both appear in B37's list of allowed differences.
- **B22 is P2, but it is in I5.** B33 is P1 and refuses edits on another snapshot, so an edit has to exist first.
- **B30 and B32 have command-line parts that land in I2.** Their workspace parts land in I5, where both behaviours are finished.
- **B31's engine refusal already exists.** I5 adds the API and UI parts.

Every behaviour id appears once above: B1–B3, B5–B39, which is 38 behaviours.
- **I7 answers the PR #220 review.** It adds B39 and builds the refinements of B17, B30 and B34, which stay in I5, together with ADRs 0035 and 0036. It is planned in the build pull request, because it is review feedback on that pull request.

## Global constraints

- **The baseline is the arbiter of B37.** Task 1 captures the Node engine's outputs on every C4 fixture scenario, with Git dates and identities pinned so the outputs are deterministic. They go under `test/impact-corpus/`. Any later change to an expected file must be its own commit. That commit must name the item of B37's list that allows the change. No other expected-file change is allowed.
- **JavaScript-exact JSON.** Path ids, digests and every JSON output file are written by the `EcmaJson` writer in `RuntimeKnowledge.Impact`. It reproduces ECMAScript `JSON.stringify`: the same escaping, key order and two-space indentation. Values that are absent in JavaScript are omitted, never written as `null`.
- **One engine.** After Task 11, no impact rule exists in Node. Node keeps `git.mjs`, `graph.mjs`, `snapshot.mjs` and `registry.mjs` (verification), together with the bundle writers and the CLI front end (file safety).
- **CLI compatibility (B24).** `npm run c4` keeps its commands, flags, output file names, error messages and exit codes. The Node front end keeps today's output safety checks (outside repositories, no symlinks, never overwriting an input), and it copies the files the tool writes to a private staging directory.
- **How the command line runs the tool.** ADR 0028 has the front end run the tool through `dotnet run`. A test suite calls the command line dozens of times, so instead the front end builds the tool once, with `dotnet build -c Release -o .cache/impact-cli`, and runs the built dll for each call. Set `DIVISION_IMPACT_CLI` to use a prebuilt dll instead. This is an execution detail only: one tool, built on first use.
- **Cursors, never offsets.** Search pages and saved-assessment lists use opaque cursors, bound to the snapshot and the query (ADRs 0019 and 0020).
- **The spec moves with the code.** Every API change regenerates `runtime/openapi/workspace-api.json` with `dotnet build runtime/RuntimeKnowledge.Api`. Examples use only invented `example-` values, and never fixture page text (Q7).
- **No page text in public outputs (B29, Q7).** Excerpts live only in the impact model, in the API's authenticated routes and in saved assessments.
- **Ontology.** No domain term changes. `core/ontology.ttl` 0.5.1 already holds every term this feature uses. Event and table names are not domain terms.
- **Each task** is written test-first. It ends in one focused commit and push, after reviewing `git diff --cached`. At its boundary, all of these must be green:
  - `npm test`;
  - `dotnet test runtime/RuntimeKnowledge.Tests --filter "Category!=Database&Category!=Performance"`;
  - the database-backed suite, `bash deploy/workspace/database/test-db.sh run dotnet test runtime/RuntimeKnowledge.Tests --filter Category=Database`, from I4 on.

## File map

| Area | Files |
| --- | --- |
| Node verification and packaging | `src/c4/impact-input.mjs` (create), `src/c4/impact-bundle.mjs` (create), `src/c4/master-list.mjs` (create, I3), `src/workspace/bundle.mjs` (modify) |
| Node CLI front end | `src/c4/cli.mjs` (rewrite as front end), `src/c4/engine.mjs` (create: build and run the tool) |
| Removed Node engine | `src/c4/candidates.mjs`, `paths.mjs`, `project.mjs`, `render.mjs`, `assessment.mjs`, `feedback.mjs`, `code-evidence.mjs`, and their unit tests (Task 11) |
| Baseline | `test/impact-corpus/` (scenarios, expected outputs, exported bundles), `test/impact-corpus/capture.mjs`, `test/fixtures/deterministic-git.mjs`, `test/impact-corpus.test.mjs` |
| Engine | `runtime/RuntimeKnowledge.Impact/` (`Json/`, `Bundle/`, `Model/`, `Engine/`, `Render/`) |
| Engine command line | `runtime/RuntimeKnowledge.Impact.Cli/` |
| Contracts | `runtime/RuntimeKnowledge.Contracts/Impact.cs` (create) |
| Store and journal | `runtime/RuntimeKnowledge.Store/Sql/006_impact_models.sql`, `007_assessments.sql`, `ImpactModels.cs`, `Assessments.cs`, and changes to `Snapshots.cs`, `BundleReader.cs` and `Replay.cs` |
| API | `runtime/RuntimeKnowledge.Api/ImpactRoutes.cs`, `ImpactModelCache.cs`, and changes to `Program.cs`, `ProblemResponses.cs`, `WorkspaceOpenApi.cs`, `OpenApiExamples.cs` and `WorkspaceLinks.cs` |
| Web | `runtime/RuntimeKnowledge.Web/Pages/Impact.razor`, `Assessment.razor`, `Assessments.razor`, `Components/ImpactPathList.razor`, `EvidenceCitation.razor`, `AssessmentDraftEditor.razor`, `Services/AssessmentDraft.cs`, and changes to `WorkspaceClient.cs` and `WorkspaceLayout.razor` |
| Tests | `runtime/RuntimeKnowledge.Tests/Impact/*`, `ImpactApiTests.cs`, `AssessmentStoreTests.cs`, `ImpactUiTests.cs`, `ImpactPerformanceTests.cs`; Node `test/impact-*.test.mjs`; `deploy/workspace/verify-impact-browser.mjs` |
| CI | `.github/workflows/ci.yml` (install the .NET SDK in the Node test job) |

## Review focus

1. **A capture run that silently varies.** If Git commit hashes depend on the clock or on the machine's Git identity, the baseline is useless. Task 1 runs the capture twice and compares the bytes, and it pins dates, identities and `commit.gpgsign`.
2. **A step object whose key order differs from JavaScript's.** The path id changes even though the content matches. Task 6's tests assert path ids from the baseline, not only the step fields.
3. **Absent compared with `null`.** JavaScript omits undefined fields, such as `reason` on covered coverage entries and `assertionIri` on inspected items. Task 3 pins this in the serializer, and Task 9 compares every baseline file byte for byte.
4. **Page text added to steps changes path ids.** Page metadata appears only on exported-page evidence (Task 16). C4 fixtures have none, so their ids stay stable.
5. **A crossing that climbs from a `specializes` term.** Task 20 pins the `specialization-boundary` gap, and confirms that the path does not reach siblings through the shared term.
6. **A save that races a snapshot activation.** The snapshot check runs inside the write transaction, after `LockHeadAsync` (Task 29). A test activates between preview and save.
7. **Replay of assessment events.** The rebuild must copy the frozen body and the Markdown exactly, and the state digest must include them (Task 29).

---

## I1. C4 parity on the .NET engine, command line

### Task 1: Deterministic fixtures and the C4 baseline
**Files:** create `test/fixtures/deterministic-git.mjs`, `test/fixtures/c4/design-records.mjs` (moved helpers), `test/impact-corpus/capture.mjs`, `test/impact-corpus/scenarios.mjs`, `test/impact-corpus.test.mjs`, and `test/impact-corpus/<scenario>/expected/*`. Modify `test/c4-design-records.test.mjs` to import the moved helpers.
**Interfaces:** `pinGitEnvironment()`; `scenarios` (id, fixture, list of `{command, args}`); `captureScenario(id) → Map<file, bytes>`.
- [ ] Write `test/impact-corpus.test.mjs`. It runs every scenario through `runC4Cli` and compares each output file's bytes with `expected/`. It fails while `expected/` is empty.
- [ ] Implement pinning and capture. Run `node test/impact-corpus/capture.mjs` twice and confirm the outputs are byte-identical.
- [ ] Commit `test: capture the C4 impact baseline` and push.

### Task 2: Impact input in bundles
**Files:** create `src/c4/impact-input.mjs`, `src/c4/impact-bundle.mjs` and `test/impact-input.test.mjs`. Modify `src/workspace/bundle.mjs` and `test/workspace-bundle.test.mjs`. Create `test/impact-corpus/export-bundles.mjs`, and commit the `bundle/` folder for each scenario. Add a C# `BundleReaderTests` case accepting the new roles.
**Interfaces:**
- `buildImpactInput({snapshot, registry})` returns the `impactInput` object, schema 1.
- `writeImpactBundle({snapshot, registry, extraFiles}, dir)` returns `{id}`.
- Bundle role `impactInput` (`division-core`, `impact-input.json`).
- [ ] Write failing tests: input shape and ordering, absent fields omitted, digest-verified files, workspace bundle carrying the role.
- [ ] Implement, export the scenario bundles, and run `npm test`.
- [ ] Commit `feat: package verified impact input in bundles` and push.

### Task 3: Engine project and JavaScript-exact JSON
**Files:**
- create the `runtime/RuntimeKnowledge.Impact/RuntimeKnowledge.Impact.csproj` project, with `Json/EcmaJson.cs` and `Json/ImpactJson.cs`;
- create the `runtime/RuntimeKnowledge.Impact.Cli` project (an empty host);
- add both projects to `runtime/RuntimeKnowledge.slnx`, and reference them from the tests;
- create `runtime/RuntimeKnowledge.Tests/Impact/EcmaJsonTests.cs`.

**Interfaces:**
- `EcmaJson.Stringify(JsonNode?, bool indent)`;
- `EcmaJson.Canonical(JsonNode)`;
- `EcmaJson.Sha256Hex(string)`;
- `ImpactJson.ToNode<T>(T)`.
- [ ] Write the failing vectors: the escaping vectors, pretty and compact output, canonical digest, and the path id for a known step, all computed with Node.
- [ ] Implement the projects and the serializer.
- [ ] Commit `feat: add the impact engine project and JSON writer` and push.

### Task 4: Bundle reader and impact model
**Files:** create `Bundle/ImpactBundle.cs`, `Model/ImpactInput.cs`, `Model/ImpactModel.cs` and `Tests/Impact/ImpactModelTests.cs`.
**Interfaces:**
- `ImpactBundle.Open(string dir, string manifestId)`, and `ImpactBundle.FromObjects(manifestBytes, Func<string sha, byte[]>)`;
- `ImpactModel.Build(ImpactBundle)`;
- model lookups: `Source`, `Node`, `Outgoing`, `Evidence`, `Mapping`, `BindingFrom`, and `Blob(sourceId, path)`.
- [ ] Write failing tests over the pilot scenario's bundle: digest mismatch, missing role, schema version, node and assertion counts, and preserved order of registry gaps.
- [ ] Implement.
- [ ] Commit `feat: build the impact model from a bundle` and push.

### Task 5: Candidate search
**Files:** create `Engine/Coverage.cs`, `Engine/Candidates.cs` and `Tests/Impact/CandidatesTests.cs`. This is the port of `findCandidates`, `coverageGaps` and `coverageInventory`.
- [ ] Write failing tests: the C# versions of `test/c4-candidates.test.mjs`, plus a byte comparison with the baseline's `candidates.json` for each scenario.
- [ ] Implement.
- [ ] Commit `feat: port candidate search to the impact engine` and push.

### Task 6: Impact paths and bindings (B9, B14)
**Files:** create `Engine/Paths.cs` and `Tests/Impact/PathsTests.cs`.
- [ ] Write failing tests: the port of `test/c4-paths.test.mjs`; path ids equal to the baseline's; the binding step citing the registry commit and identity evidence (B14); and one `no-relationship-master-list` gap (B9).
- [ ] Implement.
- [ ] Update the expected `paths.json` files in a commit of their own, for B37 item 4. Commit the engine separately, as `feat: port impact paths with the no-master-list gap`, and push.

### Task 7: C4 views, code supplements and view Markdown
**Files:** create `Engine/Views.cs`, `Engine/CodeSupplement.cs`, `Render/ViewMarkdown.cs` and `Tests/Impact/ViewsTests.cs`.
- [ ] Write failing tests: the port of `c4-project`, `c4-render` and `c4-code-evidence`, plus byte comparison with the baseline's `view-*.json` and `view-*.md`.
- [ ] Implement.
- [ ] Commit `feat: port C4 views to the impact engine` and push.

### Task 8: Schema 1 assessments and feedback
**Files:** create `Engine/Assessments.cs`, `Render/AssessmentMarkdownV1.cs`, `Render/FeedbackMarkdown.cs` and `Tests/Impact/AssessmentsV1Tests.cs`.
- [ ] Write failing tests: the port of `c4-assessment` and `c4-feedback`, plus baseline bytes for `assessment.json`, `assessment.md` and `feedback.md`.
- [ ] Implement.
- [ ] Commit `feat: port schema 1 assessments and feedback` and push.

### Task 9: Engine command-line host and baseline conformance
**Files:** create `RuntimeKnowledge.Impact.Cli/Program.cs`, `Engine/CommandRunner.cs` and `Tests/Impact/ConformanceTests.cs`.
**Interfaces:** `impact-cli <command> --bundle <dir> --manifest <id> --staging <dir> [flags]`, which exits 0, or exits 1 with today's message on stderr. Also `CommandRunner.Run(args) → (exit, files, error)`.
- [ ] Write a failing conformance test that runs every scenario step in-process against its exported bundle, and compares every file with `expected/`.
- [ ] Implement.
- [ ] Commit `feat: add the impact engine command line` and push.

### Task 10: Node front end and CI
**Files:** rewrite `src/c4/cli.mjs` and create `src/c4/engine.mjs`. Modify `.gitignore`, `.github/workflows/ci.yml` and `package.json` (an `impact:build` script).
- [ ] Run `test/impact-corpus.test.mjs` and `test/c4-cli.test.mjs`. They pass on the Node engine.
- [ ] Switch the front end to the bundle-plus-tool path.
- [ ] Run both again, together with `test/acceptance/c4.test.mjs`. They must still pass.
- [ ] Commit `feat: run npm run c4 through the impact engine` and push.

### Task 11: Remove the Node engine
**Files:**
- delete the seven Node engine modules, and the unit tests that the C# ports replace;
- create `test/fixtures/c4/digest.mjs` (a test-only `viewDigest`);
- modify `test/c4-cli.test.mjs` and `test/representative-fixture.test.mjs`.
- [ ] Delete the files.
- [ ] Run `npm test` and the full `dotnet test`.
- [ ] Commit `refactor: remove the Node impact engine` and push. **I1 ends: stop.**

## I2. Evidenced graphs and documents, command line

### Task 12: Confluence-shaped fixture
**Files:** create `test/fixtures/impact/confluence-repos.mjs`. Add scenarios `confluence-search`, `confluence-paths` and `confluence-assessment`, with exported bundles. Their expected outputs are written by hand and reviewed.
- [ ] Write the fixture and its scenarios.
- [ ] Mark them `pending` in `test/impact-corpus.test.mjs` until Tasks 13–17.
- [ ] Commit `test: add the Confluence-shaped fixture` and push.

### Task 13: Evidence makes a lead supported (B1, B2)
- [ ] Write failing tests: a lead cited through an assertion is supported, with the reason `Verified evidence: selected-document`. Unmapped nodes with no evidence stay unsupported, and concepts are unchanged.
- [ ] Implement.
- [ ] Update the baseline in a commit of its own (B37 item 1), then commit the engine change.

### Task 14: Paths start from any supported lead (B3)
- [ ] Write failing tests: Confluence starts produce paths, and an unsupported start gives the new reason.
- [ ] Implement.
- [ ] Update the baseline in a commit of its own (B37 items 2 and 3), then commit the engine change.

### Task 15: C4 views are optional (B5)
- [ ] Write failing tests: an unmapped focus gives `no-c4-mapping`, and a focus mapped at the wrong level still gives `unsupported-focus`.
- [ ] Implement.
- [ ] Update the baseline in a commit of its own (B37 item 6), then commit the engine change.

### Task 16: Evidence excerpts and page metadata (B25, B26, B27, B38)
**Files:** create `Model/EvidenceText.cs`, `Model/FrontMatter.cs` and `Tests/Impact/EvidenceTextTests.cs`.
- [ ] Write failing tests: a page with a title and address; `page-metadata-missing` for each missing key and for an invalid URL; an ordinary document with no gap; `evidence-text-unavailable` for bytes that are not UTF-8; and path ids stable for code and ordinary evidence.
- [ ] Implement.
- [ ] Commit `feat: read cited text and page metadata` and push.

### Task 17: Schema 2 records, freezing and the assessment renderer (command-line parts of B30 and B32)
**Files:** create `Engine/Freezing.cs`, `Render/AssessmentMarkdown.cs` and `Tests/Impact/FreezingTests.cs`. Modify `Engine/Assessments.cs` and `CommandRunner.cs`.
- [ ] Write failing tests: `assessment-init` writes schema 2; a schema 2 record renders the pins, the page evidence and every gap section; a schema 1 record renders as today plus B30's sections; injected Markdown stays inert (Q4); and the digest is that of the canonical frozen assessment.
- [ ] Implement.
- [ ] Update the baseline in a commit of its own (B37 item 7), then commit the engine change. **I2 ends: stop.**

## I3. Relationship meaning and crossings, command line

### Task 18: Master list packaging (B10)
**Files:** create `src/c4/master-list.mjs` and `test/impact-master-list.test.mjs`. Modify `impact-input.mjs`, `impact-bundle.mjs` and `bundle.mjs`.
- [ ] Write failing tests: the list is read at the catalog commit, an invalid list fails packaging, it is included under the role `relationshipMasterList`, and the identity is the catalog commit plus the SHA-256.
- [ ] Implement.
- [ ] Commit `feat: package the relationship master list` and push.

### Task 19: Impact directions (B6, B7, B8, B10)
- [ ] Write failing tests: steps follow the four directions, counts are kept for `none`, there is one `unlisted-relationship` gap per IRI, and the result carries `masterList`. Existing C4 scenarios stay byte-stable when there is no list.
- [ ] Implement.
- [ ] Commit `feat: follow relationship impact directions` and push.

### Task 20: Reviewed-link crossings (B11, B12, B13)
- [ ] Write failing tests: `exact` is crossed both ways; `specializes` is crossed only from general to specific, with `specialization-boundary` otherwise; identical labels with no links never cross; and there is one `unlinked-term` gap for each source and term.
- [ ] Implement.
- [ ] Update the baseline in a commit of its own (B37 item 5) if any scenario denotes terms, then commit the engine change.

### Task 21: Command-line walkthrough for meaning and crossings
- [ ] Write a Node end-to-end test with a master list, links and two sources, run through `runC4Cli`.
- [ ] Commit `test: cover meaning and crossings end to end` and push. **I3 ends: stop.**

## I4. Workspace search and trace

### Task 22: Impact model storage
**Files:** create `Sql/006_impact_models.sql`, `ImpactModels.cs`, and `Impact/ModelSerializer.cs` in the engine.
- [ ] Write failing database tests: staging a bundle that has `impactInput` stores one model row, staging a bundle without it stores none, and a serialised model round-trips byte for byte.
- [ ] Implement.
- [ ] Commit `feat: store impact models with snapshots` and push.

### Task 23: Replay rebuilds models
- [ ] Write a failing replay test: a rebuilt generation has identical model rows.
- [ ] Implement in `Replay.cs`, through the shared staging.
- [ ] Commit `feat: rebuild impact models on replay` and push.

### Task 24: Impact read API (B15, B16, Q2)
**Files:** create `ImpactRoutes.cs`, `ImpactModelCache.cs` and `Contracts/Impact.cs`. Modify `ProblemResponses.cs`, `WorkspaceOpenApi.cs`, `OpenApiExamples.cs` and `WorkspaceLinks.cs`, and regenerate the spec.
- [ ] Write failing API tests:
  - search pages of 100 with a cursor, and no cursor on the last page;
  - `invalidCursor` and `staleCursor`;
  - paths and views;
  - `snapshotChanged` and `impactUnavailable`;
  - 401 when unauthenticated;
  - OpenAPI completeness.
- [ ] Implement.
- [ ] Commit `feat: serve impact search, paths and views` and push.

### Task 25: Evidence text in the workspace, and the publication guard (B28, B29, Q7)
- [ ] Write failing tests:
  - excerpts for code and ordinary-document evidence in API responses only;
  - a `dist/` build of the Confluence fixture contains no cited line;
  - unauthenticated responses and OpenAPI examples contain none.
- [ ] Implement.
- [ ] Commit `feat: show cited lines to signed-in readers only` and push.

### Task 26: Search and paths pages (B15, B16, Q6)
- [ ] Write failing bUnit tests: search with "Load more", tracing chosen leads, rendering paths step by step, evidence and gaps, and keyboard operation.
- [ ] Implement the `Impact` page and its components.
- [ ] Commit `feat: add workspace impact search and paths pages` and push.

### Task 27: Performance and accessibility checks (Q1, Q6)
- [ ] Write `ImpactPerformanceTests` (category `Performance`). It uses a generated bundle of 10,000 nodes and 50,000 assertions, measures the 95th percentile over 100 runs at the API, and records the time to load a cold model.
- [ ] Write `verify-impact-browser.mjs`, which runs axe through Playwright.
- [ ] If Q1 fails, raise pushback rather than cutting results short.
- [ ] Commit `test: measure impact search and paths, and check accessibility` and push. **I4 ends: stop.**

## I5. Workspace drafts, saves and sharing

### Task 28: Assessment storage
**Files:** create `Sql/007_assessments.sql`.
- [ ] Write a failing migration test.
- [ ] Implement the tables and grants.
- [ ] Commit `feat: add assessment tables` and push.

### Task 29: Assessment store and replay (B18, B20, B22, B31, B33, B34, Q5)
**Files:** create `Store/Assessments.cs`. Modify `Replay.cs`, including its digest.
- [ ] Write failing database tests:
  - a save is atomic;
  - get returns exactly what was saved;
  - an edit with `expectedVersion` succeeds, and a stale version gives `versionConflict`;
  - an edit on a different snapshot gives `snapshotChanged`;
  - approval fields are refused;
  - replay copies content byte for byte;
  - content survives a restart and an activation.
- [ ] Implement.
- [ ] Commit `feat: save and revise assessments in the journal` and push.

### Task 30: Assessment API (B17, B18, B19, B20, B22, B31, B32, B33, B36, Q2)
- [ ] Write failing API tests: preview validates and stores nothing; save and edit need the manage role; a reader's save gets 403; `snapshotChanged` on every route; and the Markdown route returns the stored bytes.
- [ ] Implement, and regenerate the spec.
- [ ] Commit `feat: serve assessment preview, save, read and edit` and push.

### Task 31: Draft, saved and edit pages (B17, B19, B20, B22, B31, B32, B34, B36, Q6)
- [ ] Write failing bUnit tests:
  - the draft lives in memory and is bound to its snapshot;
  - confirm, reject (a reason is required) and add impact (cited paths and evidence);
  - preview and export;
  - save appears only for managers;
  - the draft is discarded on `snapshotChanged`;
  - the saved view shows the pins and the "pinned to an earlier snapshot" marker;
  - no approve control exists.
- [ ] Implement.
- [ ] Commit `feat: add workspace assessment drafting and saved views` and push.

### Task 32: Byte identity and durability (B23, B30, Q3, Q5)
- [ ] Write failing tests: for the same bundle and record, the workspace export equals the command line's `assessment.md` byte for byte; a saved assessment opens identically after a restart, an activation and a rebuild.
- [ ] Commit `test: prove workspace and command-line Markdown identity` and push.

### Task 33: End-to-end workspace scenario and accessibility (Q6)
- [ ] Extend `ScenarioRunner` and `verify-impact-browser.mjs` to cover search, trace, draft, save, open as another reader and export.
- [ ] Commit `test: cover the workspace assessment flow end to end` and push. **I5 ends: stop.**

## I6. History and reuse

### Task 34: Saved assessment list (B21)
- [ ] Write failing tests: newest first, pages of 50, a cursor of (saved time, id), and a stable order on ties.
- [ ] Implement the route and the `Assessments` page.
- [ ] Commit `feat: list saved assessments` and push.

### Task 35: Start from an earlier assessment (B35)
- [ ] Write failing tests: the successor route needs the manage role; the new draft holds only the request id and description on the active snapshot, with `startedFrom`; the UI action appears only for managers.
- [ ] Implement.
- [ ] Commit `feat: start a new assessment from a saved one` and push. **I6 ends: stop**, then run the build phase's closing steps.

## I7. Review amendments

### Task 36: The record lists its traces (ADR 0035, B17)
- [ ] Write failing engine tests: a path from a depth 5 trace and one from an unconfirmed start validate when the record lists their traces; a record without traces keeps confirmed leads at depth 4; more than 5 traces (ADR 0035, amended in PR #231), more than 20 starts or a depth outside 1 to 6 is refused.
- [ ] Implement `Assessments.CitablePaths(model, record)` and use it in the API and the command line.
- [ ] Commit `feat: validate citations against the traces a record lists` and push.

### Task 37: The draft records its traces (ADR 0035)
- [ ] Write failing UI tests: starting a draft from a trace, and taking paths from a later trace, add each trace once to `traces`.
- [ ] Implement in `AssessmentDraft`.
- [ ] Commit `feat: record the traces a draft takes paths from` and push.

### Task 38: A reviewed-link crossing supports an impact (B39)
- [ ] Write failing tests: an impact on the node a crossing arrives at validates with no evidence IRI; one that cites no such path still needs evidence; the export and the editor show the crossing as its support.
- [ ] Implement in the engine, the renderer and the draft editor.
- [ ] Commit `feat: accept a reviewed-link crossing as an impact's support` and push.

### Task 39: Freeze every cited excerpt (B34)
- [ ] Write failing tests: code and ordinary-document lines are frozen and shown on the saved page; the export still prints page lines only.
- [ ] Implement in freezing. Recapture the corpus in its own commit if frozen digests change.
- [ ] Commit `feat: freeze every cited excerpt` and push.

### Task 40: Carry the search gaps of the record's leads (B30)
- [ ] Write failing tests: a page-metadata gap on a confirmed or rejected lead's page is frozen even when no cited path carries it.
- [ ] Implement in freezing.
- [ ] Commit `feat: carry the search gaps of an assessment's leads` and push.

### Task 41: Repair a missing impact model on import (ADR 0036)
- [ ] Write failing store tests: a snapshot staged without its model gains it when staged again and when activated; a present model is not rebuilt.
- [ ] Implement in staging and activation.
- [ ] Commit `fix: repair a missing impact model on import` and push.

### Task 42: Measure the worst-case preview (Q1)
- [ ] Add a performance test that previews a record listing 5 traces of 20 starts on the Q1 fixture, within API Gateway's 30 s, and record the result. A 20-trace cap measured 37.8 s and was lowered to 5 in PR #231.
- [ ] Commit `test: measure preview with five traces` and push. **I7 ends: stop**, then rerun the build phase's closing steps.

