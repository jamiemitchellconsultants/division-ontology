import test from 'node:test';
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { withPilotFixture } from './fixtures/c4/fixture-repos.mjs';
import { loadMasterList } from '../src/c4/master-list.mjs';
import { loadSnapshot } from '../src/c4/snapshot.mjs';
import { buildImpactInput } from '../src/c4/impact-input.mjs';

const list = { schemaVersion: 1, relationships: [
  { iri: 'https://ontology-kit.dev/schema#contains', impact: 'subject-to-object', definition: 'kept, ignored' },
  { iri: 'https://ontology-kit.dev/schema#communicates-with', impact: 'both' }] };

test('a missing list is null, so the engine applies B9', () => withPilotFixture(f => {
  assert.equal(loadMasterList(f), null);
  assert.equal('masterList' in buildImpactInput({ snapshot: loadSnapshot(f), registry: null }), false);
}));

test('the list is read at the catalog commit with its digest as identity', () => withPilotFixture(f => {
  const text = JSON.stringify(list);
  f.writeDivision('relationships/master-list.json', text);
  const catalogCommit = f.commitDivision('add list');
  const loaded = loadMasterList({ divisionRoot: f.divisionRoot, catalogCommit });
  assert.deepEqual(loaded.identity, { status: 'listed', catalogCommit,
    sha256: createHash('sha256').update(text).digest('hex') });
  assert.equal(loadMasterList(f), null, 'an earlier catalog commit has no list');
}));

for (const [name, bad] of [
  ['wrong schema', { schemaVersion: 2, relationships: [] }],
  ['unknown impact', { schemaVersion: 1, relationships: [{ iri: 'x', impact: 'sometimes' }] }],
  ['duplicate iri', { schemaVersion: 1, relationships: [{ iri: 'x', impact: 'none' }, { iri: 'x', impact: 'both' }] }],
  ['missing iri', { schemaVersion: 1, relationships: [{ impact: 'none' }] }]]) {
  test(`an invalid list fails packaging: ${name}`, () => withPilotFixture(f => {
    f.writeDivision('relationships/master-list.json', JSON.stringify(bad));
    const catalogCommit = f.commitDivision('bad list');
    assert.throws(() => loadMasterList({ divisionRoot: f.divisionRoot, catalogCommit }), /invalid relationship master list/);
  }));
}
