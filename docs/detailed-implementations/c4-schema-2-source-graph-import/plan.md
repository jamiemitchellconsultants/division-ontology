# C4 schema 2 source graph import — detailed implementation plan

**Inputs:** [Approved feature design](../../features/c4-schema-2-source-graph-import/feature.md) and ADRs [0024](../../adr/0024-version-source-graph-validation-at-the-c4-boundary.md), [0025](../../adr/0025-preserve-design-provenance-in-c4-snapshots.md) and [0026](../../adr/0026-record-the-actual-source-graph-version-in-workspace-bundles.md). [Task overview](../../superpowers/plans/c4-schema-2-source-graph-import/plan.md)

All commands in this plan run on **James's Mac, Terminal**, from the isolated division-ontology checkout unless a command changes directory. Start the build from merged main on `feature/c4-schema-2-source-graph-import/build` after confirming the plan PR merge and the Dev Build gate. Install locked Node dependencies and use the SDK in `runtime/global.json`. The tests use temporary Git repositories or in-memory bundle objects. The checked-in catalog and source data stay outside the staged changes.

In each task, run the named cases before the change and see the expected failure. Run the same cases after the change, stage only the listed paths, review the staged file list and full diff, run any installed ontology lint and the full Node suite, validate the disposable catalog as described below, then commit and push. The tests in the code blocks are the planned changes for the build phase.

## Task 1 — Validate and preserve C4 source graphs

**Files:** Modify `src/c4/graph.mjs`. Create `test/c4-schema-compatibility.test.mjs` and `test/c4-design-records.test.mjs`. Reuse `test/fixtures/c4/fixture-repos.mjs`, `src/c4/git.mjs` and the real `loadSnapshot` entry point.

**Interfaces:** Keep `loadSourceGraph({ checkout, commit, config })`. An available graph state adds numeric `schemaVersion`. The existing maps retain their keys and ordinary record fields. A behaviour or decision node adds `designStatus` as a full IRI and `evidenceIris` as an array containing the one resolved selected-document reference. The evidence role is the normalized selector key. The C4 fixture's small committed catalog is test data. Use the real catalog builder for the representative and trial verification.

- [ ] **1.1 Add the complete compatibility test file below.** It pins schema 1 aliases, standard roles in both versions, rejected manifest declarations and schema 2 constructs under schema 1. The existing C4 tests remain the controls for optional graphs, coverage, blob checks, parser failure and deployment eligibility.

