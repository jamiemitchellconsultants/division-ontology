import test from 'node:test';
import assert from 'node:assert/strict';
import { symlinkSync } from 'node:fs';
import { join } from 'node:path';
import { createFixtureDivision, importConfig, ontologyText } from './fixtures/onboarding/fixture-division.mjs';
import { importGrantedSources } from '../src/granted-sources.mjs';
import { InfrastructureError } from '../src/errors.mjs';
import { readSourceOntology } from '../src/rdf.mjs';

const ns = (n) => `https://example.test/${n}#`;
const repo = (id, hasConfig = true) => ({ id, defaultBranch: 'main', hasConfig });
const withDivision = (fn) => async () => { const d = createFixtureDivision(); try { await fn(d); } finally { d.cleanup(); } };
const run = (d, repos, published = new Map()) => importGrantedSources({ repositories: repos, published, urlFor: d.urlFor, checkoutOverrides: d.overrides() });

test('a valid repository yields its namespace, quads and the commit it read', withDivision((d) => {
  d.addSource('o/a', { namespace: ns('a'), terms: ['A', 'B'] });
  const [outcome] = run(d, [repo('o/a')]);
  assert.equal(outcome.id, 'o/a');
  assert.equal(outcome.attemptedCommit, d.head('o/a'));
  assert.equal(outcome.result.ok, true);
  assert.equal(outcome.result.namespace, ns('a'));
  assert.ok(outcome.result.quads.length >= 4);
}));
test('an unconfigured, unpublished repository is not cloned', withDivision((d) => {
  d.addSource('o/a', { config: null });
  assert.deepEqual(run(d, [repo('o/a', false)]), []);
}));
test('a published repository is attempted even when its configuration is gone', withDivision((d) => {
  d.addSource('o/a', { config: null });
  const [outcome] = run(d, [repo('o/a', false)], new Map([['o/a', {}]]));
  assert.equal(outcome.attemptedCommit, d.head('o/a'));
  assert.equal(outcome.result.reason.code, 'config-missing');
}));
test('configuration problems are config-invalid', withDivision((d) => {
  d.addSource('o/json', { config: '{nope' });
  d.addSource('o/key', { config: JSON.stringify({ schemaVersion: 1, ontologyPath: 'docs/ontology.ttl', namespace: ns('key'), extra: 1 }) });
  d.addSource('o/big', { config: JSON.stringify({ schemaVersion: 1, ontologyPath: 'docs/ontology.ttl', namespace: ns('big'), pad: 'x'.repeat(17000) }) });
  d.addSource('o/link', { config: null });
  symlinkSync('docs/ontology.ttl', join(d.overrides().get('o/link'), '.division-ontology.json'));
  d.commitSource('o/link', {});
  const byId = Object.fromEntries(run(d, ['o/big', 'o/json', 'o/key', 'o/link'].map((id) => repo(id))).map((o) => [o.id, o.result]));
  for (const id of ['o/big', 'o/json', 'o/key', 'o/link']) { assert.equal(byId[id].ok, false, id); assert.equal(byId[id].reason.code, 'config-invalid', id); }
  assert.match(byId['o/link'].reason.detail, /not a regular file/);
  assert.match(byId['o/big'].reason.detail, /larger than 16 KiB/);
}));
test('ontology problems carry a code and detail and do not stop other repositories', withDivision((d) => {
  d.addSource('o/bad', { namespace: ns('bad') });
  d.addSource('o/good', { namespace: ns('good') });
  d.commitSource('o/bad', { 'docs/ontology.ttl': 'this is not turtle {' });
  const outcomes = run(d, [repo('o/bad'), repo('o/good')]);
  assert.deepEqual(outcomes.map((o) => [o.id, o.result.ok]), [['o/bad', false], ['o/good', true]]);
  assert.equal(outcomes[0].result.reason.code, 'ontology-invalid-turtle');
  assert.match(outcomes[0].result.reason.detail, /^line /);
}));
test('readSourceOntology failures carry codes and details', withDivision((d) => {
  const cases = [
    ['docs/missing.ttl', ns('m'), 'ontology-unreadable', 'docs/missing.ttl', {}],
    ['docs/ontology.ttl', ns('o'), 'concept-outside-namespace', ns('other') + 'Thing', { 'docs/ontology.ttl': ontologyText(ns('other')) }],
    ['docs/ontology.ttl', ns('e'), 'no-concepts', ns('e'), { 'docs/ontology.ttl': `@prefix : <${ns('e')}> .\n` }],
  ];
  for (const [index, [ontologyPath, namespace, code, detail, files]] of cases.entries()) {
    const id = `o/case${index}`;
    d.addSource(id, { namespace });
    const commit = d.commitSource(id, files);
    assert.throws(() => readSourceOntology({ id, ontologyPath, namespace }, d.overrides().get(id), commit), (e) => e.code === code && e.detail === detail, code);
  }
}));
test('a configuration change is followed on the next import', withDivision((d) => {
  d.addSource('o/a', { namespace: ns('a'), terms: ['Old'] });
  d.commitSource('o/a', { 'docs/new.ttl': ontologyText(ns('a'), ['New']), '.division-ontology.json': importConfig(ns('a'), 'docs/new.ttl'), 'docs/ontology.ttl': null });
  const [outcome] = run(d, [repo('o/a')]);
  assert.equal(outcome.result.ok, true);
  assert.ok(outcome.result.quads.some((q) => q.subject.value === ns('a') + 'New'));
}));
test('a clone failure is an infrastructure error, not a status', () => {
  assert.throws(() => importGrantedSources({ repositories: [repo('o/gone')], published: new Map(), urlFor: () => '/nonexistent/path/for/test', checkoutOverrides: new Map() }), InfrastructureError);
});
