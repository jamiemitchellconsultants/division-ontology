# I1. C4 parity on the .NET engine: detailed implementation plan

**Inputs:**
- [Behaviours](../../features/generalise-impact-assessment/02-behaviours.md): B9, B14, B24 and B37 are delivered here.
- [Architecture](../../features/generalise-impact-assessment/03-architecture.md).
- ADRs [0028](../../adr/0028-run-one-dotnet-impact-engine.md) and [0029](../../adr/0029-package-verified-impact-input-in-the-bundle.md).
- [Task overview](../../superpowers/plans/generalise-impact-assessment/plan.md).

**Where to work:**
- Use James's Mac, in Terminal, from the division-ontology checkout, on the branch `feature/generalise-impact-assessment/dev-build`, created from merged `main`.
- Install locked dependencies with `npm ci`.
- Use the SDK pinned by `runtime/global.json`.
- The JavaScript sources quoted below are the versions at `main` `c6c071c`. Port them before Task 11 deletes them.

**How every task runs:**
1. Write the test exactly as given.
2. Run the named command and watch it fail for the stated reason.
3. Implement the change.
4. Re-run the named command, then `npm test` and `dotnet test runtime/RuntimeKnowledge.Tests --filter "Category!=Database&Category!=Performance"`.
5. Stage only the listed paths, and review `git diff --cached`.
6. Commit with the given message, ending with the `Co-Authored-By` line the repository uses, and push.

## Conventions used in every engine task

- **Faithful port.** Each C# engine function translates the named JavaScript function statement by statement. Outputs are `System.Text.Json.Nodes` trees (`JsonObject` and `JsonArray`), built with their keys in the same order as the JavaScript object literals. A JavaScript property that can be `undefined` is added only when it has a value. A JavaScript `null` becomes a present key with a `null` value.
- **Ordering.** String comparisons use `string.CompareOrdinal`, which matches JavaScript's `<` on strings (UTF-16 code units). Sorts use LINQ `OrderBy`/`ThenBy`, which are stable as `Array.prototype.sort` is. Never use `List<T>.Sort`.
- **Keys.** JavaScript `key(x)` is `JSON.stringify([x.sourceId, x.nodeIri])`. Port it as `EcmaJson.Stringify(new JsonArray(sourceId, nodeIri))`.
- **Hashes.** `createHash('sha256').update(s).digest('hex')` becomes `EcmaJson.Sha256Hex(s)`: UTF-8 bytes, lowercase hex. The Git blob SHA-1 becomes `GitBlob.Sha1Hex(bytes)`.
- **Text decoding (port rule).** JavaScript reads Git files as UTF-8 strings, and then hashes `Buffer.from(content)`. Do the same: decode with `Encoding.UTF8.GetString`, which replaces invalid bytes, and re-encode before hashing. Never strip a BOM.
- **Errors.** Throw `ImpactException(message)` with the JavaScript message text exactly. The CLI prints `message` and exits 1.

---

## Task 1: Deterministic fixtures and the C4 baseline

**Files:**
- create `test/fixtures/deterministic-git.mjs`, `test/impact-corpus/scenarios.mjs`, `test/impact-corpus/capture.mjs`, `test/impact-corpus/README.md` and `test/impact-corpus.test.mjs`;
- generate `test/impact-corpus/<scenario>/steps.json`, `inputs/` and `expected/`;
- modify `test/acceptance/c4-walkthrough.mjs`, so that `generateWalkthrough(root, { run = runC4Cli } = {})` uses `run` for every CLI call.

**Why:** B37 needs today's outputs as fixed bytes. Fixture commit hashes depend on Git dates and identity, so these are pinned for every Git process started by the capture and the conformance test.

### 1.1 `test/fixtures/deterministic-git.mjs`

```js
// Pins every Git commit made in this process, so fixture commit hashes, and every
// output that cites them, are the same on every machine and every run.
export function pinGitEnvironment() {
  Object.assign(process.env, {
    GIT_AUTHOR_NAME: 'Fixture', GIT_AUTHOR_EMAIL: 'fixture@example.test',
    GIT_COMMITTER_NAME: 'Fixture', GIT_COMMITTER_EMAIL: 'fixture@example.test',
    GIT_AUTHOR_DATE: '2026-01-01T00:00:00Z', GIT_COMMITTER_DATE: '2026-01-01T00:00:00Z',
    GIT_CONFIG_NOSYSTEM: '1', GIT_CONFIG_GLOBAL: '/dev/null',
    GIT_CONFIG_COUNT: '2',
    GIT_CONFIG_KEY_0: 'commit.gpgsign', GIT_CONFIG_VALUE_0: 'false',
    GIT_CONFIG_KEY_1: 'init.defaultBranch', GIT_CONFIG_VALUE_1: 'main'
  });
}
```

### 1.2 `test/impact-corpus/scenarios.mjs`

Each scenario builds its fixture and calls `run(argv, { cwd })` for each step. `run` is supplied by the caller: the capture uses a recorder, and the conformance test uses the real `runC4Cli`. Scenarios write their input files into the fixture's temporary folders, exactly as the walkthrough does. The recorder copies each input into the corpus and replaces absolute paths with tokens (see 1.3).

```js
import { writeFileSync, readFileSync } from 'node:fs';
import { join } from 'node:path';
import { withPilotFixture } from '../fixtures/c4/fixture-repos.mjs';
import { generateWalkthrough } from '../acceptance/c4-walkthrough.mjs';

const common = f => ['--catalog-commit', f.catalogCommit, '--registry-commit', f.registryCommit,
  ...[...f.checkouts].flatMap(([id, path]) => ['--checkout', `${id}=${path}`])];
const at = key => `${key.sourceId}=${key.nodeIri}`;

export const scenarios = {
  async pilot(run) {
    await withPilotFixture(async f => {
      const c = [...common(f), '--output', f.outputDir], o = { cwd: f.divisionRoot };
      const temp = name => join(f.divisionRoot, '..', name);
      await run(['search', ...c, '--query', 'Alpha API'], o);                                    // 0
      await run(['search', ...c, '--exact-iri', f.keys.alphaSystem.nodeIri], o);                // 1
      await run(['search', ...c, '--query', 'unmatched phrase'], o);                             // 2
      await run(['paths', ...c, '--start', at(f.keys.alphaSystem)], o);                          // 3
      const traced = JSON.parse(readFileSync(join(f.outputDir, 'paths.json'), 'utf8'));
      await run(['paths', ...c, '--start', at(f.keys.alphaSystem), '--max-depth', '1'], o);      // 4
      await run(['paths', ...c, '--start', at(f.keys.betaSystem)], o);                           // 5
      for (const [level, focus] of [['context', f.keys.alphaSystem], ['container', f.keys.alphaSystem],
        ['component', f.keys.alphaContainer], ['code', f.keys.alphaComponent], ['container', f.keys.betaSystem]])
        await run(['view', ...c, '--level', level, '--focus', at(focus)], o);                    // 6-10
      const supplement = temp('supplement.json');
      writeFileSync(supplement, JSON.stringify(f.codeSupplement()));
      await run(['view', ...c, '--level', 'code', '--focus', at(f.keys.alphaComponent), '--supplement', supplement], o); // 11
      const description = temp('description.txt');
      writeFileSync(description, 'Change Alpha API\n');
      await run(['assessment-init', ...c, '--request-id', 'CR-1', '--description-file', description], o);              // 12
      const record = JSON.parse(readFileSync(join(f.outputDir, 'assessment.json'), 'utf8'));
      const path = traced.paths.find(p => p.steps.length === 2);
      Object.assign(record, { architect: 'Architect A', confirmedLeads: [f.keys.alphaSystem],
        rejectedLeads: [{ key: f.keys.externalBeta, reason: 'Out | <b>scope</b>\nnext' }],
        likelyImpacts: [{ key: path.steps[1].object, reason: 'Call path', pathIds: [path.id],
          evidenceIris: [path.steps[0].evidenceIris[0]] }],
        unresolved: [{ code: 'partial-code', reason: 'No graph code coverage' }] });
      const input = temp('assessment-input.json');
      writeFileSync(input, JSON.stringify(record));
      await run(['assessment-render', ...c, '--input', input], o);                               // 13
      const digest = readFileSync(join(f.outputDir, 'assessment.md'), 'utf8').match(/Assessment digest: ([a-f0-9]{64})/)[1];
      const feedback = temp('feedback-input.json');
      writeFileSync(feedback, JSON.stringify({ schemaVersion: 1, assessmentDigest: digest,
        architect: 'Architect A', reviewer: 'Reviewer B', perceivedAccuracy: 'mostly accurate',
        perceivedCompleteness: 'partial', confidence: 'medium', usefulDiscoveries: ['Beta dependency'],
        suspectedMisses: ['data store'], falseLeads: [], examples: ['CR-1 path'], openQuestions: ['Who owns it?'] }));
      await run(['feedback-render', ...c, '--input', feedback, '--assessment-file', input], o);  // 14
    });
  },
  async walkthrough(run, root) {
    await generateWalkthrough(join(root, 'walkthrough'), { run });
  }
};
```

`withPilotFixture` puts `division`, `alpha`, `beta` and `output` under one temporary root, so `join(f.divisionRoot, '..', name)` lies outside every repository and outside the output directory.

### 1.3 `test/impact-corpus/capture.mjs`

The recorder never clears an output directory, because the walkthrough keeps its own input files there. It reads only the files that the command is known to write:

| Command | Files |
| --- | --- |
| `search` | `candidates.json` |
| `paths` | `paths.json` |
| `view` | `view-<level>.json`, `view-<level>.md` |
| `assessment-init` | `assessment.json` |
| `assessment-render` | `assessment.md` |
| `feedback-render` | `feedback.md` |

