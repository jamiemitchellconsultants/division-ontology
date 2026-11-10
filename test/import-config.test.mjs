import test from 'node:test';
import assert from 'node:assert/strict';
import { IMPORT_CONFIG_MAX_BYTES, IMPORT_CONFIG_PATH, parseImportConfiguration } from '../src/import-config.mjs';

const valid = { schemaVersion: 1, ontologyPath: 'docs/ontology.ttl', namespace: 'https://example.test/a#' };
const parse = (value) => parseImportConfiguration(typeof value === 'string' ? value : JSON.stringify(value));

test('the file lives at a fixed repository-root path with a 16 KiB limit', () => {
  assert.equal(IMPORT_CONFIG_PATH, '.division-ontology.json');
  assert.equal(IMPORT_CONFIG_MAX_BYTES, 16384);
});
test('a valid file yields its ontology path and namespace', () => {
  assert.deepEqual(parse(valid), { ok: true, config: { ontologyPath: 'docs/ontology.ttl', namespace: 'https://example.test/a#' } });
});
for (const [name, input, detail] of [
  ['text that is not JSON', '{nope', /not valid JSON/],
  ['a JSON array', '[]', /must be a JSON object/],
  ['an unknown key', { ...valid, extra: 1 }, /unknown key extra/],
  ['another schema version', { ...valid, schemaVersion: 2 }, /schemaVersion must be 1/],
  ['a missing namespace', { schemaVersion: 1, ontologyPath: 'docs/ontology.ttl' }, /namespace must be an HTTP IRI/],
  ['a non-HTTP namespace', { ...valid, namespace: 'ftp://example.test/a#' }, /namespace must be an HTTP IRI/],
  ['an absolute path', { ...valid, ontologyPath: '/etc/o.ttl' }, /ontologyPath must stay inside the repository/],
  ['a parent-directory path', { ...valid, ontologyPath: 'docs/../../o.ttl' }, /ontologyPath must stay inside the repository/],
  ['an empty path', { ...valid, ontologyPath: '' }, /ontologyPath must stay inside the repository/],
  ['an oversized file', JSON.stringify({ ...valid, pad: 'x'.repeat(17000) }), /larger than 16 KiB/],
]) {
  test(`rejects ${name} as config-invalid`, () => {
    const result = parse(input);
    assert.equal(result.ok, false);
    assert.equal(result.reason.code, 'config-invalid');
    assert.match(result.reason.detail, detail);
  });
}
