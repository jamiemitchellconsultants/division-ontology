# C4 impact assessment pilot — detailed implementation plan

**Inputs:** [accepted vision](../../features/c4-diagrams-from-source-knowledge-graphs/01-vision.md), [behaviours B1–B15](../../features/c4-diagrams-from-source-knowledge-graphs/02-behaviours.md), [architecture](../../features/c4-diagrams-from-source-knowledge-graphs/03-architecture.md), ADRs 0001–0003, and the [task plan](../../superpowers/plans/c4-diagrams-from-source-knowledge-graphs/plan.md).

**Build rule:** Work on `feature/c4-diagrams-from-source-knowledge-graphs/dev-build` only after this plan PR merges. Execute Tasks 1–10 in order. Add the named test file first, run the focused test and see it fail, implement the interface, run the focused test and `npm test`, review `git diff --cached` and `git diff --cached --check`, then commit and push that task. The superpowers plan has the exact commit messages. Do not stage `dist/`.

## Data and command contracts

Use source-owned keys `{sourceId,nodeIri}` throughout. A label is never an ID. Sort serialized arrays by stable source ID and IRI with code-point comparison.

```js
// SnapshotId = {catalogCommit,catalogSha256,sources:[{id,commit}]}
// Snapshot = {id:SnapshotId,catalog,sources:Map<sourceId,SourceState>}
// SourceState = {id,commit,checkout,config,graphState}
// GraphState = {status:'available'|'unavailable',reason?,nodes:Map,
//   assertions:Map,evidence:Map,coverage:Array}
// GraphNode = {iri,kind,label,denotes?}
// GraphAssertion = {iri,subject,relationship,object,basis:'direct'|'inferred',
//   evidenceIris:string[]}
// GraphEvidence = {iri,path,startLine,endLine,blobId,role}
// Gap = {code,sourceId?,nodeIri?,reason}
// RegistryState = {commit,mappingSha256,bindingSha256,mappings,bindings,gaps}
```

`c4/mappings.json` is `{"schemaVersion":1,"mappings":[]}` and `c4/bindings.json` is `{"schemaVersion":1,"bindings":[]}` at first. These empty reviewed registries are deliberate; real mappings need graph evidence that the current JointBooking catalog commit does not provide. Each mapping has `sourceId`, `nodeIri`, `level` (`context`, `container`, `component`, `code`), `role` (`person`, `software-system`, `container`, `component`, `code`), optional `parent:{sourceId,nodeIri}`, `supportAssertionIri` and `supportEvidenceIri`. Each binding has `fromSourceId`, `externalNodeIri`, `toSourceId`, `targetNodeIri`, `sourceAssertionIri`, `sourceEvidenceIri`, and `identityEvidence:{sourceId,path,startLine,endLine,blobId,reason}`. A malformed registry is an error; a reference made stale by a pinned source commit is an unresolved gap and excluded from supported lookup.

The upstream source graph is schema version 1 from `InternalOntology/docs/features/source-knowledge-graph/03-architecture.md`. Read `ontology.config.json` at the source commit; it supplies `sourceGraph.enabled`, `root`, and eligible evidence-role selectors. Parse committed Turtle below that root except `graph-schema.ttl` with the existing `n3` dependency. Core resource classes are `GraphNode`, `GraphAssertion`, `GraphEvidence`, and `GraphCoverage`; the `kit:` namespace is `https://ontology-kit.dev/schema#`. Preserve `kit:direct` and `kit:inferred`. Do not execute source scripts or import the upstream toolkit at runtime.

The CLI is `npm run c4 -- <command>`. `search`, `paths`, and `view` require `--catalog-commit SHA`, `--registry-commit SHA`, one `--checkout owner/repo=/absolute/path` per catalog source, and `--output DIR`. `paths` adds repeatable `--start owner/repo=IRI`. `view` adds `--focus owner/repo=IRI` and `--level context|container|component|code`. `assessment-init` adds `--request-id` and `--description-file`; `assessment-render` and `feedback-render` take `--input FILE`. Never write partial output on a validation failure.