It copies every input file the step names (`--description-file`, `--input`, `--assessment-file`, `--view-input` and `--supplement`) to `inputs/<step>/<basename>`, where `<step>` is the zero-padded step index. It writes argv with these tokens:

| Token | Replaces |
| --- | --- |
| `$output` | the value after `--output` |
| `$checkout:<id>` | the path in each `--checkout <id>=<path>` |
| `$input/<step>/<basename>` | each copied input |

```js
import { mkdtempSync, rmSync, readFileSync, writeFileSync, mkdirSync, copyFileSync, existsSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve, basename } from 'node:path';
import { fileURLToPath } from 'node:url';
import { pinGitEnvironment } from '../fixtures/deterministic-git.mjs';
import { scenarios } from './scenarios.mjs';
import { runC4Cli } from '../../src/c4/cli.mjs';

const corpus = fileURLToPath(new URL('.', import.meta.url));
const inputFlags = new Set(['--description-file', '--input', '--assessment-file', '--view-input', '--supplement']);
const pad = n => String(n).padStart(3, '0');

export function outputsFor(argv) {
  const level = argv[argv.indexOf('--level') + 1];
  return { search: ['candidates.json'], paths: ['paths.json'], view: [`view-${level}.json`, `view-${level}.md`],
    'assessment-init': ['assessment.json'], 'assessment-render': ['assessment.md'], 'feedback-render': ['feedback.md'] }[argv[0]] ?? [];
}

export async function recordScenario(id, { onStep, copyInputs = true } = {}) {
  pinGitEnvironment();
  const root = mkdtempSync(join(tmpdir(), 'impact-corpus-'));
  const steps = [];
  const run = async (argv, options) => {
    const index = steps.length, step = { argv: [], stderr: '' };
    const output = argv[argv.indexOf('--output') + 1];
    step.argv = argv.map((value, i) => {
      const flag = argv[i - 1];
      if (flag === '--output') return '$output';
      if (flag === '--checkout') return `$checkout:${value.slice(0, value.indexOf('='))}`;
      if (inputFlags.has(flag)) {
        const target = join(corpus, id, 'inputs', pad(index), basename(value));
        if (copyInputs) { mkdirSync(join(target, '..'), { recursive: true }); copyFileSync(value, target); }
        return `$input/${pad(index)}/${basename(value)}`;
      }
      return value;
    });
    step.exit = await runC4Cli(argv, { ...options, stdout: null, stderr: { write: s => { step.stderr += s; } } });
    step.files = step.exit === 0 ? outputsFor(argv).filter(name => existsSync(join(output, name))).sort() : [];
    step.outputs = Object.fromEntries(step.files.map(name => [name, readFileSync(join(output, name))]));
    if (onStep) await onStep(step, argv, options);
    steps.push(step);
    return step.exit;
  };
  try { await scenarios[id](run, root); } finally { rmSync(root, { recursive: true, force: true }); }
  return steps;
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  for (const id of process.argv.slice(2).length ? process.argv.slice(2) : Object.keys(scenarios)) {
    rmSync(join(corpus, id, 'inputs'), { recursive: true, force: true });
    const steps = await recordScenario(id);
    const dir = join(corpus, id);
    rmSync(join(dir, 'expected'), { recursive: true, force: true });
    steps.forEach((step, index) => {
      const target = join(dir, 'expected', pad(index));
      mkdirSync(target, { recursive: true });
      for (const [name, bytes] of Object.entries(step.outputs)) writeFileSync(join(target, name), bytes);
    });
    const previous = existsSync(join(dir, 'steps.json')) ? JSON.parse(readFileSync(join(dir, 'steps.json'), 'utf8')) : [];
    writeFileSync(join(dir, 'steps.json'), JSON.stringify(steps.map(({ argv, exit, stderr, files }, index) =>
      ({ argv, exit, stderr, files, ...(previous[index]?.manifest ? { manifest: previous[index].manifest } : {}) })), null, 2) + '\n');
    console.log(`${id}: ${steps.length} steps`);
  }
}
```

Re-capturing keeps each step's `manifest`, which Task 2 adds. Re-export the bundles whenever a fixture changes.

### 1.4 `test/impact-corpus.test.mjs`

```js
import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { recordScenario } from './impact-corpus/capture.mjs';
import { scenarios } from './impact-corpus/scenarios.mjs';

const corpus = fileURLToPath(new URL('./impact-corpus/', import.meta.url));
// Scenarios added in I2 and I3 are listed here until their behaviour lands.
const pending = new Set();

for (const id of Object.keys(scenarios)) {
  test(`B37 baseline: ${id} matches its expected bytes`, { skip: pending.has(id) }, async () => {
    const expected = JSON.parse(readFileSync(join(corpus, id, 'steps.json'), 'utf8'));
    const steps = await recordScenario(id, { copyInputs: false });
    assert.equal(steps.length, expected.length, 'step count');
    steps.forEach((step, index) => {
      const want = expected[index], dir = join(corpus, id, 'expected', String(index).padStart(3, '0'));
      assert.deepEqual(step.argv, want.argv, `step ${index} argv`);
      assert.equal(step.exit, want.exit, `step ${index} exit`);
      assert.equal(step.stderr, want.stderr, `step ${index} stderr`);
      assert.deepEqual(step.files, want.files, `step ${index} files`);
      for (const name of want.files)
        assert.ok(step.outputs[name].equals(readFileSync(join(dir, name))), `step ${index} ${name} differs from the baseline`);
    });
  });
}
```

The test passes `copyInputs: false`, so it never writes into the corpus.

### 1.5 Steps

- [ ] Write 1.1–1.4. Change `generateWalkthrough(root)` to `generateWalkthrough(root, { run = runC4Cli } = {})`, and pass `run` into its `c4` helper and both direct `runC4Cli` calls.
- [ ] Run `node --test test/impact-corpus.test.mjs`. It fails with ENOENT on `steps.json`.
- [ ] Run `node test/impact-corpus/capture.mjs` and `git add test/impact-corpus`. Then run the capture again. `git status --porcelain test/impact-corpus` must be empty, which proves the capture is deterministic.
- [ ] Read `test/impact-corpus/pilot/steps.json`. Step 005, `paths` from the unmapped Beta system, must have exit 0 and an `unsupported-start` gap in `expected/005/paths.json`. Step 010, the unmapped Beta container view, must have `unsupported-focus`.
- [ ] Write `test/impact-corpus/README.md`, in four short paragraphs. It says the corpus is the B37 baseline, captured from the Node engine at `c6c071c`. It says how to replay it. It says that any later change to `expected/` is a commit of its own, naming the B37 list item that allows it. And it lists the tokens.
- [ ] Run `node --test test/impact-corpus.test.mjs`, then `npm test`.
- [ ] Commit `test: capture the C4 impact baseline (#198)` and push.

---

## Task 2: Impact input in bundles

**Files:**
- create `src/c4/impact-input.mjs`, `src/c4/impact-bundle.mjs`, `test/impact-input.test.mjs` and `test/impact-corpus/export-bundles.mjs`;
- generate `test/impact-corpus/objects/` (`manifests/*.json`, `blobs/*`) and add a `manifest` field to each step in `steps.json`;
- modify `src/workspace/bundle.mjs`, `test/workspace-bundle.test.mjs` and `runtime/RuntimeKnowledge.Tests/BundleGraphSchemaTests.cs` (one new case).

### 2.1 The `impactInput` contract (schema 1)

Write `JSON.stringify(input) + '\n'` as UTF-8. **Do not** sort keys with `ordered()`. Key order is part of the contract, because steps copy evidence objects and `identityEvidence` verbatim into path ids.

```text
{
  schemaVersion: 1,
  snapshotId: snapshot.id,                       // {catalogCommit, catalogSha256, sources:[{id, commit}]}
  sources: [                                     // snapshot.sources in Map order (sorted by id)
    { id, commit,
      evidenceSelectors?: config.sourceGraph.evidence,          // absent when the config has none
      graph: {
        status,                                  // 'available' | 'unavailable'
        reason?,                                 // unavailable only
        schemaVersion?,                          // available only
        nodes: [...graphState.nodes.values()],            // loader objects, Map order
        assertions: [...graphState.assertions.values()],
        evidence: [...graphState.evidence.values()],
        coverage: graphState.coverage } } ],
  catalog: { concepts: (snapshot.catalog.concepts ?? []).map(({iri, source, label, definition}) => ...) },
  reviewedLinks: (snapshot.catalog.links ?? []).map(({source, local, shared, relation}) => ...),
  registry: registry && { commit, mappingSha256, bindingSha256, mappings, bindings, gaps }
}
```

`reviewedLinks` is carried from I1 onwards even though I3 is the first increment to use it. It is part of schema 1. In each concept, `definition` is present only when it is defined (`undefined` is omitted, never written as `null`).

### 2.2 `src/c4/impact-input.mjs`

```js
// Packages what the Node loaders verified for the .NET impact engine (ADR 0029).
// Key order is a contract: do not sort keys here.
export function buildImpactInput({ snapshot, registry }) {
  const sources = [...snapshot.sources.values()].map(({ id, commit, config, graphState: g }) => ({
    id, commit,
    ...(config?.sourceGraph?.evidence ? { evidenceSelectors: config.sourceGraph.evidence } : {}),
    graph: {
      status: g.status,
      ...(g.status !== 'available' ? { reason: g.reason } : { schemaVersion: g.schemaVersion }),
      nodes: [...g.nodes.values()], assertions: [...g.assertions.values()],
      evidence: [...g.evidence.values()], coverage: g.coverage
    }
  }));
  return {
    schemaVersion: 1, snapshotId: snapshot.id, sources,
    catalog: { concepts: (snapshot.catalog.concepts ?? []).map(({ iri, source, label, definition }) =>
      definition === undefined ? { iri, source, label } : { iri, source, label, definition }) },
    reviewedLinks: (snapshot.catalog.links ?? []).map(({ source, local, shared, relation }) => ({ source, local, shared, relation })),
    registry: registry ? { commit: registry.commit, mappingSha256: registry.mappingSha256,
      bindingSha256: registry.bindingSha256, mappings: registry.mappings, bindings: registry.bindings, gaps: registry.gaps } : null
  };
}
export const impactInputBytes = input => Buffer.from(JSON.stringify(input) + '\n', 'utf8');
```

