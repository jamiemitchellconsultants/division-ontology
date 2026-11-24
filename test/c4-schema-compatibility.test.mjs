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
  ['language-tagged version', text => text.replace(versionOne, versionPredicate + ' "1"@en.')],
  ['boolean version', text => text.replace(versionOne, versionPredicate + ' true.')],
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