## Reproducible fixture

Create `test/fixtures/c4/fixture-repos.mjs` exporting `withPilotFixture(run)` and `makeModel()`. The former creates temporary Git repositories for the division and two sources, configures `user.name`, `user.email`, and `origin`, writes committed files, and deletes them in `finally`. It returns `{divisionRoot,catalogCommit,registryCommit,checkouts,sourceCommits,keys,outputDir,writeDivision,readDivision,commitDivision,writeSource,commitSource,codeSupplement}`. `checkouts` maps `fixture/Alpha` and `fixture/Beta` to absolute paths. `sourceCommits` has `alpha` and `beta` SHA fields. `keys` has `alphaSystem`, `alphaContainer`, `alphaComponent`, `alphaCode`, `externalBeta`, and `betaSystem` as source-owned key objects.

Alpha has a system, container, component, source symbol and external Beta system with evidenced contains, inferred communicates-with and code calls assertions. Beta has a Python system whose label equals Alpha's external Beta node label. Each evidence resource cites a committed blob and covered path. Alpha's `src/app.ts`, `src/App.cs`, `src/App.java` and Terraform file, and Beta's `src/app.py`, permit all four language and deployment checks. Add source-symbol nodes and evidence for each language without changing the shared graph schema. Make `makeModel()` return `{snapshot,registry,keys,paths,views}` with the same graph facts in memory for pure-function tests; `paths` and `views` are literal fixtures, so earlier tasks do not depend on later modules. Fixture registries contain supported mappings and one reviewed binding; production registries remain empty. Use `n3.Writer` to create valid fixture Turtle and `git hash-object` for evidence blob IDs. Add fixture mutations only where a test needs staleness or a new commit.

## Task 1 — pinned catalog and checkouts

**Files:** `src/c4/git.mjs`, `src/c4/snapshot.mjs`, `test/c4-snapshot.test.mjs`, fixture helper.

**Interfaces:** `readAtCommit(checkout,commit,path)->string|null`, `listAtCommit(checkout,commit,root)->string[]`, `verifyCheckout(checkout,expectedUrl,commit)->void`, `loadSnapshot({divisionRoot,catalogCommit,checkouts})->Snapshot`.

Use `execFileSync('git', args)` with argument arrays and no shell. Validate 40-character commits and safe repository-relative paths. Verify checkout root, clean state, origin identity and pinned commit availability. Read `dist/catalog.json` and `sources.json` from the specified division commit; compare source ID sets, and SHA-256 the exact catalog bytes. Do not clone latest refs. A missing configured source checkout is an error; a source without an enabled graph remains in the snapshot with an unavailable graph state.

**Complete test file:**

```js
import test from 'node:test';
import assert from 'node:assert/strict';
import {createHash} from 'node:crypto';
import {withPilotFixture} from './fixtures/c4/fixture-repos.mjs';
import {loadSnapshot} from '../src/c4/snapshot.mjs';
import {readAtCommit} from '../src/c4/git.mjs';

test('snapshot uses published bytes and pinned source commits', async () => {
  await withPilotFixture(async f => {
    const s = loadSnapshot(f);
    const raw = readAtCommit(f.divisionRoot,f.catalogCommit,'dist/catalog.json');
    assert.equal(s.id.catalogSha256,createHash('sha256').update(raw).digest('hex'));
    assert.deepEqual(s.id.sources,[
      {id:'fixture/Alpha',commit:f.sourceCommits.alpha},
      {id:'fixture/Beta',commit:f.sourceCommits.beta}]);
  });
});
test('missing and wrong source checkouts fail closed', async () => {
  await withPilotFixture(async f => {
    assert.throws(() => loadSnapshot({...f,checkouts:new Map()}),/checkout/i);
    const wrong = new Map(f.checkouts);
    wrong.set('fixture/Alpha',f.checkouts.get('fixture/Beta'));
    assert.throws(() => loadSnapshot({...f,checkouts:wrong}),/origin|identity/i);
  });
});
```

