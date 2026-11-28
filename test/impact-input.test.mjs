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