### 2.3 `src/c4/impact-bundle.mjs`

This writes a bundle in the same layout as `writeBundle`. It holds:
- the manifest;
- `impactInput`;
- each evidence file (role `graphEvidence`) and each identity evidence file;
- the `extraFiles` (role `inspectedSource`), each `{ sourceId, path }`, read at that source's pinned commit. A file that is missing at the commit is skipped, so the engine reports `source path missing` as today.

The manifest uses the workspace manifest's schema and fields, `{ schemaVersion: 1, divisionCommit, catalogSha256, sources, registryCommit, files }`. The `sources` are `[{id, commit, graphSchemaVersion, graphStatus}]`. It is written with `jsonBytes`, which sorts keys, exactly as `bundle.mjs` does. Reuse the helpers from `src/workspace/bundle.mjs`: export `pinnedBytes`, `jsonBytes`, `hash` and the `include` pattern from there, rather than copying them.

```js
export function buildImpactBundle({ snapshot, registry, catalogBytes, extraFiles = [] })   // -> { id, manifest, manifestBytes, blobs }
export function writeImpactBundle(bundle, outputDir)                                       // same checks as writeBundle
```

`catalogBytes` is `pinnedBytes(divisionRoot, catalogCommit, 'dist/catalog.json')`. Its only use is the manifest's `catalogSha256`.

In `src/workspace/bundle.mjs`, `buildBundle` now also includes `include('impactInput', 'division-core', 'impact-input.json', impactInputBytes(buildImpactInput({ snapshot, registry })))`.

### 2.4 `test/impact-input.test.mjs`

```js
import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, readFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { createHash } from 'node:crypto';
import { withPilotFixture } from './fixtures/c4/fixture-repos.mjs';
import { loadSnapshot } from '../src/c4/snapshot.mjs';
import { loadRegistries } from '../src/c4/registry.mjs';
import { buildImpactInput, impactInputBytes } from '../src/c4/impact-input.mjs';
import { buildImpactBundle, writeImpactBundle } from '../src/c4/impact-bundle.mjs';
import { readAtCommit } from '../src/c4/git.mjs';

const load = f => { const snapshot = loadSnapshot(f); return { snapshot, registry: loadRegistries({ divisionRoot: f.divisionRoot, registryCommit: f.registryCommit, snapshot }) }; };

test('impact input keeps loader order and omits undefined fields', () => withPilotFixture(f => {
  const { snapshot, registry } = load(f), input = buildImpactInput({ snapshot, registry });
  assert.equal(input.schemaVersion, 1);
  assert.deepEqual(input.snapshotId, snapshot.id);
  assert.deepEqual(input.sources.map(s => s.id), ['fixture/Alpha', 'fixture/Beta']);
  const alpha = input.sources[0];
  assert.deepEqual(Object.keys(alpha), ['id', 'commit', 'evidenceSelectors', 'graph']);
  assert.deepEqual(Object.keys(alpha.graph), ['status', 'schemaVersion', 'nodes', 'assertions', 'evidence', 'coverage']);
  assert.deepEqual(Object.keys(alpha.graph.evidence[0]), ['iri', 'path', 'startLine', 'endLine', 'blobId', 'role']);
  assert.equal('reason' in alpha.graph.coverage[0], false);
  assert.deepEqual(Object.keys(input.registry.bindings[0].identityEvidence), ['sourceId', 'path', 'startLine', 'endLine', 'blobId', 'reason']);
  assert.match(impactInputBytes(input).toString('utf8'), /\n$/);
}));

test('unavailable graphs carry their reason and no schema version', () => withPilotFixture(f => {
  f.writeSource('fixture/Alpha', 'ontology.config.json', JSON.stringify({ sourceGraph: { enabled: false } }));
  const pin = f.commitSource('fixture/Alpha', 'disable');
  const catalog = JSON.parse(f.readDivision('dist/catalog.json')); catalog.sources[0].commit = pin;
  f.writeDivision('dist/catalog.json', JSON.stringify(catalog));
  const catalogCommit = f.commitDivision('pin');
  const snapshot = loadSnapshot({ ...f, catalogCommit });
  const alpha = buildImpactInput({ snapshot, registry: null }).sources[0];
  assert.deepEqual(alpha.graph, { status: 'unavailable', reason: 'Source graph not enabled', nodes: [], assertions: [], evidence: [], coverage: [] });
}));

test('impact bundles are digest-verified and include evidence and inspected files', () => withPilotFixture(f => {
  const { snapshot, registry } = load(f), dir = mkdtempSync(join(tmpdir(), 'impact-bundle-'));
  try {
    const bundle = buildImpactBundle({ snapshot, registry,
      catalogBytes: Buffer.from(readAtCommit(f.divisionRoot, f.catalogCommit, 'dist/catalog.json')),
      extraFiles: [{ sourceId: 'fixture/Alpha', path: 'src/App.cs' }, { sourceId: 'fixture/Alpha', path: 'missing.txt' }] });
    writeImpactBundle(bundle, join(dir, 'b'));
    const manifest = JSON.parse(readFileSync(join(dir, 'b', 'manifests', bundle.id + '.json')));
    const roles = manifest.files.map(x => `${x.role}:${x.path}`);
    assert.ok(roles.includes('impactInput:impact-input.json'));
    assert.ok(roles.includes('graphEvidence:src/app.ts'));
    assert.ok(roles.includes('inspectedSource:src/App.cs'));
    assert.ok(!roles.some(r => r.endsWith('missing.txt')));
    for (const file of manifest.files)
      assert.equal(createHash('sha256').update(readFileSync(join(dir, 'b', 'blobs', file.sha256))).digest('hex'), file.sha256);
  } finally { rmSync(dir, { recursive: true, force: true }); }
}));
```

Add this case to `test/workspace-bundle.test.mjs`: the built bundle's manifest has exactly one `impactInput` file for `division-core`. Its bytes parse, with `schemaVersion` 1. Its `registry.commit` equals the `registryCommit` given, or it is `null` when none was given.

### 2.5 `test/impact-corpus/export-bundles.mjs`

For each scenario, re-run `recordScenario(id, { onStep, copyInputs: false })`. In `onStep`, for every step that exited 0 and whose command is one of the six engine commands:
1. load the snapshot and registry from the step's real arguments, as the CLI does;
2. collect the `extraFiles`: the `--supplement` file's `{sourceId, path}`, and every view input item with `basis: 'architect-inspected'`;
3. build the impact bundle and write its objects into `test/impact-corpus/objects/`, merging by digest;
4. set `step.manifest = bundle.id` in that scenario's `steps.json`.

Steps that failed before the engine ran get no bundle. The export must leave `expected/` untouched. It asserts that each step's outputs still equal `expected/`.

### 2.6 C#: `BundleReader` accepts the new roles

Add `[Fact] public async Task Bundle_with_impact_input_role_is_read()` to `BundleGraphSchemaTests.cs`. Using the file's existing helpers, add a file with role `impactInput`, `sourceId` `division-core`, path `impact-input.json`, to a valid in-memory bundle. Then assert that `ReadAsync` succeeds. `ValidateManifest` already accepts unknown roles, so this test passes at once and pins the behaviour.

### 2.7 Steps

- [ ] Write the tests in 2.4 and 2.6. Run `node --test test/impact-input.test.mjs`: it fails, because the module is missing.
- [ ] Implement 2.2, 2.3 and the change to `bundle.mjs`. Run the test, and `node --test test/workspace-bundle.test.mjs`.
- [ ] Write and run `node test/impact-corpus/export-bundles.mjs`. Check that `objects/` exists and that every engine step in `steps.json` has a `manifest`.
- [ ] Run `npm test` and `dotnet test runtime/RuntimeKnowledge.Tests --filter "FullyQualifiedName~BundleGraphSchema"`.
- [ ] Commit `feat: package verified impact input in bundles (#198)` and push.

---

## Task 3: Engine project and JavaScript-exact JSON

**Files:**
- create `runtime/RuntimeKnowledge.Impact/RuntimeKnowledge.Impact.csproj`, with `Json/EcmaJson.cs`, `Json/GitBlob.cs` and `ImpactException.cs`;
- create `runtime/RuntimeKnowledge.Impact.Cli/RuntimeKnowledge.Impact.Cli.csproj`, with a `Program.cs` that returns 2 and prints `not implemented`;
- create `runtime/RuntimeKnowledge.Tests/Impact/EcmaJsonTests.cs`;
- modify `runtime/RuntimeKnowledge.slnx` and `runtime/RuntimeKnowledge.Tests/RuntimeKnowledge.Tests.csproj`, adding a `ProjectReference` to the Impact project.

### 3.1 Projects

`RuntimeKnowledge.Impact.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>
  </PropertyGroup>
  <ItemGroup>
    <InternalsVisibleTo Include="RuntimeKnowledge.Tests" />
  </ItemGroup>
</Project>
```

`RuntimeKnowledge.Impact.Cli.csproj` has `OutputType` `Exe`, the same properties, `AssemblyName` `RuntimeKnowledge.Impact.Cli`, and a `ProjectReference` to the Impact project. Add both projects to `RuntimeKnowledge.slnx`. Run `dotnet restore runtime/RuntimeKnowledge.slnx` to create the `packages.lock.json` files, and commit them.

### 3.2 `Json/EcmaJson.cs` (complete)

