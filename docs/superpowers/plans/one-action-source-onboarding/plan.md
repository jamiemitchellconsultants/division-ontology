# One-action source onboarding implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task by task. Steps use checkbox (`- [ ]`) syntax for tracking. Read the detailed companion plan before each task.

**Goal:** Let a source repository owner join a division catalog by adding the repository to the division's read-only App, with the next normal sync importing it, publishing a per-source status, and merging a granted-source-only refresh without a steward.

**Architecture:** The existing `src/` catalog builder gains a granted-source path beside the strict `sources.json` path. A grant provider lists the source-read App's installation; an importer validates each granted repository's `.division-ontology.json` and ontology in isolation; a classifier turns outcomes plus the committed `dist/` into status rows and carried-forward stale versions; the builder and renderer publish them; the refresh classifier treats granted-only changes as automatic. The two sync workflows mint the source token from the source-read App.

**Tech Stack:** Node.js 20+, Git, the existing `n3` dependency, the Node test runner and `yaml` for workflow tests. No new dependency, service, database or model call.

**Spec:** `docs/features/one-action-source-onboarding/01-vision.md`, `02-behaviours.md`, `03-architecture.md`; ADRs 0009–0012. Exact signatures, schemas, fixtures and test files are in `docs/detailed-implementations/one-action-source-onboarding/plan.md`.

## Global constraints

- Do not edit `sources.json`, `division.json`, `core/`, `links/` or generated `dist/` in the build PR. The generated `dist/` change (new `origin`, `stale` and `importStatus` fields) arrives in the scheduled-sync PR after merge (AGENTS.md). The template keeps no registered or granted sources (B7).
- Granted-source failures never fail the build; registered-source failures still do, with today's messages (B15, ADR 0012). GitHub or network failures and incomplete listings always fail the run and leave `dist/` untouched; they are never read as removed access or invalid content (ADR 0009, ADR 0011).
- Status rows hold commits, states and coded reasons only: no timestamps, no raw Git or exception text, no credentials. Reasons come from `reason(code, detail)` (B10, B17).
- Link files stay steward-only: a link naming a granted source fails the build (U14, ADR 0012).
- Credentials go to Git only through `scripts/git-askpass.sh` and `GH_SOURCE_TOKEN`; never in files, URLs, arguments or logs.
- Keep `buildCatalog` output deterministic: sources, rows and concepts sorted by code point; carried-forward graphs keep the recorded graph IRI `urn:division-ontology:source:<id>:<commit>`.
- Each task is test first, ends in one focused commit and push, and requires a staged diff review (`git diff --cached`, `git diff --cached --check`). Run `npm test` after each task. Never stage `dist/`.

## File map and interfaces

| File | Responsibility |
| --- | --- |
| `src/import-status.mjs` | Reason codes, `reason`, `safeDetail`, `readPublishedState`, `classifyGrantedSources`. |
| `src/import-config.mjs` | `.division-ontology.json` path constant and `parseImportConfiguration`. |
| `src/errors.mjs` | `InfrastructureError`, the one error type that fails a run. |
| `src/granted-sources.mjs` | `importGrantedSources`: clone, read configuration and ontology, per-source outcome. |
| `src/grants.mjs` | `discoverGrants` and `githubGrantProvider`. |
| `src/rdf.mjs` (modify) | `readSourceOntology` and `parseTurtle` failures carry `code` and `detail`. |
| `src/catalog.mjs`, `src/output.mjs` (modify) | `origin`, `stale`, `importStatus`, status table. |
| `src/cli.mjs` (modify) | Orchestrates discovery, import, classification and build; `grantProvider` and `urlFor` options. |
| `src/refresh-policy.mjs`, `src/refresh-body.mjs`, `src/verify-refresh.mjs` (modify) | Registered-only source view, status in PR body, base `dist/` carried into the verifier. |
| `src/c4/snapshot.mjs` (modify) | `sourceInventory` accepts granted sources. |
| `.github/workflows/sync-catalog.yml`, `merge-policy.yml` (modify) | Source token from the source-read App; `DIVISION_SOURCE_APP` flag. |
| `docs/automation-setup.md`, `docs/Add-Repo-…-Three-Step-Instructions.md`, `README.md`, `GOVERNANCE.md`, `AGENTS.md` (modify) | Two-App setup and the single owner action. |
| `test/fixtures/onboarding/fixture-division.mjs` | `createFixtureDivision`: local Git sources, fake grant provider, run helpers. |
| `test/*.test.mjs` | One file per task, named in the detailed plan. |

Shared shapes: `Reason = {code, detail}`; `Discovery = {selection:'all'|'selected', repositories:[{id,defaultBranch,hasConfig}]}`; `Outcome = {id, attemptedCommit, result:{ok:true,namespace,quads}|{ok:false,reason}}`; `Published = Map<id,{commit,namespace,quads}>`; `Row = {source,status,attemptedCommit,publishedCommit,reason?}`; `GrantedSource = {id,namespace,commit,quads,origin:'granted',stale}`. Status values are exactly `current`, `invalid`, `not configured`, `access removed`.

## Review focus

1. A listing that errors, returns fewer repositories than `total_count`, or a provider that rejects must fail the run with `dist/` untouched, never publish `access removed`; Tasks 5 and 8 test it.
2. A granted namespace that is a prefix of, or prefixed by, the core's, a registered source's or another source's must be `namespace-conflict`, not a build-wide concept collision; Task 6 tests it.
3. A second build over unchanged inputs must write identical bytes, and the verifier must reproduce a carried-forward source from the base `dist/`; Tasks 8 and 10 test it.
4. Token-shaped text in a source's content must never reach a status reason; Task 1 tests it.
5. A `.division-ontology.json` that is a symlink, oversized or has unknown keys, and a granted repository later added to `sources.json`, must be handled without crashing; Tasks 2, 4 and 8 test them.

