import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { createFixtureDivision, ontologyText } from './fixtures/onboarding/fixture-division.mjs';
import { runCli } from '../src/cli.mjs';

const ns = (n) => `https://example.test/${n}#`;
const withDivision = (fn) => async () => { const d = createFixtureDivision(); try { await fn(d); } finally { d.cleanup(); } };
const files = ['catalog.json', 'catalog.md', 'combined.nq'];
const bytes = (d) => files.map((name) => d.dist(name));
const source = (d, id) => d.catalog().sources.find((s) => s.id === id);
const row = (d, id) => d.catalog().importStatus.find((r) => r.source === id);
const concepts = (d, id) => d.catalog().concepts.filter((c) => c.source === id).map((c) => c.label).sort();
async function joinSource(d, id = 'o/alpha', terms = ['Alpha']) { d.addSource(id, { namespace: ns(id.split('/')[1]), terms }); d.grant(id); await d.run('build'); return id; }
const alphaOntology = (terms) => ({ 'docs/ontology.ttl': ontologyText(ns('alpha'), terms) });

test('B1 granting a repository joins it without touching the division files', withDivision(async (d) => {
  const before = ['sources.json', 'division.json'].map((f) => readFileSync(join(d.root, f), 'utf8'));
  await joinSource(d);
  assert.equal(source(d, 'o/alpha').origin, 'granted');
  assert.deepEqual(['sources.json', 'division.json'].map((f) => readFileSync(join(d.root, f), 'utf8')), before);
}));
test('B3 a repository both registered and granted appears once, registered, with no status', withDivision(async (d) => {
  d.addSource('o/alpha', { namespace: ns('alpha') });
  d.register('o/alpha'); d.grant('o/alpha');
  await d.run('build');
  assert.deepEqual(d.catalog().sources.map((s) => [s.id, s.origin]), [['o/alpha', 'registered']]);
  assert.deepEqual(d.catalog().importStatus, []);
}));
test('B3 a published granted source later registered is built once, strictly', withDivision(async (d) => {
  await joinSource(d);
  d.register('o/alpha');
  await d.run('build');
  assert.deepEqual(d.catalog().sources.map((s) => [s.id, s.origin, s.stale]), [['o/alpha', 'registered', false]]);
  assert.deepEqual(d.catalog().importStatus, []);
}));
test('B4 a grant reaches only its own division', async () => {
  const a = createFixtureDivision(); const b = createFixtureDivision();
  try {
    a.addSource('o/alpha', {}); b.addSource('o/alpha', {});
    a.grant('o/alpha');
    await a.run('build'); await b.run('build');
    assert.equal(a.catalog().sources.length, 1);
    assert.deepEqual([b.catalog().sources, b.catalog().importStatus], [[], []]);
  } finally { a.cleanup(); b.cleanup(); }
});
test('B5 an all-repositories installation admits only configured repositories', withDivision(async (d) => {
  d.setSelection('all');
  d.addSource('o/with', {}); d.addSource('o/without', { config: null });
  d.grant('o/with'); d.grant('o/without');
  await d.run('build');
  assert.deepEqual(d.catalog().sources.map((s) => s.id), ['o/with']);
  assert.deepEqual(d.catalog().importStatus.map((r) => r.source), ['o/with']);
}));
test('B6 a valid new source appears with its exact commit and every concept', withDivision(async (d) => {
  await joinSource(d, 'o/alpha', ['A', 'B', 'C']);
  assert.equal(source(d, 'o/alpha').commit, d.head('o/alpha'));
  assert.equal(source(d, 'o/alpha').conceptCount, 3);
  assert.deepEqual(concepts(d, 'o/alpha'), ['A', 'B', 'C']);
  assert.equal(row(d, 'o/alpha').status, 'current');
}));
test('B8 a new valid version is imported without a renewed grant', withDivision(async (d) => {
  await joinSource(d);
  const commit = d.commitSource('o/alpha', alphaOntology(['Alpha', 'Beta']));
  await d.run('build');
  assert.deepEqual([source(d, 'o/alpha').commit, concepts(d, 'o/alpha')], [commit, ['Alpha', 'Beta']]);
}));
test('B9 a configuration change is followed', withDivision(async (d) => {
  await joinSource(d);
  d.commitSource('o/alpha', { 'docs/next.ttl': ontologyText(ns('alpha'), ['Moved']), 'docs/ontology.ttl': null,
    '.division-ontology.json': JSON.stringify({ schemaVersion: 1, ontologyPath: 'docs/next.ttl', namespace: ns('alpha') }) });
  await d.run('build');
  assert.deepEqual(concepts(d, 'o/alpha'), ['Moved']);
}));
test('B10 every granted source has a status; reasons carry no credentials', withDivision(async (d) => {
  await joinSource(d);
  assert.deepEqual(Object.keys(row(d, 'o/alpha')), ['source', 'status', 'attemptedCommit', 'publishedCommit']);
  const token = 'ghp_' + 'a1B2'.repeat(8);
  d.commitSource('o/alpha', { 'docs/ontology.ttl': ontologyText(`https://example.test/${token}#`, ['Thing']) });
  await d.run('build');
  assert.deepEqual(Object.keys(row(d, 'o/alpha')), ['source', 'status', 'attemptedCommit', 'publishedCommit', 'reason']);
  assert.equal(row(d, 'o/alpha').reason.code, 'concept-outside-namespace');
  assert.match(row(d, 'o/alpha').reason.detail, /\[redacted\]/);
  assert.doesNotMatch(JSON.stringify(d.catalog()) + d.dist('catalog.md'), /ghp_|x-access-token/);
}));
test('B11 an invalid new version keeps the last valid one, then recovers', withDivision(async (d) => {
  await joinSource(d);
  const good = d.head('o/alpha');
  const broken = d.commitSource('o/alpha', { 'docs/ontology.ttl': 'this is not turtle {' });
  await d.run('build');
  assert.deepEqual([row(d, 'o/alpha').status, row(d, 'o/alpha').attemptedCommit, row(d, 'o/alpha').publishedCommit], ['invalid', broken, good]);
  assert.deepEqual([source(d, 'o/alpha').stale, source(d, 'o/alpha').commit, concepts(d, 'o/alpha')], [true, good, ['Alpha']]);
  const fixed = d.commitSource('o/alpha', alphaOntology(['Alpha', 'Again']));
  await d.run('build');
  assert.deepEqual([row(d, 'o/alpha').status, source(d, 'o/alpha').stale, source(d, 'o/alpha').commit], ['current', false, fixed]);
}));
test('B12 an invalid first version is reported but not published', withDivision(async (d) => {
  d.addSource('o/alpha', { namespace: ns('alpha') });
  d.commitSource('o/alpha', { 'docs/ontology.ttl': 'this is not turtle {' });
  d.grant('o/alpha');
  await d.run('build');
  assert.equal(row(d, 'o/alpha').status, 'invalid');
  assert.equal(row(d, 'o/alpha').publishedCommit, null);
  assert.deepEqual([d.catalog().sources, concepts(d, 'o/alpha')], [[], []]);
}));
test('B13 a selected repository without configuration is reported', withDivision(async (d) => {
  d.addSource('o/alpha', { config: null }); d.grant('o/alpha');
  await d.run('build');
  assert.deepEqual([row(d, 'o/alpha').status, d.catalog().sources], ['not configured', []]);
}));
test('B14 removed access leaves the last version stale; regranting resumes', withDivision(async (d) => {
  await joinSource(d);
  d.revoke('o/alpha');
  await d.run('build');
  assert.deepEqual([row(d, 'o/alpha').status, row(d, 'o/alpha').publishedCommit, source(d, 'o/alpha').stale, concepts(d, 'o/alpha')], ['access removed', d.head('o/alpha'), true, ['Alpha']]);
  d.grant('o/alpha');
  const next = d.commitSource('o/alpha', alphaOntology(['Alpha', 'Back']));
  await d.run('build');
  assert.deepEqual([row(d, 'o/alpha').status, source(d, 'o/alpha').commit], ['current', next]);
}));
test('B14 a never-published source that loses access leaves the table', withDivision(async (d) => {
  d.addSource('o/alpha', { config: null }); d.grant('o/alpha');
  await d.run('build'); d.revoke('o/alpha'); await d.run('build');
  assert.deepEqual(d.catalog().importStatus, []);
}));
test('B15 one failing source does not hold back the others', withDivision(async (d) => {
  d.addSource('o/bad', { namespace: ns('bad') }); d.addSource('o/good', { namespace: ns('good') });
  d.commitSource('o/bad', { 'docs/ontology.ttl': 'this is not turtle {' });
  d.grant('o/bad'); d.grant('o/good');
  await d.run('build');
  assert.deepEqual([row(d, 'o/bad').status, row(d, 'o/good').status, source(d, 'o/good').conceptCount], ['invalid', 'current', 1]);
}));
test('B16 a conflicting namespace is rejected and the holder is unaffected', withDivision(async (d) => {
  d.addSource('o/reg', { namespace: ns('shared') }); d.register('o/reg');
  d.addSource('o/late', { namespace: ns('shared') }); d.grant('o/late');
  await d.run('build');
  assert.equal(row(d, 'o/late').reason.code, 'namespace-conflict');
  assert.deepEqual([source(d, 'o/reg').origin, concepts(d, 'o/reg')], ['registered', ['Thing']]);
}));
test('B17 unchanged inputs write identical bytes, including stale states', withDivision(async (d) => {
  await joinSource(d);
  const once = bytes(d); await d.run('build'); assert.deepEqual(bytes(d), once);
  d.commitSource('o/alpha', { 'docs/ontology.ttl': 'this is not turtle {' });
  await d.run('build');
  const stale = bytes(d); await d.run('build'); assert.deepEqual(bytes(d), stale);
  await d.run('check');
}));
test('a failed listing fails the run and leaves dist untouched, never marking access removed', withDivision(async (d) => {
  await joinSource(d);
  const before = bytes(d);
  d.failListing('GitHub API /installation/repositories: HTTP 503');
  await assert.rejects(d.run('build'), /HTTP 503/);
  assert.deepEqual(bytes(d), before);
}));
test('without a provider, a catalog that holds granted sources refuses to build', withDivision(async (d) => {
  await joinSource(d);
  await assert.rejects(runCli(['build', ...d.checkoutArgs()], { root: d.root, grantProvider: null }), /no source-read App/);
}));
test('a checkout override for an unknown repository is still rejected', withDivision(async (d) => {
  d.addSource('o/stray', {});
  await assert.rejects(runCli(['build', '--checkout', `o/stray=${d.overrides().get('o/stray')}`], { root: d.root, grantProvider: d.provider, urlFor: d.urlFor }), /o\/stray is not in sources\.json/);
}));
