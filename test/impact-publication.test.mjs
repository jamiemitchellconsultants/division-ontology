import test from 'node:test';
import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { mkdirSync, readdirSync, readFileSync, statSync, writeFileSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { pinGitEnvironment } from './fixtures/deterministic-git.mjs';
import { withConfluenceFixture } from './fixtures/impact/confluence-repos.mjs';
import { runCli } from '../src/cli.mjs';

// Cited page text lives only in the impact model and the signed-in API, never in published files (B29).
export const PAGE_TEXT = [
  "Claims over 500 need a manager's approval.",
  'Managers approve claims in the finance system.',
  'The old ledger still exports claims nightly.',
  'The finance team runs the claims job.'
];

const git = (cwd, ...args) => execFileSync('git', args, { cwd, encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] }).trim();
const put = (root, path, text) => { mkdirSync(dirname(join(root, path)), { recursive: true }); writeFileSync(join(root, path), text); };
const files = dir => readdirSync(dir).flatMap(name => {
  const path = join(dir, name);
  return statSync(path).isDirectory() ? files(path) : [path];
});

test('the published catalog never carries cited page text (B29)', async () => {
  pinGitEnvironment();
  await withConfluenceFixture(async f => {
    // The catalog build needs each source's ontology and a division core, which the impact fixture omits.
    const sources = [];
    for (const [id, checkout] of f.checkouts) {
      const name = id.split('/')[1], namespace = `https://example.test/${name}/ontology#`;
      put(checkout, 'docs/ontology.ttl', `@prefix : <${namespace}> .\n@prefix owl: <http://www.w3.org/2002/07/owl#> .\n` +
        `@prefix rdfs: <http://www.w3.org/2000/01/rdf-schema#> .\n:Claim a owl:Class; rdfs:label "Claim"; rdfs:comment "Synthetic ${name} claim." .\n`);
      git(checkout, 'add', '.');
      git(checkout, 'commit', '-qm', 'ontology');
      sources.push({ id, url: `https://github.com/${id}.git`, ref: 'main', ontologyPath: 'docs/ontology.ttl', namespace });
    }
    put(f.divisionRoot, 'core/ontology.ttl', '@prefix : <https://github.com/jamiemitchellconsultants/division-ontology#> .\n' +
      '@prefix owl: <http://www.w3.org/2002/07/owl#> .\n@prefix rdfs: <http://www.w3.org/2000/01/rdf-schema#> .\n' +
      ':BusinessOrder a owl:Class; rdfs:label "BusinessOrder"; rdfs:comment "A request for goods made by a customer." .\n');
    put(f.divisionRoot, 'division.json', JSON.stringify({ namespace: 'https://github.com/jamiemitchellconsultants/division-ontology#', stewards: ['steward-one'] }, null, 2) + '\n');
    put(f.divisionRoot, 'sources.json', JSON.stringify({ sources }, null, 2) + '\n');
    const flags = [...f.checkouts].flatMap(([id, path]) => ['--checkout', `${id}=${path}`]);
    await runCli(['build', ...flags], { root: f.divisionRoot });

    const published = files(join(f.divisionRoot, 'dist'));
    const all = published.map(path => readFileSync(path, 'utf8')).join('\n');
    assert.ok(all.includes('fixture/policies') && all.includes('https://example.test/policies/ontology#Claim'),
      'the catalog was built from the fixture sources');
    for (const path of published) {
      const text = readFileSync(path, 'utf8');
      for (const line of PAGE_TEXT) assert.ok(!text.includes(line), `${path} contains cited page text: ${line}`);
    }
  });
});