```js
import test from 'node:test';
import assert from 'node:assert/strict';
import { symlinkSync } from 'node:fs';
import { join } from 'node:path';
import { withPilotFixture } from './fixtures/c4/fixture-repos.mjs';
import { loadSourceGraph } from '../src/c4/graph.mjs';

const source = 'fixture/Alpha';
const kit = 'https://ontology-kit.dev/schema#';
const ns = 'https://alpha.example/#';
const manifestPath = 'graph/manifest.ttl';
const roleKeys = ['implementation', 'test', 'selected-document', 'ontology', 'deployment'];
const versionPredicate = '<' + kit + 'schemaVersion>';
const rolePredicate = '<' + kit + 'role>';
const versionOne = versionPredicate + ' "1".';

function changed(f, change, evidence = { implementation: ['src/**'] }) {
  const turtle = f.readSource(source, manifestPath);
  assert.ok(turtle.includes(versionOne));
  f.writeSource(source, manifestPath, change(turtle));
  const config = { sourceGraph: { enabled: true, root: 'graph', evidence } };
  f.writeSource(source, 'ontology.config.json', JSON.stringify(config));
  return { checkout: f.checkouts.get(source), commit: f.commitSource(source, 'Graph under test'), config };
}

for (const version of [1, 2]) {
  for (const key of roleKeys) {
    test('schema ' + version + ' accepts the standard ' + key + ' role', () => withPilotFixture(f => {
      const input = changed(f, turtle => turtle
        .replace(versionOne, versionPredicate + ' ' + version + '.')
        .replace(rolePredicate + ' <' + kit + 'implementation>', rolePredicate + ' <' + kit + 'role-' + key + '>'),
      { [key]: ['src/app.ts'] });
      const graph = loadSourceGraph(input);
      assert.equal(graph.schemaVersion, version);
      assert.equal(graph.evidence.get(ns + 'evidence').role, key);
      assert.equal(graph.assertions.get(ns + 'assertion').basis, 'inferred');
      assert.equal(graph.coverage[0].selector, 'src/**');
    }));
  }
}

for (const key of roleKeys) {
  test('schema 1 keeps the legacy ' + key + ' selector', () => withPilotFixture(f => {
    const input = changed(f, turtle => turtle.replace(rolePredicate + ' <' + kit + 'implementation>',
      rolePredicate + ' <' + kit + key + '>'), { [key]: ['src/app.ts'] });
    const graph = loadSourceGraph(input);
    assert.equal(graph.schemaVersion, 1);
    assert.equal(graph.evidence.get(ns + 'evidence').role, key);
  }));
}

for (const [name, transform] of [
  ['version 0', text => text.replace(versionOne, versionPredicate + ' 0.')],
  ['version 3', text => text.replace(versionOne, versionPredicate + ' 3.')],
  ['missing version', text => text.replace(';\n    ' + versionOne, '.')],
  ['conflicting versions', text => text.replace(versionOne, versionPredicate + ' 1, 2.')],
  ['multiple manifests', text => text + '\n<urn:other-manifest> a <' + kit + 'SourceKnowledgeGraph> ; ' + versionPredicate + ' 1.\n'],
]) {
  test('rejects ' + name, () => withPilotFixture(f => {
    const input = changed(f, transform);
    assert.throws(() => loadSourceGraph(input), /schema|manifest/i);
  }));
}

const appendQuad = (subject, property, object) => '\n<' + ns + subject + '> <' + kit + property + '> ' + object + ' .\n';
for (const [name, transform] of [
  ['behaviour kind', text => text.replace('<' + kit + 'source-symbol>', '<' + kit + 'behaviour>')],
  ['decision kind', text => text.replace('<' + kit + 'source-symbol>', '<' + kit + 'decision>')],
  ['node design status', text => text + appendQuad('code', 'designStatus', '<' + kit + 'current>')],
  ['node evidence', text => text + appendQuad('code', 'evidence', '<' + ns + 'evidence>')],
  ['replacement', text => text.replace('<' + kit + 'communicates-with>', '<' + kit + 'replaced-by>')],
  ['succession', text => text.replace('<' + kit + 'communicates-with>', '<' + kit + 'superseded-by>')],
]) {
  test('rejects ' + name + ' under schema 1', () => withPilotFixture(f => {
    const input = changed(f, transform);
    assert.throws(() => loadSourceGraph(input), /schema|version/i);
  }));
}

test('schema 2 rejects a legacy role', () => withPilotFixture(f => {
  const input = changed(f, text => text.replace(versionOne, versionPredicate + ' 2.'));
  assert.throws(() => loadSourceGraph(input), /role selector/i);
}));

for (const [iri, eligible] of [
  [kit + 'role-implementation', { implementation: ['test/**'] }],
  [kit + 'role-test', { implementation: ['src/app.ts'] }],
  ['https://other.example/#role-implementation', { implementation: ['src/app.ts'] }],
]) {
  test('rejects role eligibility for ' + iri + ' under ' + Object.keys(eligible).join(), () => withPilotFixture(f => {
    const input = changed(f, text => text.replace(rolePredicate + ' <' + kit + 'implementation>',
      rolePredicate + ' <' + iri + '>'), eligible);
    assert.throws(() => loadSourceGraph(input), /role selector/i);
  }));
}

const pathPredicate = '<' + kit + 'path>';
for (const [name, transform, error] of [
  ['unsafe evidence path', text => text.replace(pathPredicate + ' "src/app.ts"', pathPredicate + ' "../elsewhere.ts"'), /unsafe repository path/i],
  ['missing evidence file', text => text.replace(pathPredicate + ' "src/app.ts"', pathPredicate + ' "src/missing.ts"'), /evidence path missing/i],
  ['invalid line range', text => text.replace('<' + kit + 'endLine> "1"', '<' + kit + 'endLine> "2"'), /evidence line range/i],
  ['uncovered evidence', text => text.replace('<' + kit + 'pathSelector> "src/**"', '<' + kit + 'pathSelector> "covered/**"'), /evidence path is not covered/i],
]) {
  test('rejects ' + name + ' with otherwise valid input', () => withPilotFixture(f => {
    const input = changed(f, transform);
    assert.throws(() => loadSourceGraph(input), error);
  }));
}

test('rejects a symlink as evidence', () => withPilotFixture(f => {
  symlinkSync('app.ts', join(f.checkouts.get(source), 'src/link.ts'));
  const input = changed(f, text => text.replace(pathPredicate + ' "src/app.ts"', pathPredicate + ' "src/link.ts"'));
  assert.throws(() => loadSourceGraph(input), /unsafe non-file path/i);
}));

```