---

### Task 1: Reasons and sanitising

**Files:** Create `src/import-status.mjs` (first part), `test/import-status-reasons.test.mjs`.

**Interfaces:** Produce `REASON_CODES`, `STATUSES`, `safeDetail(value)->string`, `reason(code, detail?)->Reason`.

Commit: `feat: add coded import status reasons`

### Task 2: Import configuration contract

**Files:** Create `src/import-config.mjs`, `test/import-config.test.mjs`. Modify `src/sources.mjs` (export `safeRelativePath`).

**Interfaces:** Consume `reason`. Produce `IMPORT_CONFIG_PATH`, `IMPORT_CONFIG_MAX_BYTES`, `parseImportConfiguration(text)->{ok:true,config:{ontologyPath,namespace}}|{ok:false,reason}`.

Commit: `feat: parse source import configuration`

### Task 3: Fixture division

**Files:** Create `test/fixtures/onboarding/fixture-division.mjs`, `test/fixture-division.test.mjs`.

**Interfaces:** Produce `createFixtureDivision()`, `ontologyText`, `importConfig`, `coreNamespace` as specified in the detailed plan. Provider interface: `listInstallation()->Promise<{selection,repositories:[{id,defaultBranch}]}>`, `hasImportConfig(id, defaultBranch)->Promise<boolean>`.

Commit: `test: add fixture division for source onboarding`

### Task 4: Granted source importer

**Files:** Create `src/errors.mjs`, `src/granted-sources.mjs`, `test/granted-sources.test.mjs`. Modify `src/rdf.mjs` (error codes), `test/sources.test.mjs` only if an existing message test breaks (messages must not change).

**Interfaces:** Consume `parseImportConfiguration`, `reason`, `resolveSource`, `readSourceOntology`. Produce `InfrastructureError`, `importGrantedSources({repositories, published, urlFor?, checkoutOverrides?})->Outcome[]`.

Commit: `feat: import granted sources one at a time`

### Task 5: Grant discovery

**Files:** Create `src/grants.mjs`, `test/grants.test.mjs`.

**Interfaces:** Consume `InfrastructureError`. Produce `discoverGrants(provider, {registeredIds, excludeRepository?})->Promise<Discovery>`, `githubGrantProvider({token, fetchImpl?, apiBase?})->provider`.

Commit: `feat: discover repositories granted to the source-read App`

### Task 6: Status classification

**Files:** Modify `src/import-status.mjs`; create `test/import-status-classify.test.mjs`.

**Interfaces:** Consume `Discovery`, `Outcome[]`, `Published`. Produce `classifyGrantedSources({discovery, outcomes, published, registeredIds, reservedNamespaces})->{rows, sources}`.

Commit: `feat: classify granted source status and carry forward stale versions`

### Task 7: Catalog builder, renderer and published state

**Files:** Modify `src/catalog.mjs`, `src/output.mjs`, `src/import-status.mjs`; create `test/catalog-granted.test.mjs`.

**Interfaces:** `buildCatalog({coreQuads, sources, links, coreNamespace, importStatus?})` where sources may carry `origin` and `stale`; catalog sources gain `origin` and `stale`; catalog gains `importStatus`. Produce `readPublishedState(root)->Published`.

Commit: `feat: publish source origin and import status in the catalog`

### Task 8: Orchestrate in the CLI and prove the behaviours

**Files:** Modify `src/cli.mjs`; create `test/onboarding-behaviours.test.mjs`.

**Interfaces:** `runCli(argv, {root?, grantProvider?, urlFor?, env?})`; result gains `granted`.

Commit: `feat: import granted sources in the catalog build`

### Task 9: Granted refreshes classify as automatic

**Files:** Modify `src/refresh-policy.mjs`, `src/refresh-body.mjs`; create `test/refresh-granted.test.mjs`; extend `test/refresh-body.test.mjs`.

Commit: `feat: merge granted-source refreshes without a steward`

### Task 10: Verifier carries the base catalog

**Files:** Modify `src/verify-refresh.mjs`; create `test/verify-granted.test.mjs`.

**Interfaces:** `verifyRefresh({baseRoot, candidateDist, changedPaths, checkoutOverrides?, grantProvider?, urlFor?})`.

Commit: `fix: rebuild refreshes from the base catalog and live grants`

### Task 11: C4 and workspace readers accept granted sources

**Files:** Modify `src/c4/snapshot.mjs`; create `test/c4-snapshot-granted.test.mjs`.

**Interfaces:** Produce `sourceInventory(catalog, manifest)->Map<id,{id,url}>`.

Commit: `fix: let C4 snapshots load granted sources`

### Task 12: Workflows mint the source-read token

**Files:** Modify `.github/workflows/sync-catalog.yml`, `.github/workflows/merge-policy.yml`, `test/workflow.test.mjs`.

Commit: `ci: read sources with the source-read App`

### Task 13: Setup, onboarding guidance and governance

**Files:** Modify `docs/automation-setup.md`, `docs/Add-Repo-With-Internal-Ontology-To-Division-Ontology-Three-Step-Instructions.md`, `README.md`, `GOVERNANCE.md`, `AGENTS.md`, `test/init-division.test.mjs`; create `test/onboarding-docs.test.mjs`.

Commit: `docs: describe one-action source onboarding and two-App setup`
