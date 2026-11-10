import { test } from 'node:test';
import assert from 'node:assert/strict';
import { cpSync, existsSync, mkdtempSync, readdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { initDivision } from '../scripts/init-division.mjs';
import { loadDivision } from '../src/division.mjs';

const templateOnlyTests = ['test/template.test.mjs', 'test/init-division.test.mjs'];
const copied = ['core', 'links', 'sources.json', 'division.json', '.github/CODEOWNERS', 'narrative', 'Narrative.md', 'docs', 'dist', ...templateOnlyTests];
const namespace = 'https://example.test/sales-division#';
const args = ['--namespace', namespace, '--steward', 'sales-steward', '--steward', 'sales-deputy'];

function templateCopy() {
  const root = mkdtempSync(join(tmpdir(), 'init-division-'));
  for (const path of copied) cpSync(path, join(root, path), { recursive: true });
  return root;
}

test('a fresh copy gets its own namespace, stewards, empty narrative and catalog', async () => {
  const root = templateCopy();
  const template = loadDivision('.');
  try {
    await initDivision(args, { root });
    assert.deepEqual(loadDivision(root), { namespace, stewards: ['sales-steward', 'sales-deputy'] });
    const ontology = readFileSync(join(root, 'core/ontology.ttl'), 'utf8');
    assert.ok(ontology.includes(`@prefix : <${namespace}>`));
    assert.ok(!ontology.includes(template.namespace));
    for (const line of readFileSync(join(root, '.github/CODEOWNERS'), 'utf8').split('\n').filter((l) => l && !l.startsWith('#'))) {
      assert.match(line, / @sales-steward @sales-deputy$/);
    }
    assert.deepEqual(readdirSync(join(root, 'narrative/entries')), ['.gitkeep']);
    assert.ok(!readFileSync(join(root, 'Narrative.md'), 'utf8').includes('Entry 1'));
    assert.ok(!existsSync(join(root, 'docs/features/one-action-source-onboarding')));
    assert.ok(!existsSync(join(root, 'docs/superpowers')));
    // The template's own tests assert that it has no sources, so a division copy must not keep them: they would fail the
    // required test check on the first refresh that adds a source.
    for (const path of templateOnlyTests) assert.ok(!existsSync(join(root, path)), `${path} is removed from a division copy`);
    assert.ok(existsSync(join(root, 'docs/features/runtime-knowledge-workspace/05-runbook.md')));
    const catalog = JSON.parse(readFileSync(join(root, 'dist/catalog.json'), 'utf8'));
    assert.equal(catalog.core.namespace, namespace);
    assert.deepEqual(catalog.sources, []);
    assert.deepEqual(catalog.importStatus, []);
    assert.ok(catalog.concepts.every((concept) => concept.iri.startsWith(namespace)));
  } finally { rmSync(root, { recursive: true, force: true }); }
});

test('initialisation refuses a copy that already has registered sources', async () => {
  const root = templateCopy();
  try {
    writeFileSync(join(root, 'sources.json'), JSON.stringify({ sources: [{ id: 'example/booking', url: 'https://github.com/example/booking.git', ref: 'main', ontologyPath: 'docs/ontology.ttl', namespace: 'https://example.test/booking#' }] }));
    await assert.rejects(initDivision(args, { root }), /no registered sources/);
    assert.notEqual(loadDivision(root).namespace, namespace);
  } finally { rmSync(root, { recursive: true, force: true }); }
});

test('initialisation requires a namespace and at least one steward', async () => {
  await assert.rejects(initDivision(['--steward', 'sales-steward'], { root: tmpdir() }), /usage/);
  await assert.rejects(initDivision(['--namespace', namespace], { root: tmpdir() }), /usage/);
  await assert.rejects(initDivision(['--namespace', 'http://insecure.test#', '--steward', 'a'], { root: tmpdir() }), /namespace/);
});