- [ ] **1.2 Add the complete design record test file below.** This uses a second Turtle shard and one real C4 snapshot over its committed pin. The clean source checkout advances its selected-document path and selector after the catalog is pinned.

```js
import test from 'node:test';
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { withPilotFixture } from './fixtures/c4/fixture-repos.mjs';
import { loadSnapshot } from '../src/c4/snapshot.mjs';

const source = 'fixture/Alpha';
const kit = 'https://ontology-kit.dev/schema#';
const ns = 'https://alpha.example/#';
const litePath = 'docs/features/payments/feature.md';
const fullPath = 'docs/features/payments/02-behaviours.md';
const declaration = '# Payments\n\n**B1 — Confirm payment.**\n';
const arc = (id, from, relationship, to, basis = 'direct') =>
  ':' + id + ' a kit:GraphAssertion ; kit:subject :' + from + ' ; kit:object :' + to +
  ' ; kit:relationship kit:' + relationship + ' ; kit:basis kit:' + basis + ' ; kit:evidence :document .\n';

function writeDesign(f, path, mutate = turtle => turtle) {
  const config = { sourceGraph: { enabled: true, root: 'graph',
    evidence: { implementation: ['src/**'], 'selected-document': [path] } } };
  f.writeSource(source, 'ontology.config.json', JSON.stringify(config));
  f.writeSource(source, path, declaration);
  const bytes = Buffer.from(declaration);
  const blobId = createHash('sha1').update('blob ' + bytes.length + '\0').update(bytes).digest('hex');
  const graph = f.readSource(source, 'graph/manifest.ttl');
  f.writeSource(source, 'graph/manifest.ttl', graph
    .replace('<' + kit + 'schemaVersion> "1"', '<' + kit + 'schemaVersion> 2')
    .replace('<' + kit + 'role> <' + kit + 'implementation>', '<' + kit + 'role> <' + kit + 'role-implementation>'));
  const design = [
    '@prefix : <' + ns + '> .',
    '@prefix kit: <' + kit + '> .',
    '@prefix rdfs: <http://www.w3.org/2000/01/rdf-schema#> .',
    ':documents a kit:GraphCoverage ; kit:pathSelector ' + JSON.stringify(path) + ' ; kit:priority 1 ; kit:status kit:covered .',
    ':document a kit:GraphEvidence ; kit:path ' + JSON.stringify(path) +
      ' ; kit:startLine 3 ; kit:endLine 3 ; kit:blobId "' + blobId + '" ; kit:role kit:role-selected-document .',
    ':behaviour a kit:GraphNode ; kit:kind kit:behaviour ; rdfs:label "payments#B1 Confirm payment" ; kit:designStatus kit:current ; kit:evidence :document .',
    ':retired a kit:GraphNode ; kit:kind kit:behaviour ; rdfs:label "payments#B2 Old flow" ; kit:designStatus kit:retired ; kit:evidence :document .',
    ':decision a kit:GraphNode ; kit:kind kit:decision ; rdfs:label "ADR 0001 Routing" ; kit:designStatus kit:accepted ; kit:evidence :document .',
    ':old-decision a kit:GraphNode ; kit:kind kit:decision ; rdfs:label "ADR 0002 Old routing" ; kit:designStatus kit:superseded ; kit:evidence :document .',
    ':proposal a kit:GraphNode ; kit:kind kit:decision ; rdfs:label "ADR 0003 Proposal" ; kit:designStatus kit:proposed ; kit:evidence :document .',
    ':deprecated a kit:GraphNode ; kit:kind kit:decision ; rdfs:label "ADR 0004 Deprecated" ; kit:designStatus kit:deprecated ; kit:evidence :document .',
    ':test a kit:GraphNode ; kit:kind kit:test ; rdfs:label "tests/payment.js" .',
    ':unlinked a kit:GraphNode ; kit:kind kit:behaviour ; rdfs:label "payments#B3 Unlinked" ; kit:designStatus kit:current ; kit:evidence :document .',
    ':empty-component a kit:GraphNode ; kit:kind kit:component ; rdfs:label "Empty handler" .',
    ':partial a kit:GraphNode ; kit:kind kit:behaviour ; rdfs:label "payments#B4 Partial" ; kit:designStatus kit:current ; kit:evidence :document .',
    arc('direct', 'code', 'implements', 'behaviour'),
    arc('via', 'component', 'implements', 'behaviour'),
    arc('partial-impl', 'empty-component', 'implements', 'partial'),
    arc('old-impl', 'code', 'implements', 'retired'),
    arc('test-link', 'behaviour', 'tested-by', 'test'),
    arc('mentions', 'component', 'documented-by', 'old-decision', 'inferred'),
    arc('replacement', 'retired', 'replaced-by', 'behaviour'),
    arc('succession', 'old-decision', 'superseded-by', 'decision'),
  ].join('\n');
  f.writeSource(source, 'graph/design.ttl', mutate(design));
  return { pin: f.commitSource(source, 'Design graph'), blobId };
}

function catalogAt(f, pin) {
  const catalog = JSON.parse(f.readDivision('dist/catalog.json'));
  catalog.sources.find(row => row.id === source).commit = pin;
  f.writeDivision('dist/catalog.json', JSON.stringify(catalog));
  return { ...f, catalogCommit: f.commitDivision('Pin C4 test source') };
}

function replace(text, needle, value) {
  assert.ok(text.includes(needle));
  return text.replace(needle, value);
}

function routes(graph, target) {
  const edges = [...graph.assertions.values()];
  const found = [];
  for (const edge of edges.filter(item => item.object === target && item.relationship === kit + 'implements')) {
    const kind = graph.nodes.get(edge.subject).kind;
    if (kind === kit + 'source-symbol') found.push([edge.iri, edge.subject]);
    if (kind === kit + 'component') {
      for (const containment of edges.filter(item => item.subject === edge.subject && item.relationship === kit + 'contains')) {
        if ([kit + 'source-file', kit + 'source-symbol'].includes(graph.nodes.get(containment.object).kind)) {
          found.push([edge.iri, edge.subject, containment.iri, containment.object]);
        }
      }
    }
  }
  return found.sort((a, b) => JSON.stringify(a).localeCompare(JSON.stringify(b)));
}

test('preserves design provenance and both routes from the pinned config and graph', () => withPilotFixture(f => {
  const { pin, blobId } = writeDesign(f, litePath);
  const input = catalogAt(f, pin);
  const later = writeDesign(f, fullPath, text => replace(text, 'payments#B1 Confirm payment', 'payments#B1 Reworded payment'));
  assert.notEqual(later.pin, pin);
  const imported = loadSnapshot(input).sources.get(source);
  assert.equal(imported.commit, pin);
  const graph = imported.graphState;
  assert.equal(graph.schemaVersion, 2);
  for (const [id, kind, label, status] of [
    ['behaviour', 'behaviour', 'payments#B1 Confirm payment', 'current'],
    ['retired', 'behaviour', 'payments#B2 Old flow', 'retired'],
    ['decision', 'decision', 'ADR 0001 Routing', 'accepted'],
    ['old-decision', 'decision', 'ADR 0002 Old routing', 'superseded'],
    ['proposal', 'decision', 'ADR 0003 Proposal', 'proposed'],
    ['deprecated', 'decision', 'ADR 0004 Deprecated', 'deprecated'],
  ]) {
    const node = graph.nodes.get(ns + id);
    assert.deepEqual([node.iri, node.kind, node.label, node.designStatus, node.evidenceIris],
      [ns + id, kit + kind, label, kit + status, [ns + 'document']]);
  }
  assert.equal(Object.hasOwn(graph.nodes.get(ns + 'code'), 'designStatus'), false);
  assert.equal(Object.hasOwn(graph.nodes.get(ns + 'code'), 'evidenceIris'), false);
  for (const [id, from, relationship, to, basis = 'direct'] of [
    ['direct', 'code', 'implements', 'behaviour'],
    ['via', 'component', 'implements', 'behaviour'],
    ['old-impl', 'code', 'implements', 'retired'],
    ['test-link', 'behaviour', 'tested-by', 'test'],
    ['mentions', 'component', 'documented-by', 'old-decision', 'inferred'],
    ['replacement', 'retired', 'replaced-by', 'behaviour'],
    ['succession', 'old-decision', 'superseded-by', 'decision'],
  ]) assert.deepEqual(graph.assertions.get(ns + id), { iri: ns + id, subject: ns + from, object: ns + to,
    relationship: kit + relationship, basis, evidenceIris: [ns + 'document'] });
  assert.deepEqual(graph.evidence.get(ns + 'document'), { iri: ns + 'document',
    path: litePath, startLine: 3, endLine: 3, blobId, role: 'selected-document' });
  assert.ok(graph.coverage.some(row => row.selector === litePath && row.status === 'covered'));
  assert.deepEqual(routes(graph, ns + 'behaviour'), [[ns + 'direct', ns + 'code'],
    [ns + 'via', ns + 'component', ns + 'contains3', ns + 'code']]);
  assert.deepEqual(routes(graph, ns + 'unlinked'), []);
  assert.deepEqual(routes(graph, ns + 'partial'), []);
}));

for (const [name, needle, value, error] of [
  ['missing status', ' ; kit:designStatus kit:current', '', /designStatus/i],
  ['unknown status', 'kit:designStatus kit:current', 'kit:designStatus kit:unknown', /designStatus|status/i],
  ['conflicting status', 'kit:designStatus kit:current', 'kit:designStatus kit:current, kit:retired', /designStatus|status/i],
  ['wrong-kind status', 'kit:designStatus kit:current', 'kit:designStatus kit:accepted', /designStatus|status/i],
  ['missing evidence', 'kit:designStatus kit:current ; kit:evidence :document', 'kit:designStatus kit:current', /evidence/i],
  ['multiple evidence', 'kit:designStatus kit:current ; kit:evidence :document', 'kit:designStatus kit:current ; kit:evidence :document, :evidence', /evidence/i],
  ['literal evidence', 'kit:designStatus kit:current ; kit:evidence :document', 'kit:designStatus kit:current ; kit:evidence "document"', /evidence/i],
  ['unknown evidence', 'kit:designStatus kit:current ; kit:evidence :document', 'kit:designStatus kit:current ; kit:evidence :unknown', /evidence/i],
  ['wrong evidence role', 'kit:designStatus kit:current ; kit:evidence :document', 'kit:designStatus kit:current ; kit:evidence :evidence', /evidence|selected-document/i],
  ['design metadata on an ordinary node', 'rdfs:label "tests/payment.js"', 'rdfs:label "tests/payment.js" ; kit:designStatus kit:current ; kit:evidence :document', /design|status|evidence/i],
]) {
  test('rejects ' + name, () => withPilotFixture(f => {
    const { pin } = writeDesign(f, litePath, text => replace(text, needle, value));
    assert.throws(() => loadSnapshot(catalogAt(f, pin)), error);
  }));
}
```

