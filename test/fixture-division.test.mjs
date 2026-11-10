import test from 'node:test';
import assert from 'node:assert/strict';
import { createFixtureDivision, importConfig, ontologyText } from './fixtures/onboarding/fixture-division.mjs';

test('a registered fixture source builds into the catalog', async () => {
  const d = createFixtureDivision();
  try {
    d.addSource('example/alpha', { terms: ['Alpha'] });
    d.register('example/alpha');
    await d.run('build');
    assert.deepEqual(d.catalog().sources.map((s) => s.id), ['example/alpha']);
    assert.match(d.head('example/alpha'), /^[0-9a-f]{40}$/);
  } finally { d.cleanup(); }
});
test('the provider lists granted repositories and whether they carry configuration', async () => {
  const d = createFixtureDivision();
  try {
    d.addSource('example/with', {});
    d.addSource('example/without', { config: null });
    d.addSource('example/unlisted', {});
    d.grant('example/with'); d.grant('example/without');
    assert.deepEqual(await d.provider.listInstallation(), { selection: 'selected', repositories: [
      { id: 'example/with', defaultBranch: 'main' }, { id: 'example/without', defaultBranch: 'main' }] });
    assert.equal(await d.provider.hasImportConfig('example/with', 'main'), true);
    assert.equal(await d.provider.hasImportConfig('example/without', 'main'), false);
    d.setSelection('all');
    assert.equal((await d.provider.listInstallation()).selection, 'all');
    d.failListing('HTTP 503');
    await assert.rejects(d.provider.listInstallation(), /HTTP 503/);
    d.revoke('example/with');
  } finally { d.cleanup(); }
});
test('helpers write the documented text', () => {
  assert.match(ontologyText('https://x.test/a#', ['A']), /:A a owl:Class; rdfs:label "A"/);
  assert.deepEqual(JSON.parse(importConfig('https://x.test/a#')), { schemaVersion: 1, ontologyPath: 'docs/ontology.ttl', namespace: 'https://x.test/a#' });
});