```csharp
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace RuntimeKnowledge.Impact.Json;

/// <summary>Writes JSON exactly as ECMAScript JSON.stringify does, so ids and digests match the Node pilot.</summary>
public static class EcmaJson
{
    public static string Stringify(JsonNode? node, bool indent = false)
    {
        var builder = new StringBuilder();
        Write(builder, node, indent, 0);
        return builder.ToString();
    }

    /// <summary>JSON.stringify(value, null, 2) + "\n", as the CLI writes its files.</summary>
    public static string File(JsonNode? node) => Stringify(node, indent: true) + "\n";

    public static string Sha256Hex(string text) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)));

    /// <summary>Port of canonical(): sorts object keys recursively by UTF-16 code unit order.</summary>
    public static JsonNode? Canonical(JsonNode? node) => node switch
    {
        JsonObject o => new JsonObject(o.OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => KeyValuePair.Create(p.Key, Canonical(p.Value)))),
        JsonArray a => new JsonArray(a.Select(Canonical).ToArray()),
        null => null,
        _ => node.DeepClone()
    };

    /// <summary>sha256(JSON.stringify(canonical(value)) + '\n').</summary>
    public static string CanonicalDigest(JsonNode? node) => Sha256Hex(Stringify(Canonical(node)) + "\n");

    private static void Write(StringBuilder b, JsonNode? node, bool indent, int depth)
    {
        switch (node)
        {
            case null: b.Append("null"); return;
            case JsonObject o:
                if (o.Count == 0) { b.Append("{}"); return; }
                b.Append('{');
                var first = true;
                foreach (var (key, value) in o)
                {
                    if (!first) b.Append(',');
                    first = false;
                    Break(b, indent, depth + 1);
                    WriteString(b, key);
                    b.Append(indent ? ": " : ":");
                    Write(b, value, indent, depth + 1);
                }
                Break(b, indent, depth);
                b.Append('}');
                return;
            case JsonArray a:
                if (a.Count == 0) { b.Append("[]"); return; }
                b.Append('[');
                for (var i = 0; i < a.Count; i++)
                {
                    if (i > 0) b.Append(',');
                    Break(b, indent, depth + 1);
                    Write(b, a[i], indent, depth + 1);
                }
                Break(b, indent, depth);
                b.Append(']');
                return;
            case JsonValue v:
                switch (v.GetValueKind())
                {
                    case System.Text.Json.JsonValueKind.String: WriteString(b, v.GetValue<string>()); return;
                    case System.Text.Json.JsonValueKind.True: b.Append("true"); return;
                    case System.Text.Json.JsonValueKind.False: b.Append("false"); return;
                    case System.Text.Json.JsonValueKind.Null: b.Append("null"); return;
                    case System.Text.Json.JsonValueKind.Number:
                        if (v.TryGetValue<long>(out var whole)) { b.Append(whole.ToString(System.Globalization.CultureInfo.InvariantCulture)); return; }
                        var number = v.GetValue<double>();
                        if (Math.Floor(number) == number && Math.Abs(number) < 9007199254740992d)
                        { b.Append(((long)number).ToString(System.Globalization.CultureInfo.InvariantCulture)); return; }
                        throw new NotSupportedException("Only integer numbers occur in impact outputs.");
                }
                break;
        }
        throw new NotSupportedException("Unsupported JSON node.");
    }

    private static void Break(StringBuilder b, bool indent, int depth)
    {
        if (!indent) return;
        b.Append('\n').Append(' ', depth * 2);
    }

    private static void WriteString(StringBuilder b, string s)
    {
        b.Append('"');
        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            switch (c)
            {
                case '"': b.Append("\\\""); break;
                case '\\': b.Append("\\\\"); break;
                case '\b': b.Append("\\b"); break;
                case '\f': b.Append("\\f"); break;
                case '\n': b.Append("\\n"); break;
                case '\r': b.Append("\\r"); break;
                case '\t': b.Append("\\t"); break;
                default:
                    if (c < 0x20) b.Append("\\u").Append(((int)c).ToString("x4"));
                    else if (char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1])) { b.Append(c).Append(s[i + 1]); i++; }
                    else if (char.IsSurrogate(c)) b.Append("\\u").Append(((int)c).ToString("x4"));
                    else b.Append(c);
                    break;
            }
        }
        b.Append('"');
    }
}
```

`Json/GitBlob.cs`: `public static string Sha1Hex(byte[] bytes)` returns the SHA-1 hex of `"blob {length}\0"` followed by `bytes`, in lowercase. `ImpactException.cs`: `public sealed class ImpactException(string message) : Exception(message);`.

### 3.3 `Tests/Impact/EcmaJsonTests.cs` (complete)

The expected strings were produced with Node 20's `JSON.stringify` on the same value.

```csharp
using System.Text.Json.Nodes;
using RuntimeKnowledge.Impact.Json;
using Xunit;

namespace RuntimeKnowledge.Tests.Impact;

public sealed class EcmaJsonTests
{
    private static JsonObject Vector() => new()
    {
        ["b"] = "quote\" back\\ nl\n tab\t ctl\u0001 eé emoji\U0001F600 ls  lone\ud800 lt< amp&",
        ["a"] = new JsonArray(1, new JsonObject { ["z"] = 2, ["y"] = null })
    };

    [Fact]
    public void Compact_output_matches_JSON_stringify() =>
        Assert.Equal("{\"b\":\"quote\\\" back\\\\ nl\\n tab\\t ctl\\u0001 eé emoji😀 ls  lone\\ud800 lt< amp&\",\"a\":[1,{\"z\":2,\"y\":null}]}",
            EcmaJson.Stringify(Vector()));

    [Fact]
    public void Indented_output_matches_JSON_stringify_with_two_spaces() =>
        Assert.Equal("{\n  \"b\": \"quote\\\" back\\\\ nl\\n tab\\t ctl\\u0001 eé emoji😀 ls  lone\\ud800 lt< amp&\",\n  \"a\": [\n    1,\n    {\n      \"z\": 2,\n      \"y\": null\n    }\n  ]\n}",
            EcmaJson.Stringify(Vector(), indent: true));

    [Fact]
    public void Canonical_digest_matches_the_pilot() =>
        Assert.Equal("148adb88869d3cc06f3846a31a23753cc0ca7c2b6fc6c1b959da96bbb9268417", EcmaJson.CanonicalDigest(Vector()));

    [Fact]
    public void Path_id_hash_matches_the_pilot() =>
        Assert.Equal("e43f95e0e0f52ac540a4669b1f5c3ab45472263d26c436c5effa9727b7d752d2",
            EcmaJson.Sha256Hex(EcmaJson.Stringify(new JsonArray(new JsonObject
            {
                ["type"] = "assertion",
                ["subject"] = new JsonObject { ["sourceId"] = "s", ["nodeIri"] = "a" }
            }))));

    [Fact]
    public void Empty_containers_and_parsed_numbers_are_written_as_JSON_stringify_does()
    {
        Assert.Equal("{}", EcmaJson.Stringify(new JsonObject(), indent: true));
        Assert.Equal("[]", EcmaJson.Stringify(new JsonArray(), indent: true));
        Assert.Equal("{\"n\":12}", EcmaJson.Stringify(JsonNode.Parse("{\"n\":12}")));
        Assert.Throws<NotSupportedException>(() => EcmaJson.Stringify(JsonValue.Create(1.5)));
    }

    [Fact]
    public void File_form_ends_with_a_newline() => Assert.Equal("{}\n", EcmaJson.File(new JsonObject()));
}
```

Steps:
- [ ] Create the projects, then write the test. Run `dotnet test runtime/RuntimeKnowledge.Tests --filter EcmaJson`: it fails to compile, because `EcmaJson` is missing.
- [ ] Implement 3.2. Run it again, until all six tests pass.
- [ ] Commit `feat: add the impact engine project and JSON writer (#198)` and push.

---

## Task 4: Bundle reader and impact model

**Files:** create `runtime/RuntimeKnowledge.Impact/Bundle/ImpactBundle.cs`, `Model/ImpactModel.cs`, `Model/Glob.cs` and `runtime/RuntimeKnowledge.Tests/Impact/CorpusPaths.cs`, `TestModels.cs` and `ImpactModelTests.cs`.

### 4.1 Interfaces

```csharp
namespace RuntimeKnowledge.Impact.Bundle;
public sealed record BundleFile(string Role, string SourceId, string Path, string Sha256, long Bytes);
public sealed class ImpactBundle
{
    public string Id { get; }
    public JsonObject Manifest { get; }                       // parsed, key order preserved
    public IReadOnlyList<BundleFile> Files { get; }
    public static ImpactBundle Open(string directory, string manifestId);          // reads manifests/<id>.json and blobs/<sha>
    public static ImpactBundle FromObjects(string manifestId, byte[] manifestBytes, Func<string, byte[]?> blob);
    public byte[]? Read(string role, string sourceId, string path);                // null when the manifest has no such file
}
```

Both factory methods verify the following, in this order, throwing `ImpactException("invalid impact bundle: <reason>")` on failure:
1. `sha256(manifestBytes) == manifestId`;
2. the manifest has `schemaVersion == 1`;
3. each file read has the SHA-256 and length the manifest records;
4. there is exactly one `impactInput` file for `division-core`.

Blobs are read lazily and cached by digest.

