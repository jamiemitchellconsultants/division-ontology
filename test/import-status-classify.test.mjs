import test from 'node:test';
import assert from 'node:assert/strict';
import { classifyGrantedSources, reason } from '../src/import-status.mjs';
import { parseTurtle } from '../src/rdf.mjs';
import { ontologyText } from './fixtures/onboarding/fixture-division.mjs';

const core = 'https://example.test/division#';
const ns = (n) => `https://example.test/${n}#`;
const sha = (c) => c.repeat(40);
const quads = (namespace) => parseTurtle(ontologyText(namespace), namespace);
const published = (...entries) => new Map(entries.map(([id, commit, namespace]) => [id, { commit, namespace, quads: quads(namespace) }]));
const repo = (id, hasConfig = true) => ({ id, defaultBranch: 'main', hasConfig });
const ok = (id, commit, namespace) => ({ id, attemptedCommit: commit, result: { ok: true, namespace, quads: quads(namespace) } });
const bad = (id, commit, code = 'ontology-invalid-turtle') => ({ id, attemptedCommit: commit, result: { ok: false, reason: reason(code, 'x') } });
const classify = (input) => classifyGrantedSources({ discovery: { selection: 'selected', repositories: [] }, outcomes: [], published: new Map(),
  registeredIds: new Set(), reservedNamespaces: new Map([[core, 'division core']]), ...input });
const discovery = (repositories, selection = 'selected') => ({ selection, repositories });

test('a valid new source is current', () => {
  const { rows, sources } = classify({ discovery: discovery([repo('o/a')]), outcomes: [ok('o/a', sha('a'), ns('a'))] });
  assert.deepEqual(rows, [{ source: 'o/a', status: 'current', attemptedCommit: sha('a'), publishedCommit: sha('a') }]);
  assert.deepEqual(sources.map(({ id, commit, namespace, origin, stale }) => ({ id, commit, namespace, origin, stale })),
    [{ id: 'o/a', commit: sha('a'), namespace: ns('a'), origin: 'granted', stale: false }]);
});
test('an invalid later version keeps the published one, marked stale', () => {
  const { rows, sources } = classify({ discovery: discovery([repo('o/a')]), outcomes: [bad('o/a', sha('b'))], published: published(['o/a', sha('a'), ns('a')]) });
  assert.deepEqual(rows[0], { source: 'o/a', status: 'invalid', attemptedCommit: sha('b'), publishedCommit: sha('a'), reason: { code: 'ontology-invalid-turtle', detail: 'x' } });
  assert.deepEqual([sources[0].commit, sources[0].stale], [sha('a'), true]);
});
test('an invalid first version is reported but not published', () => {
  const { rows, sources } = classify({ discovery: discovery([repo('o/a')]), outcomes: [bad('o/a', sha('b'))] });
  assert.deepEqual([rows[0].status, rows[0].publishedCommit, sources], ['invalid', null, []]);
});
test('a removed configuration on a published source is invalid and stale', () => {
  const { rows, sources } = classify({ discovery: discovery([repo('o/a', false)]), outcomes: [bad('o/a', sha('b'), 'config-missing')], published: published(['o/a', sha('a'), ns('a')]) });
  assert.deepEqual([rows[0].status, rows[0].reason.code, sources[0].stale], ['invalid', 'config-missing', true]);
});
test('an unconfigured repository is reported under a selected installation and ignored under all', () => {
  const selected = classify({ discovery: discovery([repo('o/a', false)]) });
  assert.deepEqual(selected.rows, [{ source: 'o/a', status: 'not configured', attemptedCommit: null, publishedCommit: null, reason: reason('config-missing', '.division-ontology.json') }]);
  assert.deepEqual(classify({ discovery: discovery([repo('o/a', false)], 'all') }).rows, []);
});
test('removed access keeps the published version stale; a never-published source leaves no row', () => {
  const { rows, sources } = classify({ published: published(['o/a', sha('a'), ns('a')]) });
  assert.deepEqual(rows, [{ source: 'o/a', status: 'access removed', attemptedCommit: null, publishedCommit: sha('a'), reason: reason('access-removed', 'repository is no longer in the installation') }]);
  assert.equal(sources[0].stale, true);
  assert.deepEqual(classify({}).rows, []);
});
test('a published source that is now steward-registered is dropped', () => {
  const out = classify({ published: published(['o/a', sha('a'), ns('a')]), registeredIds: new Set(['o/a']) });
  assert.deepEqual([out.rows, out.sources], [[], []]);
});
test('a namespace equal to, or overlapping, the core or a registered source is a conflict', () => {
  const reservedNamespaces = new Map([[core, 'division core'], [ns('reg'), 'o/reg']]);
  const outcomes = [ok('o/c', sha('a'), core), ok('o/p', sha('a'), `${ns('reg')}sub`), ok('o/q', sha('a'), 'https://example.test/')];
  const { rows, sources } = classify({ discovery: discovery(['o/c', 'o/p', 'o/q'].map((id) => repo(id))), outcomes, reservedNamespaces });
  assert.deepEqual(rows.map((row) => [row.status, row.reason.code]), [['invalid', 'namespace-conflict'], ['invalid', 'namespace-conflict'], ['invalid', 'namespace-conflict']]);
  assert.match(rows[0].reason.detail, /overlaps division core/);
  assert.deepEqual(sources, []);
});
test('the existing holder of a namespace wins regardless of id order', () => {
  const { rows } = classify({ discovery: discovery([repo('a/new'), repo('z/old')]),
    outcomes: [ok('a/new', sha('b'), ns('x')), ok('z/old', sha('c'), ns('x'))], published: published(['z/old', sha('c'), ns('x')]) });
  assert.deepEqual(rows.map((row) => [row.source, row.status]), [['a/new', 'invalid'], ['z/old', 'current']]);
  assert.equal(rows[0].reason.code, 'namespace-conflict');
});
test('one failing source does not hold back the others, and rows are sorted', () => {
  const { rows, sources } = classify({ discovery: discovery([repo('o/z'), repo('o/a'), repo('o/m', false)]), outcomes: [ok('o/z', sha('a'), ns('z')), bad('o/a', sha('b'))] });
  assert.deepEqual(rows.map((row) => [row.source, row.status]), [['o/a', 'invalid'], ['o/m', 'not configured'], ['o/z', 'current']]);
  assert.deepEqual(sources.map((s) => s.id), ['o/z']);
});
