import { after, before, test } from 'node:test';
import assert from 'node:assert/strict';
import { createServer } from 'node:http';
import { mkdtempSync, readFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { act, catalog, checkoutArgs, classify, distBytes, head, namespaceOf, prepare, run, sourceDir, teardown } from './onboarding-walkthrough.mjs';
import { spawnSync } from 'node:child_process';
import { checkDeployed, deployedSteps, optionalChecks } from './onboarding-deployed.mjs';
import { githubGrantProvider } from '../../src/grants.mjs';
import { runCli } from '../../src/cli.mjs';

// One ordered story that automates the scripted walkthrough in 05-acceptance.md. Each test depends on the
// state left by the previous ones, exactly as the walkthrough does.
const parent = mkdtempSync(join(tmpdir(), 'division-onboarding-acceptance-'));
const dir = join(parent, 'w');
const alpha = 'acceptance/alpha', beta = 'acceptance/beta', gamma = 'acceptance/gamma', delta = 'acceptance/delta', epsilon = 'acceptance/epsilon';
const source = (id, division = 'A') => catalog(dir, division).sources.find((item) => item.id === id);
const row = (id, division = 'A') => catalog(dir, division).importStatus.find((item) => item.source === id);
const concepts = (id, division = 'A') => catalog(dir, division).concepts.filter((item) => item.source === id).map((item) => item.label).sort();
const division = (name) => ['sources.json', 'division.json'].map((file) => readFileSync(join(dir, name, file), 'utf8'));

before(async () => { await prepare(dir); });
after(() => { teardown(parent); });

test('B7 a division made from the template with init-division starts with no sources and no status', async () => {
  await run(dir, 'A');
  assert.deepEqual([catalog(dir, 'A').sources, catalog(dir, 'A').importStatus], [[], []]);
  assert.deepEqual(JSON.parse(readFileSync(join(dir, 'A', 'sources.json'), 'utf8')).sources, []);
});

test('B1 adding a repository to the App joins it without touching the division files', async () => {
  const before = division('A');
  act(dir, 'A', 'grant', alpha);
  await run(dir, 'A');
  assert.equal(source(alpha).origin, 'granted');
  assert.deepEqual(division('A'), before);
});

test('B6 the source appears at its exact head commit with every concept', () => {
  assert.equal(source(alpha).commit, head(dir, alpha));
  assert.deepEqual(concepts(alpha), ['Alpha']);
  assert.equal(row(alpha).status, 'current');
});

test('B4 the grant reaches division A only', async () => {
  await run(dir, 'B');
  assert.deepEqual([catalog(dir, 'B').sources, catalog(dir, 'B').importStatus], [[], []]);
});

test('B5 an all-repositories installation admits only configured repositories', async () => {
  act(dir, 'A', 'selection', 'all');
  act(dir, 'A', 'grant', gamma);
  await run(dir, 'A');
  assert.deepEqual([source(gamma), row(gamma)], [undefined, undefined]);
  assert.equal(row(alpha).status, 'current');
});

test('B13 a selected repository without configuration is reported', async () => {
  act(dir, 'A', 'selection', 'selected');
  await run(dir, 'A');
  assert.equal(row(gamma).status, 'not configured');
  assert.equal(source(gamma), undefined);
});

test('B8 a new valid version is imported without a renewed grant', async () => {
  const commit = act(dir, 'A', 'commit', alpha, 'new-version');
  await run(dir, 'A');
  assert.deepEqual([source(alpha).commit, concepts(alpha), row(alpha).status], [commit, ['Alpha', 'NewTerm'], 'current']);
});

test('B9 a change to the repository configuration is followed', async () => {
  act(dir, 'A', 'commit', alpha, 'move-ontology');
  await run(dir, 'A');
  assert.deepEqual([concepts(alpha), row(alpha).status], [['Alpha', 'Moved'], 'current']);
});

test('B11 an invalid new version keeps the last valid one, then recovers', async () => {
  const good = head(dir, alpha);
  const broken = act(dir, 'A', 'commit', alpha, 'break');
  await run(dir, 'A');
  assert.deepEqual([row(alpha).status, row(alpha).attemptedCommit, row(alpha).publishedCommit], ['invalid', broken, good]);
  assert.deepEqual([source(alpha).stale, source(alpha).commit, concepts(alpha)], [true, good, ['Alpha', 'Moved']]);
  const fixed = act(dir, 'A', 'commit', alpha, 'fix');
  await run(dir, 'A');
  assert.deepEqual([row(alpha).status, source(alpha).stale, source(alpha).commit], ['current', false, fixed]);
});

test('B12 and B15 an invalid first version is not published and does not hold back a valid source', async () => {
  act(dir, 'A', 'grant', beta);
  act(dir, 'A', 'grant', delta);
  await run(dir, 'A');
  assert.deepEqual([row(beta).status, source(beta).conceptCount], ['current', 1]);
  assert.deepEqual([row(delta).status, row(delta).publishedCommit, source(delta)], ['invalid', null, undefined]);
});

test('B10 every granted source has a status and no reason contains a credential', () => {
  const current = catalog(dir, 'A');
  for (const granted of current.sources.filter((item) => item.origin === 'granted')) assert.ok(row(granted.id), granted.id);
  for (const item of current.importStatus) {
    assert.ok(['source', 'status', 'attemptedCommit', 'publishedCommit'].every((key) => key in item));
    if (item.status !== 'current') assert.ok(item.reason.code);
  }
  assert.doesNotMatch(JSON.stringify(current) + distBytes(dir, 'A')[1], /ghp_|ghs_|github_pat_|x-access-token/);
});

test('B16 a conflicting namespace is rejected and the holder is unaffected', async () => {
  act(dir, 'A', 'commit', beta, 'claim-namespace', alpha);
  await run(dir, 'A');
  assert.deepEqual([row(beta).status, row(beta).reason.code], ['invalid', 'namespace-conflict']);
  assert.deepEqual([row(alpha).status, source(alpha).namespace], ['current', namespaceOf(alpha)]);
});

test('B14 removed access leaves the last version stale; granting again resumes; a never-published source leaves the table', async () => {
  act(dir, 'A', 'revoke', alpha);
  act(dir, 'A', 'revoke', delta);
  await run(dir, 'A');
  assert.deepEqual([row(alpha).status, row(alpha).publishedCommit, source(alpha).stale, concepts(alpha)], ['access removed', head(dir, alpha), true, ['Alpha', 'Fixed']]);
  assert.equal(row(delta), undefined);
  act(dir, 'A', 'grant', alpha);
  const resumed = act(dir, 'A', 'commit', alpha, 'new-version');
  await run(dir, 'A');
  assert.deepEqual([row(alpha).status, source(alpha).commit], ['current', resumed]);
});

test('B2 a refresh that changes only granted sources is classified automatic', () => {
  assert.deepEqual(classify(dir, 'A'), { mode: 'automatic', reasons: [] });
});

test('B3 a repository both registered and granted appears once, as registered, with no status', async () => {
  act(dir, 'A', 'register', epsilon);
  act(dir, 'A', 'grant', epsilon);
  await run(dir, 'A');
  assert.deepEqual(catalog(dir, 'A').sources.filter((item) => item.id === epsilon).map((item) => item.origin), ['registered']);
  assert.equal(row(epsilon), undefined);
  const result = classify(dir, 'A');
  assert.equal(result.mode, 'review');
  assert.ok(result.reasons.includes('source registration changed'));
});

test('B17 a repeat run over unchanged inputs writes identical files and check passes', async () => {
  const once = distBytes(dir, 'A');
  await run(dir, 'A');
  assert.deepEqual(distBytes(dir, 'A'), once);
  await run(dir, 'A', 'check');
});

test('B1 through the real GitHub client against a local API stand-in', async () => {
  const requests = [];
  const configured = new Set([alpha]);
  const server = createServer((req, res) => {
    requests.push(req.headers.authorization);
    const send = (status, body) => { res.writeHead(status, { 'content-type': 'application/json' }); res.end(JSON.stringify(body)); };
    if (req.url.startsWith('/installation/repositories')) {
      return send(200, { total_count: 2, repository_selection: 'selected', repositories: [alpha, gamma].map((id) => ({ full_name: id, default_branch: 'main' })) });
    }
    const match = /^\/repos\/([^/]+\/[^/]+)\/contents\/\.division-ontology\.json\?ref=main$/.exec(req.url);
    if (match) return send(configured.has(match[1]) ? 200 : 404, {});
    return send(500, {});
  });
  await new Promise((resolve) => server.listen(0, '127.0.0.1', resolve));
  try {
    const provider = githubGrantProvider({ token: 'acceptance-token', apiBase: `http://127.0.0.1:${server.address().port}` });
    await runCli(['build', '--checkout', `${alpha}=${sourceDir(dir, alpha)}`], { root: join(dir, 'B'), grantProvider: provider, urlFor: (id) => sourceDir(dir, id) });
    assert.deepEqual([source(alpha, 'B').origin, row(alpha, 'B').status, row(gamma, 'B').status], ['granted', 'current', 'not configured']);
    assert.ok(requests.length >= 3 && requests.every((header) => header === 'Bearer acceptance-token'));
  } finally { await new Promise((resolve) => server.close(resolve)); }
});

test('the deployed-environment checker reports each kind of discrepancy', () => {
  const sha = (c) => c.repeat(40);
  const good = { sources: [{ id: 'o/a', origin: 'granted', stale: false, commit: sha('a') }, { id: 'o/b', origin: 'granted', stale: true, commit: sha('b') }],
    importStatus: [{ source: 'o/a', status: 'current', attemptedCommit: sha('a'), publishedCommit: sha('a') },
      { source: 'o/b', status: 'access removed', attemptedCommit: null, publishedCommit: sha('b'), reason: { code: 'access-removed', detail: 'x' } },
      { source: 'o/c', status: 'not configured', attemptedCommit: null, publishedCommit: null, reason: { code: 'config-missing', detail: 'x' } }] };
  const heads = { 'o/a': sha('a') };
  const expectations = [{ source: 'o/a', status: 'current' }, { source: 'o/b', status: 'access removed' }, { source: 'o/c', status: 'not configured' }, { source: 'o/d', status: 'absent' }];
  assert.deepEqual(checkDeployed({ catalog: good, heads, expectations }), []);
  assert.match(checkDeployed({ catalog: good, heads: { 'o/a': sha('z') }, expectations })[0], /o\/a: expected commit/);
  assert.match(checkDeployed({ catalog: good, heads, expectations: [{ source: 'o/a', status: 'invalid' }] })[0], /expected invalid, found current/);
  assert.match(checkDeployed({ catalog: good, heads, expectations: [{ source: 'o/a', status: 'absent' }] })[0], /expected absent/);
  assert.match(checkDeployed({ catalog: good, heads, expectations: [{ source: 'o/z', status: 'current' }] })[0], /no import status row/);
});

test('the deployed checker understands registered and absent sources', () => {
  const catalog = { sources: [{ id: 'o/r', origin: 'registered', stale: false, commit: 'a'.repeat(40) }], importStatus: [] };
  assert.deepEqual(checkDeployed({ catalog, heads: {}, expectations: [{ source: 'o/r', status: 'registered' }, { source: 'o/none', status: 'absent' }] }), []);
  assert.match(checkDeployed({ catalog, heads: {}, expectations: [{ source: 'o/r', status: 'absent' }] })[0], /expected absent/);
  assert.match(checkDeployed({ catalog, heads: {}, expectations: [{ source: 'o/none', status: 'registered' }] })[0], /expected one registered/);
});

test('every behaviour B1-B17 has an executable check in this file', () => {
  const titles = [...readFileSync(new URL(import.meta.url), 'utf8').matchAll(/^test\('([^']*)'/gm)].map((match) => match[1]);
  for (let n = 1; n <= 17; n++) assert.ok(titles.some((title) => new RegExp(`\\bB${n}\\b`).test(title)), `an executable check names B${n}`);
});

test('the deployed run refuses to start without its repositories', () => {
  const result = spawnSync(process.execPath, [join(import.meta.dirname, 'onboarding-deployed.mjs'), 'run', 'o/division'], { encoding: 'utf8' });
  assert.equal(result.status, 1);
  assert.match(result.stderr, /usage: onboarding-deployed\.mjs run/);
});

test('the deployed dry run lists every action and expected result without touching GitHub', () => {
  const result = spawnSync(process.execPath, [join(import.meta.dirname, 'onboarding-deployed.mjs'), 'run', 'o/division', '--alpha', 'o/alpha', '--beta', 'o/beta', '--gamma', 'o/gamma', '--installation', '123', '--dry-run'], { encoding: 'utf8', env: { ...process.env, PATH: '/nonexistent' } });
  assert.equal(result.status, 0, result.stderr);
  assert.match(result.stdout, /D2 — B1, B6, B2[\s\S]*would: add o\/alpha to the source-read App/);
  assert.match(result.stdout, /D11 — B17[\s\S]*dispatch the sync twice/);
  assert.doesNotMatch(result.stdout, /EXCEPTION/);
});

test('every behaviour B1-B17 has a deployed step or a one-command check', () => {
  const steps = deployedSteps({ alpha: 'o/a', beta: 'o/b', gamma: 'o/g' });
  const optional = optionalChecks({ division: 'o/d', alpha: 'o/a', gamma: 'o/g' });
  const covered = new Set([...steps, ...optional].flatMap((item) => item.behaviours.split(',').map((id) => id.trim())));
  for (let n = 1; n <= 17; n++) assert.ok(covered.has(`B${n}`), `B${n} is covered`);
  for (const step of steps) assert.ok(step.expect.length && (step.expectations || step.empty), step.id);
  for (const check of optional) assert.match(check.command, /onboarding-deployed\.mjs --sync/);
});

test('the manual-app dry run keeps commits automatic and leaves only App changes to you', () => {
  const result = spawnSync(process.execPath, [join(import.meta.dirname, 'onboarding-deployed.mjs'), 'run', 'o/division', '--alpha', 'o/alpha', '--beta', 'o/beta', '--gamma', 'o/gamma', '--manual-app', '--dry-run'], { encoding: 'utf8', env: { ...process.env, PATH: '/nonexistent' } });
  assert.equal(result.status, 0, result.stderr);
  assert.match(result.stdout, /would: ask you to do in your browser: add o\/gamma to the source-read App/);
  assert.match(result.stdout, /would: commit docs\/ontology\.ttl to o\/alpha/);
  assert.doesNotMatch(result.stdout, /would: ask you to do in your browser: commit/);
});

test('the deployed run can start from a later step and rejects an unknown one', () => {
  const run = (...extra) => spawnSync(process.execPath, [join(import.meta.dirname, 'onboarding-deployed.mjs'), 'run', 'o/division', '--alpha', 'o/alpha', '--beta', 'o/beta', '--gamma', 'o/gamma', '--manual-app', '--dry-run', ...extra], { encoding: 'utf8', env: { ...process.env, PATH: '/nonexistent' } });
  const later = run('--from', 'D2');
  assert.equal(later.status, 0, later.stderr);
  assert.doesNotMatch(later.stdout, /=== D1 /);
  assert.match(later.stdout, /=== D2 /);
  const unknown = run('--from', 'D99');
  assert.equal(unknown.status, 1);
  assert.match(unknown.stderr, /--from D99 is not a step/);
});
