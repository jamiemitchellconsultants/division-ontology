import { test } from 'node:test';
import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { readdirSync, readFileSync } from 'node:fs';
import { templateHistory } from '../scripts/init-division.mjs';

const json = (path) => JSON.parse(readFileSync(path, 'utf8'));
const shipped = ['sources.json', 'dist/catalog.json', 'dist/catalog.md', 'dist/combined.nq', 'c4/mappings.json', 'c4/bindings.json'];

test('the template registers no source repositories', () => assert.deepEqual(json('sources.json').sources, []));

test('the template ships no source links', () => assert.deepEqual(readdirSync('links'), ['.gitkeep']));

test('the template catalog has no sources, links or C4 registry entries', () => {
  const catalog = json('dist/catalog.json');
  assert.deepEqual(catalog.sources, []);
  assert.deepEqual(catalog.links, []);
  assert.deepEqual(json('c4/mappings.json').mappings, []);
  assert.deepEqual(json('c4/bindings.json').bindings, []);
});

test('no shipped source or catalog file names the former pilot source', () => {
  for (const path of shipped) assert.doesNotMatch(readFileSync(path, 'utf8'), /joint-?booking/i, path);
});

const removedOrRegenerated = ['Narrative.md', 'narrative/entries/', ...templateHistory.map((path) => `${path}/`)];

test('nothing a division copy keeps names the former pilot source', () => {
  const kept = execFileSync('git', ['ls-files'], { encoding: 'utf8' }).split('\n')
    .filter((path) => path && !removedOrRegenerated.some((prefix) => path === prefix || path.startsWith(prefix)));
  const named = kept.filter((path) => /joint-?booking/i.test(readFileSync(path, 'latin1')));
  assert.deepEqual(named, []);
});