- [ ] **1.3 Run the red suites.** Run `node --test test/c4-schema-compatibility.test.mjs test/c4-design-records.test.mjs`. Expect standard schema 1 to fail on role resolution, valid schema 2 to fail on the manifest guard, legacy cases to lack the version field, and schema 1 to accept some of the prohibited constructs. These failures exercise the current loader. If a fixture mutation fails before import, correct the fixture first.

- [ ] **1.4 Implement the approved boundary in `src/c4/graph.mjs`.** Keep the function signature and parser. Resolve a single manifest with supported version 1 or 2 and preserve plain literal 1 in legacy fixtures. Return the version as a number for available graphs. Map the exact five standard kit role IRIs to the selector keys named in ADR 0024 and allow the corresponding short legacy role IRIs only in schema 1. An IRI outside that map fails before selector evaluation. Use the evidence role selector diagnostic for both unknown and ineligible roles. Keep selector, file, blob, range and coverage validation against the supplied pinned configuration. Keep optional graph results and their reasons.

- [ ] **1.5 Validate design records in the same increment.** Reject the two design kinds, design status, node evidence and the replacement and succession predicates where used by version 1 records. In version 2, read the two new fields only on behaviour and decision nodes. Validate status as one named IRI for that kind and require exactly one named evidence pointer. Resolve it against the validated evidence map and require the selected-document role. Keep assertion endpoint, basis and evidence validation, including on replacement and succession assertions. Return the existing maps with the two approved additive fields on design nodes. Keep errors actionable by identifying the invalid kind, node, evidence, version or role. Reuse the existing C4 snapshot and pinned Git access.