```csharp
namespace RuntimeKnowledge.Impact.Model;
public sealed class ImpactModel
{
    public static ImpactModel Build(ImpactBundle bundle);          // parses impactInput schema 1; any other version -> "unsupported impact input"
    public JsonObject SnapshotId { get; }                          // input.snapshotId, cloned
    public IReadOnlyList<SourceModel> Sources { get; }             // input order
    public SourceModel? Source(string id);
    public IReadOnlyList<JsonObject> Concepts { get; }
    public IReadOnlyList<JsonObject> ReviewedLinks { get; }
    public RegistryModel? Registry { get; }
    public ImpactBundle Bundle { get; }
}
public sealed class SourceModel
{
    public string Id { get; } public string Commit { get; } public JsonObject? EvidenceSelectors { get; }
    public string Status { get; } public string? Reason { get; } public int? SchemaVersion { get; }
    public IReadOnlyList<JsonObject> Nodes { get; }            // Map order
    public IReadOnlyList<JsonObject> Assertions { get; }
    public IReadOnlyList<JsonObject> Coverage { get; }
    public JsonObject? Node(string iri);                       // dictionary lookups
    public JsonObject? Evidence(string iri);
    public JsonObject? Assertion(string iri);
    public IReadOnlyList<JsonObject> AssertionsFrom(string subjectIri);   // Map order preserved
    public IReadOnlyList<JsonObject> AssertionsTo(string objectIri);
}
public sealed class RegistryModel
{
    public string Commit { get; } public string MappingSha256 { get; } public string BindingSha256 { get; }
    public IReadOnlyList<JsonObject> Mappings { get; } public IReadOnlyList<JsonObject> Bindings { get; } public IReadOnlyList<JsonObject> Gaps { get; }
    public JsonObject? MappingFor(string sourceId, string nodeIri);        // first match, as mappingFor
    public JsonObject? BindingFrom(string sourceId, string nodeIri);       // first match, as bindingFrom
    public static RegistryModel Empty { get; }                             // commit "", no rows; used when the input registry is null
}
```

`Glob.Matches(string selector, string path)` is a port of `matches()` in `src/c4/graph.mjs`. It throws `ImpactException("invalid selector")` under the same conditions. Translate the loop as written, and anchor the `Regex` with `RegexOptions.CultureInvariant`.

The JSON returned by the model is shared and read-only. Callers `DeepClone()` before inserting a node into an output.

### 4.2 Test helpers

`CorpusPaths.cs`:

```csharp
using RuntimeKnowledge.Tests.OpenApi;
namespace RuntimeKnowledge.Tests.Impact;
internal static class CorpusPaths
{
    public static string Corpus { get; } = Path.Combine(RepositoryPaths.Root, "test", "impact-corpus");
    public static string Objects { get; } = Path.Combine(Corpus, "objects");
    public static System.Text.Json.Nodes.JsonArray Steps(string scenario) =>
        System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(Path.Combine(Corpus, scenario, "steps.json")))!.AsArray();
    public static string Expected(string scenario, int step, string file) =>
        Path.Combine(Corpus, scenario, "expected", step.ToString("000"), file);
    public static string FirstManifest(string scenario, string command) =>
        Steps(scenario).First(s => s!["argv"]![0]!.GetValue<string>() == command && s["manifest"] is not null)!["manifest"]!.GetValue<string>();
}
```

`TestModels.cs` holds `ImpactModel MakeModel(Action<JsonObject>? change = null)`. It is a C# port of `makeModel()` in `test/fixtures/c4/fixture-repos.mjs`. It builds the same nodes, assertions, evidence, mappings, binding and concept as an `impactInput` `JsonObject`, applies `change`, and serialises it into an in-memory bundle with `ImpactBundle.FromObjects`. Its blob function returns the bytes of `src/app.ts` (`"export const api = 1;\n"`) for every `graphEvidence` path. Write it literally from `makeModel`, one row at a time, keeping the same IRIs and `'1'.repeat(40)` blob ids. The unit-test ports in Tasks 5–8 use it wherever the JavaScript test used `makeModel()`.

### 4.3 `ImpactModelTests.cs`

```csharp
using System.Text;
using RuntimeKnowledge.Impact;
using RuntimeKnowledge.Impact.Bundle;
using RuntimeKnowledge.Impact.Model;
using Xunit;

namespace RuntimeKnowledge.Tests.Impact;

public sealed class ImpactModelTests
{
    private static ImpactBundle Pilot() => ImpactBundle.Open(CorpusPaths.Objects, CorpusPaths.FirstManifest("pilot", "search"));

    [Fact]
    public void Pilot_bundle_builds_a_model_in_loader_order()
    {
        var model = ImpactModel.Build(Pilot());
        Assert.Equal(["fixture/Alpha", "fixture/Beta"], model.Sources.Select(s => s.Id));
        var alpha = model.Source("fixture/Alpha")!;
        Assert.Equal("available", alpha.Status);
        Assert.Equal(5, alpha.Nodes.Count);
        Assert.Equal(4, alpha.Assertions.Count);
        Assert.NotNull(alpha.Evidence("https://alpha.example/#evidence"));
        Assert.Single(alpha.AssertionsFrom("https://alpha.example/#container"));
        Assert.Equal(4, model.Registry!.Mappings.Count);
        Assert.NotNull(model.Registry.BindingFrom("fixture/Alpha", "https://alpha.example/#external"));
    }

    [Fact]
    public void Evidence_bytes_are_read_by_role_source_and_path() =>
        Assert.Equal("export const api = 1;\n",
            Encoding.UTF8.GetString(Pilot().Read("graphEvidence", "fixture/Alpha", "src/app.ts")!));

    [Fact]
    public void A_wrong_manifest_digest_is_refused()
    {
        var id = CorpusPaths.FirstManifest("pilot", "search");
        var bytes = File.ReadAllBytes(Path.Combine(CorpusPaths.Objects, "manifests", id + ".json"));
        var error = Assert.Throws<ImpactException>(() => ImpactBundle.FromObjects(new string('0', 64), bytes, _ => null));
        Assert.StartsWith("invalid impact bundle", error.Message);
    }

    [Fact]
    public void A_tampered_blob_is_refused()
    {
        var id = CorpusPaths.FirstManifest("pilot", "search");
        var bytes = File.ReadAllBytes(Path.Combine(CorpusPaths.Objects, "manifests", id + ".json"));
        var bundle = ImpactBundle.FromObjects(id, bytes, _ => "tampered"u8.ToArray());
        Assert.Throws<ImpactException>(() => ImpactModel.Build(bundle));
    }

    [Fact]
    public void Glob_matches_as_the_pilot_does()
    {
        Assert.True(Glob.Matches("src/**", "src/a/b.ts"));
        Assert.True(Glob.Matches("**/*.tf", "infra/main.tf"));
        Assert.False(Glob.Matches("src/*", "src/a/b.ts"));
        Assert.Throws<ImpactException>(() => Glob.Matches("../x", "x"));
    }

    [Fact]
    public void Test_model_matches_makeModel() =>
        Assert.Equal(5, TestModels.MakeModel().Registry!.Mappings.Count);
}
```

Steps:
- [ ] Write the tests. Run `--filter ImpactModel`: it fails to compile.
- [ ] Implement.
- [ ] Commit `feat: build the impact model from a bundle (#198)` and push.

---

## Task 5: Candidate search

**Files:** create `Engine/Coverage.cs` (ports of `coverageGaps` and `coverageInventory`), `Engine/Candidates.cs` (port of `findCandidates`) and `Tests/Impact/CandidatesTests.cs`.

**Interface:** `public static JsonObject Candidates.Find(ImpactModel model, string query = "", string? exactIri = null)`. `query` and `exactIri` follow the JavaScript semantics:
- `query.Trim()` is JavaScript's `trim()`. Use `string.Trim()`, which also trims Unicode whitespace, as JavaScript does.
- Lower-casing uses `ToLowerInvariant()`. This differs from JavaScript only for U+0130, which the baseline does not use. Note it in an XML comment.
- Tokens use `Regex.Split(lower, @"[^\p{L}\p{N}]+")` with empty strings removed, de-duplicated in first-seen order.
- `matchedText` is the node label, or `label — definition` for concepts, where the separator is an em dash with a space on each side.

Lead objects are `{key:{sourceId,nodeIri}, matchedText, matchKind, sourceCommit, supported, reason, kind}`, in that key order. JavaScript deletes `_rank` before output, so never add a rank to the output. The result is `{snapshotId, registryCommit, coverage, leads, gaps}`. Gaps are the registry gaps (cloned, in input order) followed by `Coverage.Gaps(model)`, plus `unknown-search` when there are no leads.

`Coverage.Gaps` orders its gaps by `string.CompareOrdinal(EcmaJson.Stringify(a), EcmaJson.Stringify(b))`. `Coverage.Inventory` builds each record as `{sourceId, ...entry}`, with `sourceId` first, and sorts the same way.

### `CandidatesTests.cs`