Add focused cases for dirty checkout and unavailable commit before completing the task. Commit and push Task 1's message from the task plan.

## Task 2 — source graph and coverage

**Files:** `src/c4/graph.mjs`, `test/c4-graph.test.mjs`, fixture helper.

**Interfaces:** `loadSourceGraph({checkout,commit,config})->GraphState`, `coverageFor(graph,path)->{status,reason?,coverageIri?}`. A disabled or absent optional graph returns `unavailable`; malformed present graph throws. Require named resources and schema version 1; validate assertion endpoints, evidence blob and line range, role selector, coverage status and highest-priority selector. Unmatched paths are unknown. A JointBooking deployment citation must match its pinned Terraform selector.

After the parser exists, wire `loadSnapshot` to populate each source's `graphState` through `loadSourceGraph`, leaving the Task 1 snapshot identity unchanged.

**Complete test file:**

```js
import test from 'node:test';
import assert from 'node:assert/strict';
import {withPilotFixture} from './fixtures/c4/fixture-repos.mjs';
import {loadSnapshot} from '../src/c4/snapshot.mjs';
import {loadSourceGraph,coverageFor} from '../src/c4/graph.mjs';

test('preserves inferred assertion and evidence', async () => {
  await withPilotFixture(async f => {
    const s = loadSnapshot(f).sources.get('fixture/Alpha');
    const g = loadSourceGraph(s);
    const a = [...g.assertions.values()].find(x => x.relationship.endsWith('#communicates-with'));
    assert.equal(a.basis,'inferred');
    assert.ok(g.evidence.has(a.evidenceIris[0]));
    assert.equal(coverageFor(g,'src/app.ts').status,'covered');
    assert.equal(coverageFor(g,'unknown/file').status,'unknown');
  });
});
test('disabled graph is unavailable, not an empty proof', async () => {
  await withPilotFixture(async f => {
    const s = loadSnapshot(f).sources.get('fixture/Alpha');
    assert.equal(loadSourceGraph({...s,config:{sourceGraph:{enabled:false}}}).status,'unavailable');
  });
});
```

Add focused cases for stale evidence blob, malformed Turtle and conflicting coverage priorities. Commit and push Task 2.

## Task 3 — reviewed registries

**Files:** `src/c4/registry.mjs`, `c4/mappings.json`, `c4/bindings.json`, `test/c4-registry.test.mjs`.

**Interfaces:** `loadRegistries({divisionRoot,registryCommit,snapshot})->RegistryState`, `mappingFor(registry,sourceId,nodeIri)->Mapping|undefined`, `bindingFrom(registry,sourceId,externalNodeIri)->Binding|undefined`. Read files at the explicit registry commit and hash exact bytes. Reject duplicate keys, invalid levels/roles and unsafe evidence paths. A mapping's support assertion must touch its node; the support evidence must belong to that assertion. Its parent must resolve to a supported higher-level mapping. A binding needs an assertion to its external node, a participating target and verified identity evidence at the pinned source commit. Equal labels do not help.

**Complete test file:**

```js
import test from 'node:test';
import assert from 'node:assert/strict';
import {withPilotFixture} from './fixtures/c4/fixture-repos.mjs';
import {loadSnapshot} from '../src/c4/snapshot.mjs';
import {loadRegistries,mappingFor,bindingFrom} from '../src/c4/registry.mjs';

test('supported mapping and binding keep both identities', async () => {
  await withPilotFixture(async f => {
    const r = loadRegistries({...f,snapshot:loadSnapshot(f)});
    assert.equal(mappingFor(r,'fixture/Alpha',f.keys.alphaSystem.nodeIri).level,'context');
    const b = bindingFrom(r,'fixture/Alpha',f.keys.externalBeta.nodeIri);
    assert.equal(b.toSourceId,'fixture/Beta');
    assert.notEqual(b.externalNodeIri,b.targetNodeIri);
  });
});
test('stale mapping support becomes unresolved', async () => {
  await withPilotFixture(async f => {
    const m = JSON.parse(f.readDivision('c4/mappings.json'));
    m.mappings[0].supportEvidenceIri = 'https://alpha.example/#missing';
    f.writeDivision('c4/mappings.json',JSON.stringify(m));
    const registryCommit = f.commitDivision('stale registry');
    const r = loadRegistries({...f,registryCommit,snapshot:loadSnapshot(f)});
    assert.equal(mappingFor(r,'fixture/Alpha',f.keys.alphaSystem.nodeIri),undefined);
    assert.ok(r.gaps.some(g => g.code === 'stale-mapping'));
  });
});
```

