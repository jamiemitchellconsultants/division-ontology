import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { readFile } from 'node:fs/promises';
import { inputState } from '../../deploy/workspace/verify-graph-explorer.mjs';

const [fixturePath, manifestPath, activeSnapshot] = process.argv.slice(2);
assert.ok(fixturePath && manifestPath && /^[a-f0-9]{64}$/.test(activeSnapshot ?? ''),
  'Usage: node test/acceptance/graph-inputs.mjs FIXTURE_JSON BUNDLE_MANIFEST ACTIVE_SNAPSHOT');

const bytes = await readFile(manifestPath);
assert.equal(createHash('sha256').update(bytes).digest('hex'), activeSnapshot,
  'The manifest matches the active snapshot observed in the workspace');
const manifest = JSON.parse(bytes);
const inputs = await inputState(fixturePath);
const revisions = Object.fromEntries(Object.entries(inputs).map(([source, state]) => [source, state.revision]));
assert.deepEqual(revisions, {
  'division-core': manifest.divisionCommit,
  ...Object.fromEntries(manifest.sources.map(source => [source.id, source.commit]))
}, 'The clean fixture checkouts match the pinned manifest revisions');
console.log(JSON.stringify({ snapshot: activeSnapshot, inputs }, null, 2));
