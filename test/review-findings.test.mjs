import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { coreNamespace, createFixtureDivision, importConfig, ontologyText } from './fixtures/onboarding/fixture-division.mjs';
import { readSourceOntology } from '../src/rdf.mjs';
import { buildCatalog } from '../src/catalog.mjs';
import { renderOutputs, writeOutputs } from '../src/output.mjs';
import { readPublishedState } from '../src/import-status.mjs';
import { parseTurtle } from '../src/rdf.mjs';

const ns = (n) => `https://example.test/${n}#`;
const withDivision = (fn) => async () => { const d = createFixtureDivision(); try { await fn(d); } finally { d.cleanup(); } };
const files = ['catalog.json', 'catalog.md', 'combined.nq'];
const bytes = (d) => files.map((name) => d.dist(name));
const row = (d, id) => d.catalog().importStatus.find((r) => r.source === id);
const source = (d, id) => d.catalog().sources.find((s) => s.id === id);

test('review 1: a stale source with blank nodes writes identical bytes on every build', withDivision(async (d) => {
  const blank = `${ontologyText(ns('a'), ['Thing', 'Other'])}:Thing <http://www.w3.org/2000/01/rdf-schema#subClassOf> [ a <http://www.w3.org/2002/07/owl#Restriction> ; <http://www.w3.org/2002/07/owl#onProperty> :p ; <http://www.w3.org/2002/07/owl#someValuesFrom> :Other ] .\n`;
  d.addSource('o/a', { namespace: ns('a') });
  d.commitSource('o/a', { 'docs/ontology.ttl': blank });
  d.grant('o/a');
  await d.run('build');
  d.commitSource('o/a', { 'docs/ontology.ttl': 'this is not turtle {' });
  await d.run('build');
  const once = bytes(d);
  assert.match(once[2], /_:/);
  await d.run('build');
  assert.deepEqual(bytes(d), once);
  await d.run('check');
}));

test('review 2: a published source cannot take another published source\'s namespace', withDivision(async (d) => {
  d.addSource('o/alpha', { namespace: ns('alpha') });
  d.addSource('o/beta', { namespace: ns('beta') });
  d.grant('o/alpha'); d.grant('o/beta');
  await d.run('build');
  d.commitSource('o/alpha', { 'docs/ontology.ttl': ontologyText(ns('beta'), ['Thing']), '.division-ontology.json': importConfig(ns('beta')) });
  await d.run('build');
  assert.deepEqual([row(d, 'o/alpha').status, row(d, 'o/alpha').reason.code, row(d, 'o/beta').status], ['invalid', 'namespace-conflict', 'current']);
  assert.equal(source(d, 'o/alpha').stale, true);
  assert.equal(source(d, 'o/alpha').namespace, ns('alpha'));
}));

test('review 3: an ontology over 1 MiB imports without failing the run', withDivision(async (d) => {
  const terms = Array.from({ length: 30000 }, (_, i) => `Term${i}`);
  d.addSource('o/big', { namespace: ns('big'), terms });
  d.addSource('o/good', { namespace: ns('good') });
  d.grant('o/big'); d.grant('o/good');
  assert.ok(readFileSync(join(d.overrides().get('o/big'), 'docs/ontology.ttl')).length > 1024 * 1024);
  await d.run('build');
  assert.deepEqual([row(d, 'o/big').status, row(d, 'o/good').status, source(d, 'o/big').conceptCount], ['current', 'current', 30000]);
}));
test('review 3: a source over the size limit is unreadable content, not an infrastructure error', withDivision((d) => {
  d.addSource('o/a', { namespace: ns('a'), terms: ['A', 'B', 'C'] });
  assert.throws(() => readSourceOntology({ id: 'o/a', ontologyPath: 'docs/ontology.ttl', namespace: ns('a') }, d.overrides().get('o/a'), d.head('o/a'), { maxBytes: 100 }),
    (e) => e.code === 'ontology-unreadable' && /larger than/.test(e.detail));
}));

test('review 4: a steward registering a published granted namespace does not break the build', withDivision(async (d) => {
  d.addSource('o/alpha', { namespace: ns('shared'), terms: ['A'] });
  d.grant('o/alpha');
  await d.run('build');
  d.addSource('o/beta', { namespace: ns('shared'), terms: ['A'] });
  d.register('o/beta');
  await d.run('build');
  assert.deepEqual([row(d, 'o/alpha').status, row(d, 'o/alpha').reason.code, row(d, 'o/alpha').publishedCommit], ['invalid', 'namespace-conflict', null]);
  assert.deepEqual(d.catalog().sources.map((s) => [s.id, s.origin]), [['o/beta', 'registered']]);
  d.revoke('o/alpha');
  await d.run('build');
  assert.equal(row(d, 'o/alpha'), undefined);
  assert.deepEqual(d.catalog().sources.map((s) => s.id), ['o/beta']);
}));

test('review 6: a granted source cannot make statements about terms outside its namespace', withDivision(async (d) => {
  d.addSource('o/a', { namespace: ns('a') });
  d.commitSource('o/a', { 'docs/ontology.ttl': `${ontologyText(ns('a'))}<${coreNamespace}StaffId> <http://www.w3.org/2000/01/rdf-schema#comment> "Deprecated" .\n` });
  d.grant('o/a');
  await d.run('build');
  assert.deepEqual([row(d, 'o/a').status, row(d, 'o/a').reason.code, row(d, 'o/a').reason.detail], ['invalid', 'concept-outside-namespace', `${coreNamespace}StaffId`]);
  assert.doesNotMatch(d.dist('combined.nq'), /Deprecated/);
}));
test('review 6: the ontology header named by the namespace stays allowed', withDivision(async (d) => {
  d.addSource('o/a', { namespace: ns('a') });
  d.commitSource('o/a', { 'docs/ontology.ttl': `${ontologyText(ns('a'))}<https://example.test/a> a <http://www.w3.org/2002/07/owl#Ontology> .\n` });
  d.grant('o/a');
  await d.run('build');
  assert.equal(row(d, 'o/a').status, 'current');
}));

test('review minor: a published granted graph missing from combined.nq fails instead of vanishing', async () => {
  const root = mkdtempSync(join(tmpdir(), 'published-missing-'));
  try {
    const coreQuads = parseTurtle(ontologyText(coreNamespace, ['StaffId']), coreNamespace);
    const quads = parseTurtle(ontologyText(ns('b')), ns('b'));
    const { catalog, quads: all } = buildCatalog({ coreQuads, coreNamespace, links: [], sources: [{ id: 'o/b', commit: 'a'.repeat(40), namespace: ns('b'), quads, origin: 'granted' }] });
    const out = await renderOutputs(catalog, all);
    out['combined.nq'] = '';
    writeOutputs(root, out);
    assert.throws(() => readPublishedState(root), /o\/b.*missing from dist\/combined\.nq/);
  } finally { rmSync(root, { recursive: true, force: true }); }
});