Add duplicate, unjoined target and stale identity evidence cases. Commit and push Task 3.

## Task 4 — candidate leads

**Files:** `src/c4/candidates.mjs`, `test/c4-candidates.test.mjs`.

**Interface:** `findCandidates({snapshot,registry,query,exactIri})->{snapshotId,registryCommit,leads,gaps}`. A lead carries source key, matched text, match kind, source commit, support state and reason. Rank exact IRI, exact phrase, then token overlap with deterministic tie-breaks. A catalog concept match without a supported graph node stays a lead.

**Complete test file:**

```js
import test from 'node:test';
import assert from 'node:assert/strict';
import {makeModel} from './fixtures/c4/fixture-repos.mjs';
import {findCandidates} from '../src/c4/candidates.mjs';

test('description gives leads without an impact conclusion', () => {
  const {snapshot,registry} = makeModel();
  const r = findCandidates({snapshot,registry,query:'change Alpha API'});
  assert.ok(r.leads.some(x => x.key.sourceId === 'fixture/Alpha'));
  assert.ok(r.leads.every(x => !('impact' in x)));
});
test('exact IRI and no-match gap', () => {
  const {snapshot,registry,keys} = makeModel();
  assert.equal(findCandidates({snapshot,registry,exactIri:keys.alphaSystem.nodeIri})
    .leads[0].key.nodeIri,keys.alphaSystem.nodeIri);
  const empty = findCandidates({snapshot,registry,query:'unmatched phrase'});
  assert.equal(empty.leads.length,0);
  assert.ok(empty.gaps.length > 0);
});
```

Add catalog-only lead and equal-label distinct-source cases. Commit and push Task 4.

## Task 5 — directed paths

**Files:** `src/c4/paths.mjs`, `test/c4-paths.test.mjs`.

**Interface:** `readImpactPaths({snapshot,registry,starts,maxDepth=4})->{snapshotId,registryCommit,paths,gaps}`. A path contains ordered assertion and binding steps. Assertion steps retain subject/object keys, direction, assertion IRI, relationship, basis, evidence and commit. Binding steps retain both keys, registry revision and identity evidence. Bound depth to 1–6, avoid cycles and never synthesize a direct edge from two steps.

**Complete test file:**

```js
import test from 'node:test';
import assert from 'node:assert/strict';
import {makeModel} from './fixtures/c4/fixture-repos.mjs';
import {readImpactPaths} from '../src/c4/paths.mjs';

test('cross-source path has distinct inferred assertion and binding', () => {
  const {snapshot,registry,keys} = makeModel();
  const r = readImpactPaths({snapshot,registry,starts:[keys.alphaSystem],maxDepth:5});
  const p = r.paths.find(x => x.steps.some(s => s.type === 'binding'));
  assert.ok(p);
  assert.ok(p.steps.some(s => s.type === 'assertion' && s.basis === 'inferred'));
  assert.ok(p.steps.every(s => s.type === 'binding' || s.assertionIri));
});
test('equal labels without binding produce no cross-source step', () => {
  const {snapshot,registry,keys} = makeModel();
  const r = readImpactPaths({snapshot,registry:{...registry,bindings:[]},
    starts:[keys.externalBeta]});
  assert.ok(!r.paths.some(p => p.steps.some(s => s.type === 'binding')));
  assert.ok(r.gaps.length > 0);
});
```