- [ ] **1.6 Verify and commit.** Run the command in 1.3 and then `node --test test/c4-*.test.mjs`. Expect all to pass. Stage exactly `src/c4/graph.mjs`, `test/c4-schema-compatibility.test.mjs` and `test/c4-design-records.test.mjs`, follow the staged verification and disposable catalog check below, then commit `feat(c4): import pinned schema 2 source graphs (#175)`. Push the build branch.

## Task 2 — Keep the workspace bundle producer and reader compatible

**Files:** Modify `src/workspace/bundle.mjs` and `runtime/RuntimeKnowledge.Store/BundleReader.cs`. Append one test to `test/workspace-bundle.test.mjs` and add `runtime/RuntimeKnowledge.Tests/BundleGraphSchemaTests.cs`.

**Interfaces:** Consume Task 1's numeric `graphState.schemaVersion` when available. Keep `buildBundle({ divisionRoot, catalogCommit, registryCommit, checkouts })`, `BundleReader.ReadAsync(string id, CancellationToken cancellationToken)` and the existing `SourceRevision` contract. The per-source field is independent of the bundle envelope version.

- [ ] **2.1 Append the complete producer case to the existing workspace bundle suite.** Use its existing imports, fixture, publisher, commit helper and projection reader.

```js
test('bundles mixed graph versions under the existing envelope', () => withFixture(async args => {
  const checkout = args.checkouts.get('fixture/ecommerce');
  const graphFile = join(checkout, 'graph/manifest.ttl');
  const turtle = await readFile(graphFile, 'utf8');
  const schemaOne = '#schemaVersion> "1"';
  const legacyRole = '#implementation>';
  assert.ok(turtle.includes(schemaOne) && turtle.includes(legacyRole));
  await writeFile(graphFile, turtle.replace(schemaOne, '#schemaVersion> 2')
    .replace(legacyRole, '#role-implementation>'));
  const upgradedSource = commit(checkout, 'Publish version 2 fixture graph');
  const absentCheckout = args.checkouts.get('fixture/warehouse');
  await rm(join(absentCheckout, 'ontology.config.json'));
  commit(absentCheckout, 'Disable optional fixture graph');

  const bundle = buildBundle(await republish(args));
  assert.equal(bundle.manifest.schemaVersion, 1);
  const sources = new Map(bundle.manifest.sources.map(source => [source.id, source]));
  assert.equal(sources.get('fixture/ecommerce').commit, upgradedSource);
  assert.equal(sources.get('fixture/ecommerce').graphSchemaVersion, 2);
  assert.equal(sources.get('fixture/accounting').graphSchemaVersion, 1);
  assert.equal(sources.get('fixture/warehouse').graphStatus, 'unavailable');
  assert.equal(sources.get('fixture/warehouse').graphSchemaVersion, null);
  assert.ok(bundle.manifest.files.some(file => file.sourceId === 'fixture/ecommerce' && file.role === 'graphTurtle'));
  assert.ok(published(bundle).entities.some(entity => entity.key.sourceId === 'fixture/ecommerce' && entity.key.kind === 'node'));
  assert.ok(published(bundle).edges.some(edge => edge.provenance.evidence.some(evidence => evidence.role === 'implementation')));
}));
```

