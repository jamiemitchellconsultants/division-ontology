import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, mkdirSync, readFileSync, writeFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { execFileSync } from 'node:child_process';
import { runCli } from '../src/cli.mjs';

const sourceId = 'example/booking';
const coreNamespace = 'https://github.com/jamiemitchellconsultants/division-ontology#';
function pilot() {
  const root = mkdtempSync(join(tmpdir(), 'division-cli-'));
  const checkout = join(root, 'booking');
  mkdirSync(checkout);
  execFileSync('git', ['init', '-q', '-b', 'main', checkout]);
  mkdirSync(join(checkout, 'docs'));
  writeFileSync(join(checkout, 'docs/ontology.ttl'), '@prefix : <https://example.test/booking#> . @prefix owl: <http://www.w3.org/2002/07/owl#> . @prefix rdfs: <http://www.w3.org/2000/01/rdf-schema#> . :StaffId a owl:Class; rdfs:label "StaffId" .\n');
  execFileSync('git', ['add', '.'], { cwd: checkout });
  execFileSync('git', ['-c', 'user.name=Test', '-c', 'user.email=test@example.invalid', 'commit', '-q', '-m', 'fixture'], { cwd: checkout });
  mkdirSync(join(root, 'core'));
  mkdirSync(join(root, 'links'));
  writeFileSync(join(root, 'core/ontology.ttl'), `@prefix : <${coreNamespace}> . @prefix owl: <http://www.w3.org/2002/07/owl#> . @prefix rdfs: <http://www.w3.org/2000/01/rdf-schema#> . :StaffId a owl:Class; rdfs:label "StaffId" .\n`);
  writeFileSync(join(root, 'sources.json'), JSON.stringify({ sources: [{ id: sourceId, url: checkout, ref: 'main', ontologyPath: 'docs/ontology.ttl', namespace: 'https://example.test/booking#', linksPath: 'links/booking.json' }] }));
  writeFileSync(join(root, 'links/booking.json'), JSON.stringify({ source: sourceId, links: [{ local: 'https://example.test/booking#StaffId', shared: `${coreNamespace}StaffId`, relation: 'exact' }] }));
  return { root, checkout, override: `${sourceId}=${checkout}` };
}

test('build and check produce stable complete outputs with provenance', async () => {
  const data = pilot();
  try {
    await runCli(['build', '--checkout', data.override], { root: data.root });
    const before = ['catalog.json', 'catalog.md', 'combined.nq'].map((name) => readFileSync(join(data.root, 'dist', name), 'utf8'));
    const catalog = JSON.parse(before[0]);
    assert.equal(catalog.sources[0].conceptCount, 1);
    assert.equal(catalog.links[0].relation, 'exact');
    assert.match(before[2], /owl#equivalentClass/);
    assert.match(before[2], /urn:division-ontology:source:/);
    await runCli(['check', '--checkout', data.override], { root: data.root });
    await runCli(['build', '--checkout', data.override], { root: data.root });
    assert.deepEqual(['catalog.json', 'catalog.md', 'combined.nq'].map((name) => readFileSync(join(data.root, 'dist', name), 'utf8')), before);
  } finally { rmSync(data.root, { recursive: true, force: true }); }
});

test('check detects a new committed source revision', async () => {
  const data = pilot();
  try {
    await runCli(['build', '--checkout', data.override], { root: data.root });
    writeFileSync(join(data.checkout, 'docs/ontology.ttl'), '@prefix : <https://example.test/booking#> . @prefix owl: <http://www.w3.org/2002/07/owl#> . @prefix rdfs: <http://www.w3.org/2000/01/rdf-schema#> . :StaffId a owl:Class; rdfs:label "StaffId" . :NewTerm a owl:Class; rdfs:label "NewTerm" .\n');
    execFileSync('git', ['add', '.'], { cwd: data.checkout });
    execFileSync('git', ['-c', 'user.name=Test', '-c', 'user.email=test@example.invalid', 'commit', '-q', '-m', 'add term'], { cwd: data.checkout });
    await assert.rejects(runCli(['check', '--checkout', data.override], { root: data.root }), /stale/i);
  } finally { rmSync(data.root, { recursive: true, force: true }); }
});

test('failed validation leaves the last complete catalog untouched', async () => {
  const data = pilot();
  try {
    await runCli(['build', '--checkout', data.override], { root: data.root });
    const before = ['catalog.json', 'catalog.md', 'combined.nq'].map((name) => readFileSync(join(data.root, 'dist', name), 'utf8'));
    writeFileSync(join(data.root, 'links/booking.json'), JSON.stringify({ source: sourceId, links: [{ local: 'https://example.test/booking#Missing', shared: `${coreNamespace}StaffId`, relation: 'exact' }] }));
    await assert.rejects(runCli(['build', '--checkout', data.override], { root: data.root }), /local.*not found/i);
    assert.deepEqual(['catalog.json', 'catalog.md', 'combined.nq'].map((name) => readFileSync(join(data.root, 'dist', name), 'utf8')), before);
  } finally { rmSync(data.root, { recursive: true, force: true }); }
});