Add stale binding, cycle and empty-coverage cases. Commit and push Task 5.

## Task 6 — C4 projection

**Files:** `src/c4/project.mjs`, `test/c4-project.test.mjs`.

**Interface:** `projectView({snapshot,registry,focus,level,supplement=null})->ViewModel`. The model has snapshot and registry identity, focus, level, boundary, sorted supported items/edges, next-focus links, evidence and gaps. System context may show evidenced external nodes without a source-inventory entry. Containers and components remain in their reviewed parent boundaries. Code can be unavailable while higher levels work. Missing node support or an empty level is a gap.

**Complete test file:**

```js
import test from 'node:test';
import assert from 'node:assert/strict';
import {makeModel} from './fixtures/c4/fixture-repos.mjs';
import {projectView} from '../src/c4/project.mjs';

test('all four levels retain focus and reviewed boundaries', () => {
  const {snapshot,registry,keys} = makeModel();
  for (const [level,focus] of [['context',keys.alphaSystem],
    ['container',keys.alphaSystem],['component',keys.alphaContainer],
    ['code',keys.alphaComponent]]) {
    const v = projectView({snapshot,registry,focus,level});
    assert.equal(v.level,level);
    assert.ok(v.boundary);
    assert.ok(v.items.every(x => x.supported && x.evidenceIris.length));
  }
});
test('missing code mapping is an explicit gap', () => {
  const {snapshot,registry,keys} = makeModel();
  const r = {...registry,mappings:registry.mappings.filter(x => x.level !== 'code')};
  const v = projectView({snapshot,registry:r,focus:keys.alphaComponent,level:'code'});
  assert.equal(v.items.length,0);
  assert.ok(v.gaps.length > 0);
});
```

Add external-context, next-level, unsupported-node and partial-source cases. Commit and push Task 6.

## Task 7 — pinned inspected code

**Files:** `src/c4/code-evidence.mjs`, `test/c4-code-evidence.test.mjs`; connect `src/c4/project.mjs`.

**Interface:** `verifyCodeSupplement({snapshot,focus,supplement})->VerifiedSupplement`. Supplement fields: source ID, repository-relative path, positive line range, blob ID, component key and architect reason. Verify against exact source commit and role selector; mark `basis:'architect-inspected'`. Reject missing path, wrong blob, wrong focus, out-of-range lines and unapproved deployment role. Do not write a graph fact.

**Complete test file:**

```js
import test from 'node:test';
import assert from 'node:assert/strict';
import {withPilotFixture} from './fixtures/c4/fixture-repos.mjs';
import {loadSnapshot} from '../src/c4/snapshot.mjs';
import {verifyCodeSupplement} from '../src/c4/code-evidence.mjs';

test('verified source line is visibly architect-inspected', async () => {
  await withPilotFixture(async f => {
    const result = verifyCodeSupplement({snapshot:loadSnapshot(f),
      focus:f.keys.alphaComponent,supplement:f.codeSupplement()});
    assert.equal(result.basis,'architect-inspected');
    assert.equal(result.commit,f.sourceCommits.alpha);
    assert.equal(result.path,'src/app.ts');
  });
});
test('wrong blob and line range fail', async () => {
  await withPilotFixture(async f => {
    const snapshot = loadSnapshot(f), good = f.codeSupplement();
    assert.throws(() => verifyCodeSupplement({snapshot,focus:f.keys.alphaComponent,
      supplement:{...good,blobId:'0'.repeat(40)}}),/blob/i);
    assert.throws(() => verifyCodeSupplement({snapshot,focus:f.keys.alphaComponent,
      supplement:{...good,endLine:9999}}),/line/i);
  });
});
```

Add a fixture with JointBooking's Terraform-only selector and prove a non-Terraform deployment citation fails. Commit and push Task 7.

## Task 8 — deterministic renderer

**Files:** `src/c4/render.mjs`, `test/c4-render.test.mjs`.