```csharp
using System.Text.Json.Nodes;
using RuntimeKnowledge.Impact;
using RuntimeKnowledge.Impact.Engine;
using RuntimeKnowledge.Impact.Json;
using Xunit;

namespace RuntimeKnowledge.Tests.Impact;

public sealed class CandidatesTests
{
    private static JsonArray Leads(JsonObject result) => result["leads"]!.AsArray();

    [Fact]
    public void Description_gives_leads_without_an_impact_conclusion()
    {
        var r = Candidates.Find(TestModels.MakeModel(), "change Alpha API");
        Assert.Contains(Leads(r), x => x!["key"]!["sourceId"]!.GetValue<string>() == "fixture/Alpha");
        Assert.All(Leads(r), x => Assert.False(x!.AsObject().ContainsKey("impact")));
    }

    [Fact]
    public void Exact_iri_and_no_match_gap()
    {
        var model = TestModels.MakeModel();
        Assert.Equal("https://alpha.example/#system", Leads(Candidates.Find(model, exactIri: "https://alpha.example/#system"))[0]!["key"]!["nodeIri"]!.GetValue<string>());
        var empty = Candidates.Find(model, "unmatched phrase");
        Assert.Empty(Leads(empty));
        Assert.Contains(empty["gaps"]!.AsArray(), g => g!["code"]!.GetValue<string>() == "unknown-search");
    }

    [Fact]
    public void Catalog_only_matches_stay_unsupported_leads() =>
        Assert.Contains(Leads(Candidates.Find(TestModels.MakeModel(), "catalog only")),
            x => x!["key"]!["nodeIri"]!.GetValue<string>() == "https://alpha.example/concept" && !x["supported"]!.GetValue<bool>());

    [Fact]
    public void Equal_labels_remain_separate_source_owned_leads()
    {
        var r = Candidates.Find(TestModels.MakeModel(), "Beta");
        Assert.Contains(Leads(r), x => x!["key"]!["sourceId"]!.GetValue<string>() == "fixture/Alpha");
        Assert.Contains(Leads(r), x => x!["key"]!["sourceId"]!.GetValue<string>() == "fixture/Beta");
    }

    [Fact]
    public void Search_reports_partial_source_coverage_with_results()
    {
        var model = TestModels.MakeModel(input => input["sources"]![0]!["graph"]!["coverage"]!.AsArray().Add(new JsonObject
            { ["iri"] = "gap", ["selector"] = "legacy/**", ["status"] = "excluded", ["priority"] = 5, ["reason"] = "Not reviewed" }));
        var r = Candidates.Find(model, "Alpha");
        Assert.NotEmpty(Leads(r));
        Assert.Contains(r["gaps"]!.AsArray(), g => g!["reason"]?.GetValue<string>() == "Not reviewed");
    }

    [Fact]
    public void A_query_or_exact_iri_is_required() =>
        Assert.Equal("query or exact IRI is required",
            Assert.Throws<ImpactException>(() => Candidates.Find(TestModels.MakeModel(), "  ")).Message);

    [Theory]
    [InlineData("pilot")]
    [InlineData("walkthrough")]
    public void Search_steps_reproduce_the_baseline_bytes(string scenario)
    {
        var steps = CorpusPaths.Steps(scenario);
        for (var i = 0; i < steps.Count; i++)
        {
            var step = steps[i]!;
            if (step["argv"]![0]!.GetValue<string>() != "search" || step["exit"]!.GetValue<int>() != 0) continue;
            var argv = step["argv"]!.AsArray().Select(x => x!.GetValue<string>()).ToList();
            string? Flag(string name) { var at = argv.IndexOf(name); return at < 0 ? null : argv[at + 1]; }
            var model = ImpactModel.Build(Bundle.ImpactBundle.Open(CorpusPaths.Objects, step["manifest"]!.GetValue<string>()));
            var actual = EcmaJson.File(Candidates.Find(model, Flag("--query") ?? "", Flag("--exact-iri")));
            Assert.Equal(File.ReadAllText(CorpusPaths.Expected(scenario, i, "candidates.json")), actual);
        }
    }
}
```

This file needs `using RuntimeKnowledge.Impact.Model;` for `ImpactModel`. `TestModels.MakeModel(change)` applies `change` to the input before the model is built.

Steps:
- [ ] Write the test. Run `--filter Candidates`: it fails to compile.
- [ ] Implement.
- [ ] Commit `feat: port candidate search to the impact engine (#198)` and push.

---

## Task 6: Impact paths and bindings (B9, B14)

**Files:** create `Engine/Paths.cs` (port of `readImpactPaths`) and `Tests/Impact/PathsTests.cs`. Regenerate the expected `paths.json` files.

**Interface:** `public static JsonObject Paths.Read(ImpactModel model, IReadOnlyList<(string SourceId, string NodeIri)> starts, int maxDepth = 4)`.

Port it as written, with these specifics:
- An assertion step is `{type:'assertion', subject, object, direction:'outgoing', assertionIri, relationship, basis, evidenceIris, evidence, commit}`. `evidence` holds deep clones of the source's evidence objects, in `evidenceIris` order.
- A binding step is `{type:'binding', from, to, registryCommit, identityEvidence}`, where `identityEvidence` is a deep clone of the binding's object.
- The path id is `EcmaJson.Sha256Hex(EcmaJson.Stringify(stepsArray))`.
- Candidate edges are ordered with `OrderBy(key(target), Ordinal).ThenBy(assertionIri ?? "", Ordinal)`.
- The starts are ordered by `key`.
- `unsupported-start` keeps today's reason, `Start lacks a supported mapping`. I2 Task 14 changes it.

**B9:** after the walk, add exactly one gap, `{code:'no-relationship-master-list', reason:'Impact direction is assumed from subject to object; no relationship master list is defined'}`. It goes immediately after the registry and coverage gaps, and before any `graph-unavailable`, `unsupported-start` or `unknown-paths` gap. I3 replaces this branch when a list exists.

### `PathsTests.cs`

```csharp
using System.Text.Json.Nodes;
using RuntimeKnowledge.Impact;
using RuntimeKnowledge.Impact.Bundle;
using RuntimeKnowledge.Impact.Engine;
using RuntimeKnowledge.Impact.Json;
using RuntimeKnowledge.Impact.Model;
using Xunit;

namespace RuntimeKnowledge.Tests.Impact;

public sealed class PathsTests
{
    private static readonly (string, string) AlphaSystem = ("fixture/Alpha", "https://alpha.example/#system");
    private static IEnumerable<JsonObject> Steps(JsonObject r) => r["paths"]!.AsArray().SelectMany(p => p!["steps"]!.AsArray().Select(s => s!.AsObject()));
    private static JsonArray Gaps(JsonObject r) => r["gaps"]!.AsArray();

    [Fact]
    public void Cross_source_path_has_distinct_inferred_assertion_and_binding()
    {
        var r = Paths.Read(TestModels.MakeModel(), [AlphaSystem], 5);
        var path = r["paths"]!.AsArray().First(p => p!["steps"]!.AsArray().Any(s => s!["type"]!.GetValue<string>() == "binding"))!;
        Assert.Contains(path["steps"]!.AsArray(), s => s!["type"]!.GetValue<string>() == "assertion" && s["basis"]!.GetValue<string>() == "inferred");
        var binding = path["steps"]!.AsArray().First(s => s!["type"]!.GetValue<string>() == "binding")!;
        Assert.Equal(new string('e', 40), binding["registryCommit"]!.GetValue<string>());          // B14
        Assert.Equal("reviewed", binding["identityEvidence"]!["reason"]!.GetValue<string>());
    }

    [Fact]
    public void Equal_labels_without_binding_produce_no_cross_source_step()
    {
        var model = TestModels.MakeModel(input => input["registry"]!["bindings"] = new JsonArray());
        var r = Paths.Read(model, [("fixture/Alpha", "https://alpha.example/#external")]);
        Assert.DoesNotContain(Steps(r), s => s["type"]!.GetValue<string>() == "binding");
        Assert.NotEmpty(Gaps(r));
    }

    [Fact]
    public void Preserves_direction_bounds_cycles_and_checks_depth()
    {
        var model = TestModels.MakeModel(input => input["sources"]![0]!["graph"]!["assertions"]!.AsArray().Add(new JsonObject
        {
            ["iri"] = "reverse", ["subject"] = "https://alpha.example/#container", ["object"] = "https://alpha.example/#system",
            ["relationship"] = "cycle", ["basis"] = "direct", ["evidenceIris"] = new JsonArray("https://alpha.example/#evidence")
        }));
        var r = Paths.Read(model, [AlphaSystem], 6);
        Assert.All(r["paths"]!.AsArray(), p => Assert.True(p!["steps"]!.AsArray().Count <= 6));
        Assert.DoesNotContain(Steps(r), s => s["assertionIri"]?.GetValue<string>() == "reverse");
        Assert.Equal("depth must be from 1 to 6", Assert.Throws<ImpactException>(() => Paths.Read(model, [AlphaSystem], 7)).Message);
        Assert.Equal("at least one start is required", Assert.Throws<ImpactException>(() => Paths.Read(model, [])).Message);
    }

    [Fact]
    public void Stale_bindings_remain_gaps_and_never_become_steps()
    {
        var model = TestModels.MakeModel(input =>
        {
            input["registry"]!["bindings"] = new JsonArray();
            input["registry"]!["gaps"] = new JsonArray(new JsonObject { ["code"] = "stale-binding", ["reason"] = "identity changed" });
        });
        var r = Paths.Read(model, [AlphaSystem]);
        Assert.Contains(Gaps(r), g => g!["code"]!.GetValue<string>() == "stale-binding");
        Assert.DoesNotContain(Steps(r), s => s["type"]!.GetValue<string>() == "binding");
    }

    [Fact]
    public void Without_a_master_list_one_gap_says_direction_is_assumed()     // B9
    {
        var r = Paths.Read(TestModels.MakeModel(), [AlphaSystem]);
        var gap = Assert.Single(Gaps(r), g => g!["code"]!.GetValue<string>() == "no-relationship-master-list");
        Assert.Contains("assumed", gap!["reason"]!.GetValue<string>());
        Assert.All(Steps(r).Where(s => s["type"]!.GetValue<string>() == "assertion"), s => Assert.Equal("outgoing", s["direction"]!.GetValue<string>()));
    }

    [Theory]
    [InlineData("pilot")]
    [InlineData("walkthrough")]
    public void Path_steps_reproduce_the_baseline_bytes(string scenario)
    {
        var steps = CorpusPaths.Steps(scenario);
        for (var i = 0; i < steps.Count; i++)
        {
            var step = steps[i]!;
            if (step["argv"]![0]!.GetValue<string>() != "paths" || step["exit"]!.GetValue<int>() != 0) continue;
            var argv = step["argv"]!.AsArray().Select(x => x!.GetValue<string>()).ToList();
            var starts = argv.Select((v, k) => (v, k)).Where(x => x.v == "--start").Select(x => argv[x.k + 1])
                .Select(s => { var at = s.IndexOf('='); return (s[..at], s[(at + 1)..]); }).ToList();
            var depthAt = argv.IndexOf("--max-depth");
            var model = ImpactModel.Build(ImpactBundle.Open(CorpusPaths.Objects, step["manifest"]!.GetValue<string>()));
            var actual = EcmaJson.File(Paths.Read(model, starts, depthAt < 0 ? 4 : int.Parse(argv[depthAt + 1])));
            Assert.Equal(File.ReadAllText(CorpusPaths.Expected(scenario, i, "paths.json")), actual);
        }
    }
}
```

