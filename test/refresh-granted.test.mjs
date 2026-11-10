import test from 'node:test';
import assert from 'node:assert/strict';
import { createFixtureDivision, ontologyText } from './fixtures/onboarding/fixture-division.mjs';
import { classifyRefresh } from '../src/refresh-policy.mjs';

const ns = (n) => `https://example.test/${n}#`;
const changedPaths = ['dist/catalog.json', 'dist/catalog.md', 'dist/combined.nq'];
const take = (d) => ({ catalog: d.catalog(), nquads: d.dist('combined.nq') });
const classify = (before, after) => classifyRefresh({ previousCatalog: before.catalog, nextCatalog: after.catalog, previousNQuads: before.nquads, nextNQuads: after.nquads, changedPaths });
const withDivision = (fn) => async () => { const d = createFixtureDivision(); try { await fn(d); } finally { d.cleanup(); } };

test('B2 adding, updating and failing a granted source is automatic', withDivision(async (d) => {
  await d.run('build');
  const empty = take(d);
  d.addSource('o/a', { namespace: ns('a') }); d.grant('o/a');
  await d.run('build'); const added = take(d);
  assert.deepEqual(classify(empty, added), { mode: 'automatic', reasons: [] });
  d.commitSource('o/a', { 'docs/ontology.ttl': ontologyText(ns('a'), ['Thing', 'More']) });
  await d.run('build'); const updated = take(d);
  assert.equal(classify(added, updated).mode, 'automatic');
  d.commitSource('o/a', { 'docs/ontology.ttl': 'this is not turtle {' });
  await d.run('build');
  assert.equal(classify(updated, take(d)).mode, 'automatic');
  const invalid = take(d);
  d.revoke('o/a');
  await d.run('build');
  assert.equal(classify(invalid, take(d)).mode, 'automatic');
}));
test('B3 adding a steward-registered source still needs review', withDivision(async (d) => {
  await d.run('build');
  const empty = take(d);
  d.addSource('o/r', { namespace: ns('r') }); d.register('o/r');
  await d.run('build');
  const result = classify(empty, take(d));
  assert.equal(result.mode, 'review');
  assert.ok(result.reasons.includes('source registration changed'));
}));
test('catalogs written before origin existed read as registered', withDivision(async (d) => {
  d.addSource('o/r', { namespace: ns('r') }); d.register('o/r');
  await d.run('build');
  const after = take(d);
  const legacy = structuredClone(after);
  for (const source of legacy.catalog.sources) { delete source.origin; delete source.stale; }
  delete legacy.catalog.importStatus;
  assert.deepEqual(classify(legacy, after), { mode: 'automatic', reasons: [] });
}));