- [ ] **2.2 Add the complete runtime reader test file below.** The current `MemoryBundles` and Store's `InternalsVisibleTo` already permit reading the real bundle in a database-free test. Rewrite only the synthetic manifest with a fresh digest and keep its referenced objects valid.

```csharp
using System.Security.Cryptography;
using System.Text.Json;
using RuntimeKnowledge.Contracts;
using RuntimeKnowledge.Store;
using Xunit;

namespace RuntimeKnowledge.Tests;

public sealed class BundleGraphSchemaTests
{
    [Theory]
    [InlineData("available", 1)]
    [InlineData("available", 2)]
    [InlineData("unavailable", null)]
    public async Task AcceptsSupportedGraphMetadata(string state, int? version)
    {
        var (objects, id) = await WithGraphVersion(state, version);
        var bundle = await new BundleReader(objects).ReadAsync(id, CancellationToken.None);
        Assert.Equal(1, bundle.Manifest.SchemaVersion);
        Assert.Equal(state, bundle.Manifest.Sources[0].GraphStatus);
        Assert.Equal(version, bundle.Manifest.Sources[0].GraphSchemaVersion);
        Assert.Equal(1, bundle.Manifest.Sources[1].GraphSchemaVersion);
    }

    [Theory]
    [InlineData("available", 0)]
    [InlineData("available", 3)]
    [InlineData("available", null)]
    [InlineData("unavailable", 1)]
    [InlineData("unavailable", 2)]
    public async Task RejectsInvalidGraphMetadata(string state, int? version)
    {
        var (objects, id) = await WithGraphVersion(state, version);
        var error = await Assert.ThrowsAsync<WorkspaceException>(() =>
            new BundleReader(objects).ReadAsync(id, CancellationToken.None));
        Assert.Equal("invalidBundle", error.Code);
    }

    private static async Task<(MemoryBundles Objects, string Id)> WithGraphVersion(string state, int? version)
    {
        var cancellation = CancellationToken.None;
        var objects = new MemoryBundles();
        var originalId = objects.AddSnapshot("initial");
        await using var original = await objects.OpenAsync("manifests/" + originalId + ".json", cancellation);
        var manifest = (await JsonSerializer.DeserializeAsync<BundleManifest>(
            original, WorkspaceJson.Options, cancellation))!;
        var sources = manifest.Sources.ToArray();
        sources[0] = sources[0] with { GraphStatus = state, GraphSchemaVersion = version };
        var bytes = JsonSerializer.SerializeToUtf8Bytes(manifest with { Sources = sources }, WorkspaceJson.Options);
        var id = Convert.ToHexStringLower(SHA256.HashData(bytes));
        using var data = new MemoryStream(bytes);
        await objects.PutImmutableAsync("manifests/" + id + ".json", data, cancellation);
        return (objects, id);
    }
}
```