The baseline does not have the B9 gap yet. Do the update this way:
- [ ] Write the test. Run `--filter Paths`: it fails to compile.
- [ ] Implement **without** the B9 gap first. Run the test: every case passes except `Without_a_master_list…`. This proves parity with the Node engine.
- [ ] Commit the engine code alone, as `feat: port impact paths to the impact engine (#198)`.
- [ ] Add the B9 gap. Update the baseline: in `src/c4/paths.mjs`, temporarily add the identical gap object at the same position, run `node test/impact-corpus/capture.mjs`, then revert `paths.mjs`. Check that `git diff test/impact-corpus` touches only `paths.json` files, and only adds the gap.
- [ ] Commit `test: baseline gains the no-relationship-master-list gap (B37 item 4)`, containing only the corpus.
- [ ] Commit `feat: report the assumed impact direction (B9)`, containing the engine change, and push both commits.

---

## Task 7: C4 views, code supplements and view Markdown

**Files:** create `Engine/Views.cs` (port of `projectView`), `Engine/CodeSupplement.cs` (port of `verifyCodeSupplement`), `Render/ViewMarkdown.cs` (port of `renderView`, with `EncodeUriComponent`) and `Tests/Impact/ViewsTests.cs`.

**Interfaces:**
- `Views.Project(ImpactModel model, (string SourceId, string NodeIri) focus, string level, JsonObject? supplement = null) → JsonObject`.
- `CodeSupplement.Verify(ImpactModel model, (string, string) focus, JsonObject supplement) → JsonObject`.
- `ViewMarkdown.Render(JsonObject view) → (string Json, string Markdown)`.
- `Assessments.ViewDigest(JsonObject view)`, as `EcmaJson.CanonicalDigest(view)`, is defined in Task 8. In this task, add it as an internal helper and use it from `ViewMarkdown`.

Port details:
- `CodeSupplement.Verify` reads `model.Bundle.Read("inspectedSource", s.sourceId, s.path)`, and `null` means `source path missing`. Decode the bytes as UTF-8, re-encode, compare `GitBlob.Sha1Hex` with `blobId`, then check the line range and `Glob.Matches` against `source.EvidenceSelectors[role]`. The messages are exactly those of `code-evidence.mjs`.
- The context-level selection reads `source.Assertions` in Map order, as JavaScript's `[...assertions.values()]` does.
- `EncodeUriComponent(string)` encodes UTF-8 bytes as `%XX`, with uppercase hex, for every byte outside `A–Z a–z 0–9 - _ . ! ~ * ' ( )`. Do not use `Uri.EscapeDataString`, which also escapes `! ' ( ) *`.

### `ViewsTests.cs`

Port these five tests from `test/c4-project.test.mjs`, using `TestModels.MakeModel(change)` in place of mutating the model:
- `all four levels retain focus and reviewed boundaries`;
- `missing code mapping is an explicit gap`: remove mappings with `level == "code"` in `change`;
- `external context is evidenced and retains source identity`;
- `unsupported focus is a gap and other boundaries stay out`;
- `container view includes an evidenced directly connected external system`: add the `link` assertion in `change`.

Port the three tests from `test/c4-code-evidence.test.mjs` against the pilot corpus bundle of the supplement step. Find it with `CorpusPaths.Steps("pilot")`: it is the step whose argv contains `--supplement`. The supplement is `test/impact-corpus/pilot/inputs/011/supplement.json`. Mutate copies of it for the blob, line-range and focus failures. Expect messages matching `blob`, `line` and `component focus mismatch`. For the deployment-selector case, add `"deployment": ["**/*.tf"]` to `EvidenceSelectors` through a `TestModels` variant, and expect `role selector mismatch`.

Add the baseline theory:

```csharp
[Theory]
[InlineData("pilot")]
[InlineData("walkthrough")]
public void View_steps_reproduce_the_baseline_bytes(string scenario)
{
    var steps = CorpusPaths.Steps(scenario);
    for (var i = 0; i < steps.Count; i++)
    {
        var step = steps[i]!;
        if (step["argv"]![0]!.GetValue<string>() != "view" || step["exit"]!.GetValue<int>() != 0) continue;
        var argv = step["argv"]!.AsArray().Select(x => x!.GetValue<string>()).ToList();
        string? Flag(string n) { var at = argv.IndexOf(n); return at < 0 ? null : argv[at + 1]; }
        var focus = Flag("--focus")!; var eq = focus.IndexOf('=');
        var supplementPath = Flag("--supplement");
        JsonObject? supplement = supplementPath is null ? null :
            JsonNode.Parse(File.ReadAllText(Path.Combine(CorpusPaths.Corpus, scenario, "inputs", supplementPath["$input/".Length..])))!.AsObject();
        var model = ImpactModel.Build(ImpactBundle.Open(CorpusPaths.Objects, step["manifest"]!.GetValue<string>()));
        var (json, markdown) = ViewMarkdown.Render(Views.Project(model, (focus[..eq], focus[(eq + 1)..]), Flag("--level")!, supplement));
        var level = Flag("--level");
        Assert.Equal(File.ReadAllText(CorpusPaths.Expected(scenario, i, $"view-{level}.json")), json);
        Assert.Equal(File.ReadAllText(CorpusPaths.Expected(scenario, i, $"view-{level}.md")), markdown);
    }
}
```

Steps:
- [ ] Write the tests. Run `--filter Views`: it fails to compile.
- [ ] Implement.
- [ ] Commit `feat: port C4 views to the impact engine (#198)` and push.

---

## Task 8: Schema 1 assessments and feedback

**Files:** create `Engine/Assessments.cs` (ports of `newAssessment`, `validateAssessment` and `viewDigest`), `Render/AssessmentMarkdownV1.cs` (port of `renderAssessment`), `Render/FeedbackMarkdown.cs` (port of `renderFeedback`) and `Tests/Impact/AssessmentsV1Tests.cs`.

**Interfaces:**
- `Assessments.New(ImpactModel model, string requestId, string description) → JsonObject`. It produces schema 1 in I1; Task 17 switches it to schema 2.
- `Assessments.Validate(JsonObject record, ImpactModel model, JsonArray paths, IReadOnlyList<JsonObject> views) → string digest`.
- `AssessmentMarkdownV1.Render(JsonObject record, ImpactModel model, JsonArray paths, IReadOnlyList<JsonObject> views) → string`.
- `FeedbackMarkdown.Render(JsonObject feedback, string requestId, string digest) → string`.

Port details:
- `text(x)` means "a string whose `Trim()` is not empty".
- `escaped(x)` is `String(x??'')`, then: line breaks (`/[\r\n]+/g`) become one space, then `|` becomes `&#124;`, `<` becomes `&lt;` and `>` becomes `&gt;`, in that order.
- `String(x)` of a JSON number writes its integer text. Of `null` or an absent value it writes `''`, because of `?? ''`. Of a boolean it writes `true` or `false`. Of an object, which never happens in practice, it writes `[object Object]`.
- View reconstruction calls `Views.Project(..., supplement: inspected.evidence[0])`, exactly as the JavaScript does.
- `'approved' in record || 'approvalStatus' in record` becomes `record.ContainsKey(...)`.
- The digest is `EcmaJson.CanonicalDigest(record)`.

### `AssessmentsV1Tests.cs`

Port the five tests in `test/c4-assessment.test.mjs` and `test/c4-feedback.test.mjs` literally, using `TestModels.MakeModel()`. Use the JavaScript's `paths` fixture as a `JsonArray`: `path-1` with two assertion steps. Expect the same message patterns:
- `/snapshot/` becomes `snapshot mismatch`;
- `/path|evidence/` becomes `unresolved path`;
- `/architect/` becomes `architect is required`;
- `/path.*impact|impact.*path/` becomes `impact is not present in a cited path`;
- `/digest/` becomes `feedback assessment digest mismatch`.

Add:

```csharp
[Fact]
public void Approval_fields_are_refused()
{
    var model = TestModels.MakeModel();
    var record = Assessments.New(model, "CR-1", "Change");
    record["approved"] = true;
    Assert.Equal("approval is not supported",
        Assert.Throws<ImpactException>(() => Assessments.Validate(record, model, [], [])).Message);
}
```

Then add a baseline theory over the pilot and walkthrough scenarios:
- `assessment-init`: compare `assessment.json`.
- `assessment-render`: load `--input` and every `--view-input` from `inputs/<step>/`. Recompute `paths` as the CLI does, `Paths.Read(model, confirmedLeads)` when there are confirmed leads and otherwise `[]`, then compare `assessment.md`.
- `feedback-render`: compare `feedback.md`.

Steps:
- [ ] Write the tests. Run `--filter AssessmentsV1`: it fails to compile.
- [ ] Implement.
- [ ] Commit `feat: port schema 1 assessments and feedback (#198)` and push.

---

## Task 9: Engine command-line host and baseline conformance

**Files:** create `runtime/RuntimeKnowledge.Impact/Engine/CommandRunner.cs`, `runtime/RuntimeKnowledge.Impact.Cli/Program.cs` and `runtime/RuntimeKnowledge.Tests/Impact/ConformanceTests.cs`.

### 9.1 Contract

```text
RuntimeKnowledge.Impact.Cli <command> --bundle <dir> --manifest <id> --staging <dir> [flags]
  search             [--query <text>] [--exact-iri <iri>]                    -> candidates.json
  paths              --start <id=iri>... [--max-depth <n>]                  -> paths.json
  view               --focus <id=iri> --level <level> [--supplement <file>] -> view-<level>.json, view-<level>.md
  assessment-init    --request-id <id> --description-file <file>            -> assessment.json
  assessment-render  --input <file> [--view-input <file>]...                -> assessment.md
  feedback-render    --input <file> --assessment-file <file> [--view-input <file>]...  -> feedback.md
```

