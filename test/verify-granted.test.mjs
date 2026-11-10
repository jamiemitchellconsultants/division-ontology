import test from 'node:test';
import assert from 'node:assert/strict';
import { writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { createFixtureDivision } from './fixtures/onboarding/fixture-division.mjs';
import { verifyRefresh } from '../src/verify-refresh.mjs';

const changedPaths = ['dist/catalog.json', 'dist/catalog.md', 'dist/combined.nq'];
const withDivision = (fn) => async () => { const d = createFixtureDivision(); try { await fn(d); } finally { d.cleanup(); } };

test('B2 a candidate that carries a stale granted source forward verifies as automatic', withDivision(async (d) => {
  d.addSource('o/a', {}); d.grant('o/a');
  await d.run('build');
  d.revoke('o/a');
  const candidate = d.cloneRoot();
  await d.run('build', { root: candidate });
  const result = await verifyRefresh({ baseRoot: d.root, candidateDist: join(candidate, 'dist'), changedPaths, grantProvider: d.provider, urlFor: d.urlFor });
  assert.deepEqual(result, { mode: 'automatic', reasons: [] });
}));
test('a tampered candidate no longer matches a fresh build', withDivision(async (d) => {
  d.addSource('o/a', {}); d.grant('o/a');
  await d.run('build');
  d.revoke('o/a');
  const candidate = d.cloneRoot();
  await d.run('build', { root: candidate });
  writeFileSync(join(candidate, 'dist/catalog.md'), 'tampered\n');
  await assert.rejects(verifyRefresh({ baseRoot: d.root, candidateDist: join(candidate, 'dist'), changedPaths, grantProvider: d.provider, urlFor: d.urlFor }), /does not match a fresh build/);
}));
test('a new grant verifies when the live installation matches', withDivision(async (d) => {
  await d.run('build');
  d.addSource('o/a', {}); d.grant('o/a');
  const candidate = d.cloneRoot();
  await d.run('build', { root: candidate });
  const result = await verifyRefresh({ baseRoot: d.root, candidateDist: join(candidate, 'dist'), changedPaths,
    checkoutOverrides: d.overrides(), grantProvider: d.provider, urlFor: d.urlFor });
  assert.equal(result.mode, 'automatic');
}));
