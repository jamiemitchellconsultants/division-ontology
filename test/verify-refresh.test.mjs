import test from 'node:test';
import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { cpSync, mkdtempSync, mkdirSync, readFileSync, rmSync, symlinkSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { runCli } from '../src/cli.mjs';
import { verifyRefresh } from '../src/verify-refresh.mjs';

const names = ['catalog.json', 'catalog.md', 'combined.nq'];
const sourceId = 'example/booking';
const core = 'https://github.com/jamiemitchellconsultants/division-ontology#';
const sourceText = (extra = '') => `@prefix : <https://example.test/booking#> . @prefix owl: <http://www.w3.org/2002/07/owl#> . @prefix rdfs: <http://www.w3.org/2000/01/rdf-schema#> . :StaffId a owl:Class; rdfs:label "StaffId" . ${extra}\n`;
function commit(checkout, text) {
  writeFileSync(join(checkout, 'docs/ontology.ttl'), text);
  execFileSync('git', ['add', '.'], { cwd: checkout });
  execFileSync('git', ['-c', 'user.name=Test', '-c', 'user.email=test@example.invalid', 'commit', '-q', '-m', 'fixture'], { cwd: checkout });
}
async function fixture() {
  const root = mkdtempSync(join(tmpdir(), 'verify-refresh-'));
  const baseRoot = join(root, 'base');
  const checkout = join(root, 'source');
  const candidateDist = join(root, 'candidate');
  mkdirSync(baseRoot); mkdirSync(checkout); mkdirSync(candidateDist);
  execFileSync('git', ['init', '-q', '-b', 'main', checkout]);
  execFileSync('git', ['remote', 'add', 'origin', checkout], { cwd: checkout });
  mkdirSync(join(checkout, 'docs'));
  commit(checkout, sourceText());
  mkdirSync(join(baseRoot, 'core')); mkdirSync(join(baseRoot, 'links'));
  writeFileSync(join(baseRoot, 'core/ontology.ttl'), `@prefix : <${core}> . @prefix owl: <http://www.w3.org/2002/07/owl#> . @prefix rdfs: <http://www.w3.org/2000/01/rdf-schema#> . :StaffId a owl:Class; rdfs:label "StaffId" .\n`);
  writeFileSync(join(baseRoot, 'sources.json'), JSON.stringify({ sources: [{ id: sourceId, url: checkout, ref: 'main', ontologyPath: 'docs/ontology.ttl', namespace: 'https://example.test/booking#', linksPath: 'links/booking.json' }] }));
  writeFileSync(join(baseRoot, 'links/booking.json'), JSON.stringify({ source: sourceId, links: [{ local: 'https://example.test/booking#StaffId', shared: `${core}StaffId`, relation: 'exact' }] }));
  await runCli(['build', '--checkout', `${sourceId}=${checkout}`], { root: baseRoot });
  cpSync(join(baseRoot, 'dist'), candidateDist, { recursive: true });
  return { root, baseRoot, checkout, candidateDist, overrides: new Map([[sourceId, checkout]]), before: names.map((name) => readFileSync(join(baseRoot, 'dist', name))) };
}
function advance(data, extra = ':Desk a owl:Class; rdfs:label "Desk" .') { commit(data.checkout, sourceText(extra)); }
function unchangedBase(data) { assert.deepEqual(names.map((name) => readFileSync(join(data.baseRoot, 'dist', name))), data.before); }
async function rebuildCandidate(data) {
  const next = join(data.root, 'next');
  mkdirSync(next); cpSync(join(data.baseRoot, 'core'), join(next, 'core'), { recursive: true }); cpSync(join(data.baseRoot, 'links'), join(next, 'links'), { recursive: true });
  cpSync(join(data.baseRoot, 'sources.json'), join(next, 'sources.json'));
  await runCli(['build', '--checkout', `${sourceId}=${data.checkout}`], { root: next });
  rmSync(data.candidateDist, { recursive: true }); cpSync(join(next, 'dist'), data.candidateDist, { recursive: true });
}

test('valid new unlinked term is automatic and never modifies base outputs', async () => {
  const data = await fixture();
  try { advance(data); await rebuildCandidate(data); assert.equal((await verifyRefresh({ baseRoot: data.baseRoot, candidateDist: data.candidateDist, changedPaths: names.map((name) => `dist/${name}`), checkoutOverrides: data.overrides })).mode, 'automatic'); unchangedBase(data); }
  finally { rmSync(data.root, { recursive: true, force: true }); }
});

test('no changed paths needs no source checkout', async () => {
  const data = await fixture();
  try { assert.equal((await verifyRefresh({ baseRoot: data.baseRoot, candidateDist: data.candidateDist, changedPaths: [], checkoutOverrides: data.overrides })).mode, 'none'); unchangedBase(data); }
  finally { rmSync(data.root, { recursive: true, force: true }); }
});

test('tampered and symlink output is rejected', async () => {
  const data = await fixture();
  try {
    writeFileSync(join(data.candidateDist, 'catalog.md'), 'tampered\n');
    await assert.rejects(verifyRefresh({ baseRoot: data.baseRoot, candidateDist: data.candidateDist, changedPaths: ['dist/catalog.md'], checkoutOverrides: data.overrides }), /does not match/i);
    rmSync(join(data.candidateDist, 'catalog.md')); symlinkSync(join(data.baseRoot, 'dist/catalog.md'), join(data.candidateDist, 'catalog.md'));
    await assert.rejects(verifyRefresh({ baseRoot: data.baseRoot, candidateDist: data.candidateDist, changedPaths: ['dist/catalog.md'], checkoutOverrides: data.overrides }), /regular file/i);
    unchangedBase(data);
  } finally { rmSync(data.root, { recursive: true, force: true }); }
});

test('inaccessible configured source fails with source identity', async () => {
  const data = await fixture();
  try { rmSync(data.checkout, { recursive: true }); await assert.rejects(verifyRefresh({ baseRoot: data.baseRoot, candidateDist: data.candidateDist, changedPaths: ['dist/catalog.json'] }), /example\/booking|source/i); unchangedBase(data); }
  finally { rmSync(data.root, { recursive: true, force: true }); }
});

test('candidate is stale if source moves after generation', async () => {
  const data = await fixture();
  try { advance(data); await rebuildCandidate(data); advance(data, ':Desk a owl:Class; rdfs:label "Desk" . :Room a owl:Class; rdfs:label "Room" .'); await assert.rejects(verifyRefresh({ baseRoot: data.baseRoot, candidateDist: data.candidateDist, changedPaths: ['dist/catalog.json'], checkoutOverrides: data.overrides }), /does not match/i); unchangedBase(data); }
  finally { rmSync(data.root, { recursive: true, force: true }); }
});

test('non-generated changes are review-only without a source checkout', async () => {
  const data = await fixture();
  try { rmSync(data.checkout, { recursive: true }); assert.equal((await verifyRefresh({ baseRoot: data.baseRoot, candidateDist: data.candidateDist, changedPaths: ['README.md'] })).mode, 'review'); }
  finally { rmSync(data.root, { recursive: true, force: true }); }
});