- [ ] **2.3 Run both red checks.** Run `node --test test/workspace-bundle.test.mjs`. Expect the mixed version case to report 1 for the upgraded source. Run `(cd runtime && dotnet test RuntimeKnowledge.Tests/RuntimeKnowledge.Tests.csproj --filter FullyQualifiedName~BundleGraphSchemaTests --nologo)`. Expect the available version 2 case to fail with an invalid source revision. Both failure messages refer to the old metadata contract.

- [ ] **2.4 Implement the two-part bundle fix.** In the existing source loop in `buildBundle`, use `graphState.schemaVersion` for an available source and null for an unavailable source. In `BundleReader.ValidateManifest`, allow 1 or 2 for available sources, and retain null-only for unavailable sources. Keep envelope version 1, digest and file validation, existing projection, and public types and HTTP contracts.

- [ ] **2.5 Verify and commit.** Run both focused commands again, expecting all tests to pass. Run `(cd runtime && dotnet test RuntimeKnowledge.Tests/RuntimeKnowledge.Tests.csproj --filter Category=OpenApi --nologo)` and verify `git diff --exit-code -- runtime/openapi`. Stage exactly the four Task 2 paths and follow the staged verification and disposable catalog check below. Commit `fix(workspace): retain source graph versions in bundles (#175)` and push the build branch.

## Staged verification for both commits

- [ ] Stage the task's explicit files, review `git diff --cached --name-only` and `git diff --cached`, and run `git diff --cached --check`.
- [ ] Run `node scripts/check-ontology-terms.mjs` if installed and `npm test` in the foreground. Expect no failures.
- [ ] Build and check the representative catalog from a new scratch location with the current branch's locked dependencies. In the following command `importer` is the absolute current division checkout root.

```sh
importer=$(pwd -P)
validation=$(mktemp -d /private/tmp/division-c4-175-catalog.XXXXXX)
DEMO_DIR="$validation/demo" npm run --silent demo:setup
(
  cd "$validation/demo/division" || exit
  node "$importer/src/cli.mjs" build \
    --checkout "fixture/ecommerce=$validation/demo/ecommerce" \
    --checkout "fixture/warehouse=$validation/demo/warehouse" \
    --checkout "fixture/accounting=$validation/demo/accounting" &&
  node "$importer/src/cli.mjs" check \
    --checkout "fixture/ecommerce=$validation/demo/ecommerce" \
    --checkout "fixture/warehouse=$validation/demo/warehouse" \
    --checkout "fixture/accounting=$validation/demo/accounting" &&
  git diff --exit-code -- dist
)
jq -r '.sources[] | [.id,.commit] | @tsv' "$validation/demo/division/dist/catalog.json"
```

