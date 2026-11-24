import test from 'node:test';
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { execFileSync } from 'node:child_process';
import { appendFile, mkdtemp, readFile, rm, writeFile } from 'node:fs/promises';
import { join } from 'node:path';
import { tmpdir } from 'node:os';
import { setup, teardown } from './fixtures/representative/setup.mjs';
import { runCli } from '../src/cli.mjs';
import { buildBundle, writeBundle } from '../src/workspace/bundle.mjs';

const git = (cwd, ...args) => execFileSync('git', args, { cwd, stdio: ['ignore', 'pipe', 'pipe'] });
const hash = data => createHash('sha256').update(data).digest('hex');
const published = bundle => JSON.parse(bundle.blobs.get(
  bundle.manifest.files.find(file => file.role === 'projection').sha256));

async function withFixture(run) {
  const parent = await mkdtemp(join(tmpdir(), 'workspace-bundle-'));
  const root = join(parent, 'fixture');
  try {
    const fixture = await setup(root);
    const args = { divisionRoot: fixture.divisionRoot, catalogCommit: fixture.commit,
      registryCommit: fixture.commit, checkouts: fixture.checkouts };
    await run(args, parent);
  } finally {
    try { teardown(root); } catch (error) { if (error.code !== 'ENOENT') throw error; }
    await rm(parent, { recursive: true, force: true });
  }
}

function commit(cwd, title) {
  git(cwd, 'add', '.');
  git(cwd, '-c', 'commit.gpgsign=false', 'commit', '-qm', title);
  return git(cwd, 'rev-parse', 'HEAD').toString().trim();
}

async function republish(args) {
  const flags = [...args.checkouts].flatMap(([id, path]) => ['--checkout', `${id}=${path}`]);
  await runCli(['build', ...flags], { root: args.divisionRoot });
  return { ...args, catalogCommit: commit(args.divisionRoot, 'Publish fixture catalog') };
}

test('bundles pinned bytes and keeps equal labels in different sources distinct', () => withFixture(async args => {
  const bundle = buildBundle(args);
  assert.equal(bundle.id, hash(bundle.manifestBytes));
  assert.equal(bundle.manifest.divisionCommit, args.catalogCommit);
  const catalog = git(args.divisionRoot, 'show', `${args.catalogCommit}:dist/catalog.json`);
  assert.equal(bundle.manifest.catalogSha256, hash(catalog));
  assert.deepEqual(bundle.blobs.get(hash(catalog)), catalog);
  for (const file of bundle.manifest.files) {
    const bytes = bundle.blobs.get(file.sha256);
    assert.equal(bytes.length, file.bytes);
    assert.equal(hash(bytes), file.sha256);
  }
  const projection = published(bundle);
  assert.equal(projection.entities.filter(entity => entity.label === 'Order').length, 3);
  const bindings = projection.edges.filter(edge => edge.key.kind === 'c4Binding');
  assert.equal(bindings.length, 2);
  assert.ok(bindings.every(edge => edge.from.sourceId !== edge.to.sourceId));
  assert.ok(bundle.manifest.files.some(file => file.role === 'graphEvidence'));
  assert.deepEqual(buildBundle({ ...args, checkouts: new Map([...args.checkouts].reverse()) }).manifestBytes,
    bundle.manifestBytes);
}));

test('rejects an absent checkout and a checkout from the wrong repository', () => withFixture(args => {
  assert.throws(() => buildBundle({ ...args, checkouts: new Map() }), /checkout/i);
  const wrong = new Map(args.checkouts);
  wrong.set('fixture/ecommerce', wrong.get('fixture/warehouse'));
  assert.throws(() => buildBundle({ ...args, checkouts: wrong }), /origin identity/i);
}));

test('rejects an evidence blob changed by a later source commit', () => withFixture(async args => {
  const checkout = args.checkouts.get('fixture/ecommerce');
  await appendFile(join(checkout, 'docs/architecture.md'), 'New line without updating graph evidence.\n');
  commit(checkout, 'Stale evidence');
  const changed = await republish(args);
  assert.throws(() => buildBundle(changed), /evidence blob mismatch/);
  assert.equal(buildBundle(args).manifest.divisionCommit, args.catalogCommit);
}));

