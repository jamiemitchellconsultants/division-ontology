import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { buildCatalog } from '../src/catalog.mjs';
import { renderOutputs, writeOutputs } from '../src/output.mjs';
import { readPublishedState, reason } from '../src/import-status.mjs';
import { parseTurtle } from '../src/rdf.mjs';
import { coreNamespace, ontologyText } from './fixtures/onboarding/fixture-division.mjs';

const ns = (n) => `https://example.test/${n}#`;
const coreQuads = parseTurtle(ontologyText(coreNamespace, ['StaffId']), coreNamespace);
const source = (id, n, extra = {}) => ({ id, commit: 'a'.repeat(40), namespace: ns(n), quads: parseTurtle(ontologyText(ns(n), ['Thing']), ns(n)), ...extra });
const build = (sources, importStatus, links = []) => buildCatalog({ coreQuads, sources, links, coreNamespace, importStatus });

test('sources record origin and stale, defaulting to registered and fresh', () => {
  const { catalog } = build([source('o/a', 'a'), source('o/b', 'b', { origin: 'granted', stale: true })], []);
  assert.deepEqual(catalog.sources.map(({ id, conceptCount, origin, stale }) => ({ id, conceptCount, origin, stale })),
    [{ id: 'o/a', conceptCount: 1, origin: 'registered', stale: false }, { id: 'o/b', conceptCount: 1, origin: 'granted', stale: true }]);
  assert.deepEqual(catalog.importStatus, []);
  assert.deepEqual(Object.keys(catalog), ['schemaVersion', 'core', 'sources', 'concepts', 'relations', 'rules', 'importStatus', 'links']);
});
test('a link naming a granted source fails the build', () => {
  const links = [{ source: 'o/b', local: ns('b') + 'Thing', shared: coreNamespace + 'StaffId', relation: 'specializes' }];
  assert.throws(() => build([source('o/b', 'b', { origin: 'granted' })], [], links), /granted; links need steward registration/);
});
test('the import status is published in JSON and as a table', async () => {
  const rows = [{ source: 'o/b', status: 'invalid', attemptedCommit: 'b'.repeat(40), publishedCommit: null, reason: reason('config-missing', '.division-ontology.json') }];
  const { catalog, quads } = build([source('o/a', 'a')], rows);
  const out = await renderOutputs(catalog, quads);
  assert.deepEqual(JSON.parse(out['catalog.json']).importStatus, rows);
  assert.match(out['catalog.md'], /## Import status/);
  assert.match(out['catalog.md'], /\| o\/b \| invalid \| `b{40}` \| — \| config-missing: \.division-ontology\.json \|/);
  const none = await renderOutputs(...Object.values(build([source('o/a', 'a')], [])));
  assert.doesNotMatch(none['catalog.md'], /Import status/);
});
test('readPublishedState returns granted sources only, with their recorded graph', async () => {
  const root = mkdtempSync(join(tmpdir(), 'published-state-'));
  try {
    assert.equal(readPublishedState(root).size, 0);
    const granted = source('o/b', 'b', { origin: 'granted' });
    const { catalog, quads } = build([source('o/a', 'a'), granted], []);
    writeOutputs(root, await renderOutputs(catalog, quads));
    const state = readPublishedState(root);
    assert.deepEqual([...state.keys()], ['o/b']);
    assert.equal(state.get('o/b').commit, granted.commit);
    assert.equal(state.get('o/b').namespace, ns('b'));
    assert.equal(state.get('o/b').quads.length, granted.quads.length);
  } finally { rmSync(root, { recursive: true, force: true }); }
});