**Interface:** `renderView(view)->{json,markdown}`. JSON ends with one newline; Mermaid IDs use stable source-key hashes, not labels. Escape Markdown and Mermaid text. Diagram item IDs link to matching evidence-table anchors and commit-pinned source locations. Evidence table includes assertion, basis, repository, commit, path, lines and blob. Gap table always exists. A tentative legend distinguishes inferred edges and architect-inspected code.

**Complete test file:**

```js
import test from 'node:test';
import assert from 'node:assert/strict';
import {makeModel} from './fixtures/c4/fixture-repos.mjs';
import {projectView} from '../src/c4/project.mjs';
import {renderView} from '../src/c4/render.mjs';

test('same pinned view has byte-identical output and evidence table', () => {
  const {snapshot,registry,keys} = makeModel();
  const view = projectView({snapshot,registry,focus:keys.alphaSystem,level:'context'});
  const a = renderView(view), b = renderView(view);
  assert.deepEqual(a,b);
  assert.equal(JSON.parse(a.json).level,'context');
  assert.match(a.markdown,/mermaid/);
  assert.match(a.markdown,/Evidence/);
  assert.match(a.markdown,/Gaps/);
  assert.match(a.markdown,new RegExp(snapshot.id.catalogCommit));
});
```

Add source-link, inferred legend and special-character label tests. Commit and push Task 8.

## Task 9 — assessment and feedback

**Files:** `src/c4/assessment.mjs`, `src/c4/feedback.mjs`, `test/c4-assessment.test.mjs`, `test/c4-feedback.test.mjs`.

**Interfaces:** `newAssessment({requestId,description,snapshot,registry})`, `validateAssessment(record,{snapshot,registry,paths,views})`, `renderAssessment(record,context)->Markdown`, `renderFeedback(record,assessment)->Markdown`. Assessment JSON schema version 1 contains request, snapshot, registry digests, architect, confirmed/rejected leads, likely impacts with reasons/path and evidence IDs, and unresolved gaps/questions. All references resolve in the pinned context. Compute the assessment digest from its canonical JSON bytes after validation. Feedback JSON is separate, references that digest, and records architect/reviewer, perceived accuracy, completeness, confidence, discoveries, suspected misses, false leads, examples and open questions. Neither has approval status or numeric gate.

**Complete assessment test file:**

```js
import test from 'node:test';
import assert from 'node:assert/strict';
import {makeModel} from './fixtures/c4/fixture-repos.mjs';
import {newAssessment,validateAssessment,renderAssessment} from '../src/c4/assessment.mjs';

test('architect findings render with evidence and gaps', () => {
  const {snapshot,registry,keys,paths,views} = makeModel();
  const a = newAssessment({requestId:'CR-1',description:'Change Alpha API',snapshot,registry});
  a.architect = 'Architect A';
  a.confirmedLeads = [keys.alphaSystem];
  a.rejectedLeads = [{key:keys.externalBeta,reason:'Out of scope'}];
  a.likelyImpacts = [{key:keys.alphaComponent,reason:'Call path',
    pathIds:[paths[0].id],evidenceIris:[paths[0].steps[0].evidenceIris[0]]}];
  a.unresolved = [{code:'partial-code',reason:'No graph code coverage'}];
  validateAssessment(a,{snapshot,registry,paths,views});
  assert.match(renderAssessment(a,{snapshot,registry,paths,views}),/CR-1/);
  assert.equal('approved' in a,false);
});
test('stale snapshot fails validation', () => {
  const {snapshot,registry,paths,views} = makeModel();
  const a = newAssessment({requestId:'CR-1',description:'Change',snapshot,registry});
  a.snapshotId.catalogCommit = '0'.repeat(40);
  assert.throws(() => validateAssessment(a,{snapshot,registry,paths,views}),/snapshot/i);
});
```

**Complete feedback test file:**