Expect the same three source pins and a clean generated diff. Review the catalog and its pins. Keep the generated fixture files in the scratch checkout. Push each passing commit with `git push -u origin feature/c4-schema-2-source-graph-import/build`.

## Final pinned verification

- [ ] Start with a clean committed importer checkout. Reuse the four disposable inputs created during the compatibility investigation when available at `/private/tmp/internalontology-185-b23-pinned.4gSEzD`. Their JSON files are `dummy-v1.json`, `kit-v1.json`, `local-b23.json` and `deployed-b23.json`. Rebind only `importerCheckout` and `importerCommit` to the candidate. Keep the source and catalog identities fixed.
```sh
inputs=/private/tmp/internalontology-185-b23-pinned.4gSEzD
rebound=$(mktemp -d /private/tmp/division-c4-175-pins.XXXXXX)
importer=$(pwd -P)
candidate=$(git rev-parse HEAD)
for fixture in dummy-v1 kit-v1 local-b23 deployed-b23; do
  jq --arg checkout "$importer" --arg commit "$candidate" \
    '.importerCheckout = $checkout | .importerCommit = $commit' \
    "$inputs/$fixture.json" > "$rebound/$fixture.json" || exit 1
done
```

- [ ] If the scratch inputs need rebuilding, use the three-source representative generator and the procedure in [InternalOntology's B23 runbook](https://github.com/jamiemitchellconsultants/InternalOntology/blob/5df7c1f90ed2769c3926c58d19e79e0c03bc4e07/docs/features/graph-feature-document-links/feature.md#build-the-disposable-b23-catalog). For the kit schema 1 control, put Widgets at the fixture's `schema-1` tag when building its catalog. For the synthetic schema 2 check, build at `linked` and leave Widgets at `promoted` after committing the generated catalog. For the deployed check register `jamiemitchellconsultants/JEPA-Speech` in a fresh disposable catalog with its ontology at `docs/ontology.ttl`, namespace `https://ontology.example/jepa-speech#`, source commit `3bcf7e7f5926e42231ae5dbfd9275f8166cc952c`, and behaviour reference `zener-shape-tracing-motor-test#B18`. Use a clean isolated checkout with the repository's GitHub origin and that commit available.
- [ ] Load the representative and kit schema 1 controls through the real C4 entry point. Expect graphs available for all configured sources and the existing version 1 meaning. The investigation supplies `load-probe.mjs` for this read. The equivalent command below also runs without the investigation's probe script. Set `B23_INPUT` to each rebound schema 1 JSON path in turn.

```sh
node --input-type=module - "$B23_INPUT" <<'JS'
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { pathToFileURL } from 'node:url';
const input = JSON.parse(readFileSync(process.argv[2], 'utf8'));
const { loadSnapshot } = await import(pathToFileURL(join(input.importerCheckout, 'src/c4/snapshot.mjs')).href);
const snapshot = loadSnapshot({ divisionRoot: input.divisionRoot, catalogCommit: input.catalogCommit,
  checkouts: new Map(Object.entries(input.checkouts)) });
assert.equal(snapshot.id.catalogCommit, input.catalogCommit);
assert.equal(snapshot.sources.get(input.sourceId).commit, input.sourceCommit);
for (const source of snapshot.sources.values()) {
  assert.equal(source.graphState.status, 'available');
  assert.equal(source.graphState.schemaVersion, 1);
  console.log(source.id, source.commit, source.graphState.schemaVersion, source.graphState.evidence.size);
}
JS
```

- [ ] Run the existing executable comparison from the InternalOntology checkout at merge `5df7c1f90ed2769c3926c58d19e79e0c03bc4e07` with `B23_SNAPSHOT` set first to the rebound `local-b23.json`, then to `deployed-b23.json`. Run `node --test tests/design-links-division-acceptance.test.mjs` for each. Expect one passing test per run. The test checks all design records and their incident assertions, the cited evidence, and reachable code at the exact catalog pin.
- [ ] Repeat the Node comparison in network-disabled Docker if used for the trial. Mount only the prepared repositories and inputs read-only at the same absolute paths the JSON contains. Keep importer, catalog and source commit IDs and version and test results for the User phase. These are build verification results. The owner walkthrough and executable approval remain in USER.