test('publishes a gap for an optional source graph and excludes stale bindings', () => withFixture(async args => {
  const checkout = args.checkouts.get('fixture/warehouse');
  await rm(join(checkout, 'ontology.config.json'));
  commit(checkout, 'Disable the optional graph');
  const bundle = buildBundle(await republish(args));
  const projection = published(bundle);
  assert.equal(bundle.manifest.sources.find(source => source.id === 'fixture/warehouse').graphStatus, 'unavailable');
  assert.ok(projection.entities.some(entity => entity.key.sourceId === 'fixture/warehouse'));
  assert.ok(projection.gaps.some(gap => gap.code === 'graph-unavailable' && gap.sourceId === 'fixture/warehouse'));
  assert.ok(projection.gaps.some(gap => gap.code === 'stale-binding'));
  assert.ok(!projection.edges.some(edge => edge.key.kind === 'c4Binding' && edge.to.sourceId === 'fixture/warehouse'));
}));

test('writes a complete content-addressed directory and refuses tampered bytes', () => withFixture(async (args, parent) => {
  const bundle = buildBundle(args);
  const output = join(parent, 'output');
  writeBundle(bundle, output);
  assert.deepEqual(await readFile(join(output, 'manifests', `${bundle.id}.json`)), bundle.manifestBytes);
  const file = bundle.manifest.files[0];
  assert.deepEqual(await readFile(join(output, 'blobs', file.sha256)), bundle.blobs.get(file.sha256));
  await writeFile(join(output, 'keep.txt'), 'keep');
  assert.throws(() => writeBundle(bundle, output), /exists/i);
  assert.equal(await readFile(join(output, 'keep.txt'), 'utf8'), 'keep');

  const corrupt = { ...bundle, blobs: new Map(bundle.blobs).set(file.sha256, Buffer.from('tampered')) };
  const refused = join(parent, 'refused');
  assert.throws(() => writeBundle(corrupt, refused), /digest|length/i);
  await assert.rejects(readFile(join(refused, 'manifests', `${bundle.id}.json`)), { code: 'ENOENT' });
}));

test('bundles mixed graph versions under the existing envelope', () => withFixture(async args => {
  const checkout = args.checkouts.get('fixture/ecommerce');
  const graphFile = join(checkout, 'graph/manifest.ttl');
  const turtle = await readFile(graphFile, 'utf8');
  const schemaOne = '#schemaVersion> "1"';
  const legacyRole = '#implementation>';
  assert.ok(turtle.includes(schemaOne) && turtle.includes(legacyRole));
  await writeFile(graphFile, turtle.replace(schemaOne, '#schemaVersion> 2')
    .replace(legacyRole, '#role-implementation>'));
  const upgradedSource = commit(checkout, 'Publish version 2 fixture graph');
  const absentCheckout = args.checkouts.get('fixture/warehouse');
  await rm(join(absentCheckout, 'ontology.config.json'));
  commit(absentCheckout, 'Disable optional fixture graph');

  const bundle = buildBundle(await republish(args));
  assert.equal(bundle.manifest.schemaVersion, 1);
  const sources = new Map(bundle.manifest.sources.map(source => [source.id, source]));
  assert.equal(sources.get('fixture/ecommerce').commit, upgradedSource);
  assert.equal(sources.get('fixture/ecommerce').graphSchemaVersion, 2);
  assert.equal(sources.get('fixture/accounting').graphSchemaVersion, 1);
  assert.equal(sources.get('fixture/warehouse').graphStatus, 'unavailable');
  assert.equal(sources.get('fixture/warehouse').graphSchemaVersion, null);
  assert.ok(bundle.manifest.files.some(file => file.sourceId === 'fixture/ecommerce' && file.role === 'graphTurtle'));
  assert.ok(published(bundle).entities.some(entity => entity.key.sourceId === 'fixture/ecommerce' && entity.key.kind === 'node'));
  assert.ok(published(bundle).edges.some(edge => edge.provenance.evidence.some(evidence => evidence.role === 'implementation')));
}));