- Exit 0 writes the files into `--staging`, an existing empty directory.
- Exit 1 writes the error message and `\n` to stderr, and writes no files.
- Any other failure is a bug; it exits 2 with the stack trace.

The Node front end has already done every check that today's `cli.mjs` does before computing: flag parsing, checkouts, output location, snapshot and registry loading, and the required-flag checks such as `focus and level required`. The tool still validates its own flags, but the front end's checks fire first, so the messages come out in the same order as today.

`CommandRunner.Run(string[] args) → (int Exit, IReadOnlyList<(string Name, string Text)> Files, string? Error)` is pure. It reads inputs with `File.ReadAllBytes` followed by `Encoding.UTF8.GetString`, which keeps any BOM. `Program.Main` writes `Files` with UTF-8 and no BOM.

### 9.2 `ConformanceTests.cs`

```csharp
using System.Text.Json.Nodes;
using RuntimeKnowledge.Impact.Engine;
using Xunit;

namespace RuntimeKnowledge.Tests.Impact;

public sealed class ConformanceTests
{
    public static TheoryData<string, int> EngineSteps()
    {
        var data = new TheoryData<string, int>();
        foreach (var scenario in new[] { "pilot", "walkthrough" })
        {
            var steps = CorpusPaths.Steps(scenario);
            for (var i = 0; i < steps.Count; i++)
                if (steps[i]!["manifest"] is not null) data.Add(scenario, i);
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(EngineSteps))]
    public void Every_engine_step_reproduces_its_baseline(string scenario, int index)
    {
        var step = CorpusPaths.Steps(scenario)[index]!;
        var staging = Directory.CreateTempSubdirectory("impact-conformance-").FullName;
        try
        {
            var args = ToolArgs(scenario, index, step, staging);
            var (exit, files, error) = CommandRunner.Run(args);
            Assert.Equal(step["exit"]!.GetValue<int>(), exit);
            Assert.Equal(step["stderr"]!.GetValue<string>(), error is null ? "" : error + "\n");
            Assert.Equal(step["files"]!.AsArray().Select(f => f!.GetValue<string>()), files.Select(f => f.Name).Order(StringComparer.Ordinal));
            foreach (var (name, text) in files)
                Assert.Equal(File.ReadAllText(CorpusPaths.Expected(scenario, index, name)), text);
        }
        finally { Directory.Delete(staging, true); }
    }

    // Maps the recorded CLI argv to tool flags. It drops the --catalog-commit, --registry-commit,
    // --checkout and --output flags that only the Node front end uses, resolves every
    // $input/<step>/<name> to test/impact-corpus/<scenario>/inputs/<step>/<name>, and adds
    // --bundle CorpusPaths.Objects, --manifest step.manifest and --staging staging.
    private static string[] ToolArgs(string scenario, int index, JsonNode step, string staging) { /* implement as described */ return []; }
}
```

The `ToolArgs` comment is the specification. Write the helper fully: it is about 25 lines.

### 9.3 Steps

- [ ] Write the test. Run `--filter Conformance`: it fails, because `CommandRunner` is missing.
- [ ] Implement `CommandRunner` and `Program`. Run the test until every step passes.
- [ ] Commit `feat: add the impact engine command line (#198)` and push.

---

## Task 10: Node front end and CI

**Files:** rewrite `src/c4/cli.mjs` and create `src/c4/engine.mjs`. Modify `package.json` (`"impact:build"` and `"pretest": "npm run impact:build"`), `.gitignore` (`.cache/`) and `.github/workflows/ci.yml`.

### 10.1 `src/c4/engine.mjs`

```js
import { execFileSync } from 'node:child_process';
import { existsSync, mkdirSync, rmSync } from 'node:fs';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = fileURLToPath(new URL('../../', import.meta.url));
const out = join(root, '.cache', 'impact-cli'), dll = join(out, 'RuntimeKnowledge.Impact.Cli.dll');

export function buildImpactCli() {
  const lock = join(root, '.cache', 'impact-cli.lock');
  mkdirSync(join(root, '.cache'), { recursive: true });
  for (let i = 0; ; i++) {
    try { mkdirSync(lock); break; }
    catch (e) { if (e.code !== 'EEXIST' || i > 600) throw e; execFileSync('sleep', ['0.5']); if (existsSync(dll)) return dll; }
  }
  try {
    execFileSync('dotnet', ['build', join(root, 'runtime/RuntimeKnowledge.Impact.Cli'), '-c', 'Release', '-o', out, '--nologo', '-v', 'q'],
      { stdio: ['ignore', 'pipe', 'pipe'] });
    return dll;
  } finally { rmSync(lock, { recursive: true, force: true }); }
}

export function impactCli() {
  if (process.env.DIVISION_IMPACT_CLI) return process.env.DIVISION_IMPACT_CLI;
  return existsSync(dll) ? dll : buildImpactCli();
}

export function runImpactCli(args) {
  try {
    execFileSync('dotnet', [impactCli(), ...args], { encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] });
  } catch (error) {
    if (error.status === 1) throw new Error(String(error.stderr).replace(/\n$/, ''));
    throw error;
  }
}
```

`package.json`: `"impact:build": "node -e \"import('./src/c4/engine.mjs').then(m => m.buildImpactCli())\""`.

### 10.2 `src/c4/cli.mjs`

Keep today's `allowed`, `common`, `parse`, `pair`, `sourceKey`, `json` and `canonicalPath`, and the export signature `runC4Cli(argv, {cwd, stdout, stderr})`. The body becomes:

1. As today: parse, checkouts, output location, `loadSnapshot` and `loadRegistries`.
2. As today, and in the same order, the per-command checks and input reads that can throw before computing:
   - for `view`: `focus and level required`, then the supplement `JSON.parse`;
   - for `assessment-init`: `request ID and description file required`;
   - for the render commands: `input is required`, then `JSON.parse` of the input, of the `--assessment-file`, and of every `--view-input`.
3. Collect `extraFiles`: from the supplement, and from every view-input item with `basis === 'architect-inspected'`, as `{sourceId: item.key.sourceId, path: item.path}`.
4. Create a staging directory with `mkdtempSync(join(tmpdir(), 'division-impact-'))` and `chmodSync(dir, 0o700)`. Write the impact bundle into `<staging>/bundle`, and create `<staging>/out`.
5. Call `runImpactCli([command, '--bundle', bundleDir, '--manifest', id, '--staging', outDir, ...toolFlags])`. The tool flags are the command flags with their input paths resolved to absolute paths.
6. Read `<staging>/out` in `readdirSync().sort()` order into the `files` map. Then run today's output checks unchanged (symlink, overwriting an input), and today's `mkdirSync` and writes, and print `Wrote N file(s)`.
7. `finally`: `rmSync(staging, { recursive: true, force: true })`.

One difference from today: today's files map is in insertion order, while the staging read is sorted. Only the count is printed, so the output is unchanged.

### 10.3 CI

In `.github/workflows/ci.yml`, in the job that runs `npm test`, add these steps before `npm ci`:

```yaml
      - uses: actions/setup-dotnet@v4
        with:
          global-json-file: runtime/global.json
      - run: dotnet restore runtime/RuntimeKnowledge.Impact.Cli --locked-mode
```

### 10.4 Steps

- [ ] Run `node --test test/impact-corpus.test.mjs test/c4-cli.test.mjs`. They pass on the Node engine: this is the control.
- [ ] Implement 10.1–10.3. Run `npm run impact:build`, then the same tests, then `node --test test/acceptance/c4.test.mjs` and `npm test`. All must pass with no change to `test/impact-corpus/`.
- [ ] Run `DIVISION_IMPACT_CLI=/nonexistent.dll node --test test/c4-cli.test.mjs`. It must fail, which proves the tool, not the Node engine, is now used.
- [ ] Commit `feat: run npm run c4 through the impact engine (#198)` and push. Check that CI is green.

---

## Task 11: Remove the Node engine

**Files:**
- delete `src/c4/candidates.mjs`, `paths.mjs`, `project.mjs`, `render.mjs`, `assessment.mjs`, `feedback.mjs` and `code-evidence.mjs`;
- delete `test/c4-candidates.test.mjs`, `c4-paths.test.mjs`, `c4-project.test.mjs`, `c4-render.test.mjs`, `c4-assessment.test.mjs`, `c4-feedback.test.mjs` and `c4-code-evidence.test.mjs`, which are ported in Tasks 5–8;
- remove `makeModel` from `test/fixtures/c4/fixture-repos.mjs`, if nothing else imports it;
- create `test/fixtures/c4/digest.mjs`, a test-only `viewDigest` for asserting record contents (`canonical` plus SHA-256, eight lines);
- point `test/c4-cli.test.mjs` at it;
- modify `test/representative-fixture.test.mjs` if it imports a deleted module.

- [ ] Run `grep -rn "c4/\(candidates\|paths\|project\|render\|assessment\|feedback\|code-evidence\)" src test deploy scripts`. Fix every hit.
- [ ] Run `npm test` and `dotnet test runtime/RuntimeKnowledge.Tests --filter "Category!=Database&Category!=Performance"`.
- [ ] Commit `refactor: remove the Node impact engine (#198)` and push.

## I1 close

- [ ] CI is green on the branch.
- [ ] Post the increment handoff on #198. It names I1 as done, lists B9, B14, B24 and B37, and gives I2 as next. It also notes: the baseline lives at `test/impact-corpus`, `DIVISION_IMPACT_CLI` exists, and architects now need the .NET SDK.
- [ ] **Stop.**