```js
import test from 'node:test';
import assert from 'node:assert/strict';
import {renderFeedback} from '../src/c4/feedback.mjs';

test('separate qualitative feedback records misses and uncertainty', () => {
  const assessment = {requestId:'CR-1',digest:'a'.repeat(64)};
  const feedback = {schemaVersion:1,assessmentDigest:assessment.digest,
    architect:'Architect A',reviewer:'Reviewer B',perceivedAccuracy:'mostly accurate',
    perceivedCompleteness:'partial',confidence:'medium',
    usefulDiscoveries:['Beta dependency'],suspectedMisses:['data store'],
    falseLeads:['legacy API'],examples:['CR-1 path'],
    openQuestions:['Who owns the data store?']};
  const markdown = renderFeedback(feedback,assessment);
  assert.match(markdown,/suspected misses/i);
  assert.match(markdown,/data store/);
  assert.equal('approvalStatus' in feedback,false);
});
```

Add stale path/evidence and feedback-digest mismatch tests. Commit and push Task 9.

## Task 10 — CLI pilot flow

**Files:** `src/c4/cli.mjs`, `test/c4-cli.test.mjs`, `package.json`, `README.md`.

**Interface:** `runC4Cli(argv,{cwd,stdout,stderr})->Promise<number>`. Parse shared flags and command-specific flags exactly as above. All assessment and feedback commands also take the shared snapshot, registry, checkout and output flags so references can be revalidated. Write stable filenames under explicit output directory only after validation succeeds. No command edits `dist/`, a source checkout, registry inputs or assessment input JSON.

**Complete test file:**

```js
import test from 'node:test';
import assert from 'node:assert/strict';
import {existsSync,readFileSync} from 'node:fs';
import {join} from 'node:path';
import {withPilotFixture} from './fixtures/c4/fixture-repos.mjs';
import {runC4Cli} from '../src/c4/cli.mjs';

test('description, paths and four views run at one snapshot', async () => {
  await withPilotFixture(async f => {
    const common = ['--catalog-commit',f.catalogCommit,'--registry-commit',f.registryCommit,
      ...[...f.checkouts].flatMap(([id,path]) => ['--checkout',`${id}=${path}`]),
      '--output',f.outputDir];
    assert.equal(await runC4Cli(['search',...common,'--query','Alpha API'],
      {cwd:f.divisionRoot}),0);
    assert.equal(await runC4Cli(['paths',...common,'--start',
      `fixture/Alpha=${f.keys.alphaSystem.nodeIri}`],{cwd:f.divisionRoot}),0);
    for (const [level,focus] of [['context',f.keys.alphaSystem],
      ['container',f.keys.alphaSystem],['component',f.keys.alphaContainer],
      ['code',f.keys.alphaComponent]]) {
      assert.equal(await runC4Cli(['view',...common,'--level',level,'--focus',
        `${focus.sourceId}=${focus.nodeIri}`],{cwd:f.divisionRoot}),0);
    }
    assert.ok(existsSync(join(f.outputDir,'view-context.json')));
    assert.match(readFileSync(join(f.outputDir,'view-context.md'),'utf8'),/Evidence/);
  });
});
```

Add end-to-end `assessment-init`, `assessment-render`, `feedback-render`, absent optional graph, newer source revision, invalid flag, and no-output-on-error cases. Test across C#, TypeScript, Java and Python fixture source files; for JointBooking, use only Terraform deployment evidence. Commit and push Task 10.

## Final verification and feature handoff

| Behaviours | Tasks |
| --- | --- |
| B1 | 1, 2, 4, 10 |
| B2 | 2, 3, 5, 10 |
| B3–B4 | 8–10 |
| B5–B9 | 3, 6–8, 10 |
| B10–B12 | 1–3, 5–8 |
| B13–B15 | 1–3, 6, 10 |

Before the build PR, run `npm test` and review staged diff. If builder inputs changed, run `npm run build` and `npm run check` against clean source checkouts, inspect generated catalog and source commits, and keep `dist/` out of the authored PR. Real JointBooking paths remain unavailable until that source publishes its graph and the catalog pins the new commit; do not invent source data or mappings in this repository.
