import assert from 'node:assert/strict';
import { randomUUID } from 'node:crypto';
import { mkdir } from 'node:fs/promises';
import { join, resolve } from 'node:path';
import { parseArgs } from 'node:util';
import { chromium } from 'playwright';
import { verifyGraphDelivery, withFinalChecks } from '../verify-graph-explorer.mjs';
import { api, captureInputs, fixtureLedger, graphSession, history, readJson, saveJson, seed, traverse } from './fixture.mjs';
import { b1, b2, b3, b33, b4, b43, b5, b6 } from './navigation.mjs';
import { b8, b9, b10 } from './paging.mjs';
import { b7, b11, b12, b13, b14 } from './contributions.mjs';

const cases = { B1: [b1], B2: [b2], B3: [b3, b33], B4: [b4, b43], B5: [b5], B6: [b6], B7: [b7],
  B8: [b8], B9: [b9], B10: [b10], B11: [b11], B12: [b12], B13: [b13], B14: [b14], B15: [] };
const writes = new Set(['B3', 'B4', 'B5', 'B7', 'B8', 'B10', 'B11', 'B12', 'B13', 'B14', 'B15']);
const paging = new Set(['B3', 'B4', 'B5', 'B8', 'B10', 'B11', 'B12', 'B14']);
const digest = /^[a-f0-9]{64}$/;
const usage = 'Usage: run.mjs --fixture-root PATH --snapshot DIGEST --evidence-dir PATH ' +
  '(--deployment FILE --expected-revision SHA --origin HTTPS_ORIGIN | --local-origin LOOPBACK_ORIGIN) ' +
  '[--case B1 ...] [--allow-fixture-writes] [--local-ingress] [--headed]';
const credential = (role, part) => process.env['WORKSPACE_VERIFY_' + role.toUpperCase() + '_' + part];
const snapshotLabel = page => page.locator('main p').filter({ hasText: /^Active snapshot / }).locator('code');

class RejectedLogin extends Error {
  constructor(role) {
    super('Keycloak rejected the ' + role + ' fixture credentials');
    this.role = role;
  }
}

function failureSummary(error) {
  if (error instanceof AggregateError) return error.errors.map(failureSummary).join(' | ');
  if (error instanceof RejectedLogin) return error.message;
  // Playwright errors may contain URLs, headers or values filled into forms.
  const source = error?.stack?.match(/graph-regression\/([a-z]+\.mjs:\d+):\d+/)?.[1];
  if (error instanceof assert.AssertionError) {
    if (typeof error.expected === 'number' && typeof error.actual === 'number') {
      return `expected ${error.expected}, got ${error.actual}`;
    }
    return source ? 'assertion failed (' + source + ')' : 'assertion failed';
  }
  const kind = error?.name === 'TimeoutError' ? 'timed out waiting for the browser' :
    error?.name === 'ReferenceError' ? 'missing test reference' : 'could not complete the step';
  return source ? kind + ' (' + source + ')' : kind;
}

async function main() {
  const { values: options } = parseArgs({ options: {
    'fixture-root': { type: 'string' }, snapshot: { type: 'string' }, 'evidence-dir': { type: 'string' },
    deployment: { type: 'string' }, 'expected-revision': { type: 'string' }, origin: { type: 'string' },
    'local-origin': { type: 'string' }, 'local-ingress': { type: 'boolean' }, headed: { type: 'boolean' },
    case: { type: 'string', multiple: true }, operator: { type: 'string' },
    'allow-fixture-writes': { type: 'boolean' }, help: { type: 'boolean' }
  } });
  if (options.help) {
    console.log(usage);
    return;
  }

  const snapshot = options.snapshot;
  assert.ok(digest.test(snapshot) && options['fixture-root'] && options['evidence-dir']);
  const requested = new Set(options.case ?? Object.keys(cases));
  assert.ok([...requested].every(id => Object.hasOwn(cases, id)), 'Unknown Graph Explorer behaviour');
  const selected = new Set(requested);
  if (selected.has('B15')) selected.add('B13');
  if (selected.has('B13')) selected.add('B12');
  const willWrite = [...selected].some(id => writes.has(id));
  const needsPaging = [...selected].some(id => paging.has(id));
  if (willWrite && !options['allow-fixture-writes']) {
    console.error('FAIL | write approval | expected: --allow-fixture-writes | actual: missing');
    process.exitCode = 1;
    return;
  }

  let origin, issuer, revision;
  if (options['local-origin']) {
    const local = new URL(options['local-origin']);
    assert.ok(local.protocol === 'http:' && ['localhost', '127.0.0.1', 'workspace.localhost'].includes(local.hostname));
    origin = local.origin;
    issuer = origin;
  } else {
    const deployment = await readJson(options.deployment);
    const target = new URL(options.origin);
    const authority = new URL(deployment.issuer);
    assert.equal(target.protocol, 'https:');
    assert.equal(authority.protocol, 'https:');
    assert.equal(target.hostname, deployment.publicHost);
    assert.equal(options.origin, target.origin);
    assert.match(options['expected-revision'], /^[a-f0-9]{40}$/);
    if (deployment.commit !== options['expected-revision']) {
      const actual = /^[a-f0-9]{40}$/.test(deployment.commit) ? deployment.commit : 'invalid deployment evidence';
      console.error(`FAIL | deployed revision | expected: ${options['expected-revision']} | actual: ${actual}`);
      process.exitCode = 1;
      return;
    }
    origin = target.origin;
    issuer = authority.origin;
    revision = deployment.commit;
  }

  const roles = willWrite ? ['reader', 'manager'] : ['reader'];
  assert.ok(roles.every(role => credential(role, 'USERNAME') && credential(role, 'PASSWORD')),
    'Fixture account environment variables');
  const run = randomUUID().slice(0, 8);
  const directory = join(resolve(options['evidence-dir']), 'graph-regression-' + randomUUID());
  await mkdir(resolve(options['evidence-dir']), { recursive: true });
  await mkdir(directory, { mode: 0o700 });
  console.log('Evidence directory: ' + directory);
  const report = { feature: 145, run, started: new Date().toISOString(), operator: options.operator, origin, snapshot, revision,
    status: 'running',
    requested: [...requested], selected: Object.keys(cases).filter(id => selected.has(id)), steps: [] };
  const evidence = (name, value) => saveJson(join(directory, name), value);
  await evidence('run.json', report);
  function result(status, test, expected, actual) {
    report.steps.push({ status, test, expected, actual });
    console.log(`${status} | ${test} | expected: ${expected} | actual: ${actual}`);
  }
  const pass = (...args) => result('PASS', ...args);
  let currentStage = 'read the pinned fixture and launch Chromium';
  const stage = name => { currentStage = name; };
  const ledger = fixtureLedger(directory);
  let browser, reader, manager, baseline, originalConnections, lateRelationship, savedRecords, edits;
  let readyToRun = false;

  async function enterCredentials(page, role) {
    assert.equal(new URL(page.url()).origin, issuer, 'Keycloak origin');
    await page.locator('#username').fill(credential(role, 'USERNAME'));
    await page.locator('#password').fill(credential(role, 'PASSWORD'));
    await page.locator('#kc-login').click();
  }

  async function login(role) {
    const page = await browser.newPage({ viewport: { width: 1440, height: 1000 } });
    page.setDefaultTimeout(30000);
    await page.goto(origin);
    await page.getByRole('button', { name: 'Sign in to the workspace', exact: true }).click();
    await page.locator('#username').waitFor();
    await enterCredentials(page, role);
    const signedIn = page.getByRole('heading', { name: 'Published inventory', exact: true });
    const rejected = page.locator('#input-error, #kc-content .alert-error');
    await signedIn.or(rejected).first().waitFor();
    if (!(await signedIn.isVisible()) && await rejected.isVisible()) throw new RejectedLogin(role);
    assert.equal(new URL(page.url()).origin, origin);
    const actual = await snapshotLabel(page).innerText();
    if (actual !== snapshot) {
      result('FAIL', 'active snapshot', snapshot, digest.test(actual) ? actual : 'unavailable');
      throw new Error('Active snapshot differs from the fixture');
    }
    return page;
  }

  async function runCase(id) {
    stage('start ' + id);
    const contexts = [];
    const caseLogin = async role => {
      const page = await login(role);
      contexts.push(page.context());
      return page;
    };
    const context = { origin, snapshot, run, lateRelationship, savedRecords, login: caseLogin, enterCredentials,
      track: ledger.track, stage, pass };
    await withFinalChecks(async () => {
      for (const scenario of cases[id]) {
        const data = await scenario(context);
        if (id === 'B12') savedRecords = data;
        if (id === 'B13') edits = data;
      }
    }, async () => {
      const results = await Promise.allSettled(contexts.map(context => context.close()));
      if (results.some(result => result.status === 'rejected')) throw new Error('Could not close a browser context');
    });
  }

  const errors = [];
  async function checkCase(id) {
    try {
      await runCase(id);
    } catch (error) {
      result('FAIL', id, currentStage, failureSummary(error));
      errors.push(error);
    }
  }

  try {
    await withFinalChecks(async () => {
      baseline = await captureInputs(options['fixture-root'], snapshot);
      await evidence('inputs-before.json', baseline);
      const hosts = [...new Set([new URL(origin).hostname, new URL(issuer).hostname])];
      const args = options['local-ingress'] || options['local-origin']
        ? ['--host-resolver-rules=' + hosts.map(host => 'MAP ' + host + ' 127.0.0.1').join(','), '--no-proxy-server'] : [];
      browser = await chromium.launch({ args, headless: !(options.headed || (process.platform === 'win32' && selected.has('B4'))) });

      stage('authenticate and verify the live fixture');
      reader = await graphSession(await login('reader'), origin, snapshot);
      if (willWrite) manager = await graphSession(await login('manager'), origin, snapshot);
      for (const [session, canManage] of [[reader, false], ...(manager ? [[manager, true]] : [])]) {
        const reply = await api(session.page, session.authorization, '/api/session');
        assert.equal(reply.status, 200);
        assert.deepEqual(reply.json, { canRead: true, canManage }, 'Fixture account capabilities');
      }
      pass('preflight snapshot', snapshot, snapshot);
      if (revision) pass('preflight deployed revision', revision, revision);
      readyToRun = true;
      originalConnections = await traverse(reader, snapshot);
      if (selected.has('B1')) {
        stage('delivery of Graph Explorer and its JavaScript files');
        await verifyGraphDelivery({ page: reader.page, origin });
        pass('graph delivery', 'direct graph route serves the workspace and two JavaScript modules',
          'workspace HTML and same-origin JavaScript verified');
      }
      if (selected.has('B15')) pass('B15.1', 'Pinned manifest and four clean checkouts containing seven Turtle files',
        'Manifest digest, all Git revisions and seven input hashes verified');
      for (const id of ['B1', 'B2'].filter(id => selected.has(id))) await checkCase(id);
      if (needsPaging) {
        stage('seed temporary paging relationships');
        await seed(manager, run, ledger.track);
        const pages = await traverse(reader, snapshot);
        const late = pages.slice(1).flat().find(step => step.label.startsWith('paging-acceptance-' + run + '-'));
        assert.ok(late?.runtimeId, 'A seeded relationship must appear beyond page one');
        lateRelationship = { id: late.runtimeId };
        pass('paging fixture', '105 exact run records including a relationship on a later page',
          `${ledger.records.size} temporary records, late relationship ${late.runtimeId}`);
      }

      for (const id of Object.keys(cases).filter(id => selected.has(id) && !['B1', 'B2', 'B15'].includes(id))) {
        if (id === 'B13' && !savedRecords) {
          result('FAIL', id, 'B12 saved records from this run', 'B12 did not complete');
          continue;
        }
        await checkCase(id);
      }
      if (selected.has('B15')) {
        stage('verify the live journal contains the run\'s five expected events');
        try {
          assert.ok(savedRecords && edits);
          const [notes, edges] = await Promise.all([history(reader, savedRecords.note.id), history(reader, savedRecords.relationship.id)]);
          assert.deepEqual(notes.map(event => [event.type, event.revision]), edits.noteHistory);
          assert.deepEqual(edges.map(event => [event.type, event.revision]), edits.relationshipHistory);
          assert.ok([...notes, ...edges].every(event => event.snapshotId === snapshot));
          pass('B15.2', 'Five creation, edit and removal events refer to the pinned snapshot',
            `${notes.length + edges.length} exact journal events under ${snapshot}`);
        } catch (error) {
          result('FAIL', 'B15.2', currentStage, failureSummary(error));
          errors.push(error);
        }
      }
      if (errors.length) throw new AggregateError(errors, 'Graph Explorer behaviours failed');
    }, async () => {
      if (!manager || !ledger.records.size) return;
      stage('remove only temporary records captured by this run');
      // Get a fresh manager session so a long run does not expire the cleanup token.
      const fresh = await graphSession(await login('manager'), origin, snapshot).catch(() => manager);
      const failures = await ledger.cleanup(fresh);
      if (failures.length) {
        result('FAIL', 'fixture cleanup', 'all tracked run records removed', 'unconfirmed IDs: ' + failures.join(', '));
        throw new Error('Temporary records remain');
      }
      pass('fixture cleanup', 'all tracked run records removed', `${ledger.records.size} exact IDs removed`);
    }, async () => {
      if (!baseline) return;
      const test = selected.has('B15') ? 'B15.3' : 'final invariants';
      const expected = 'same snapshot, original connections and unchanged repository inputs';
      try {
        const after = await captureInputs(options['fixture-root'], snapshot);
        await evidence('inputs-after.json', after);
        assert.deepEqual(after, baseline);
        if (readyToRun) {
          const fresh = await graphSession(await login('reader'), origin, snapshot);
          const remaining = (await traverse(fresh, snapshot)).flat();
          const ids = items => items.flat().map(step => step.runtimeId ?? JSON.stringify(step.key)).sort();
          if (originalConnections) assert.deepEqual(ids(remaining), ids(originalConnections));
          assert.ok(remaining.every(step => !ledger.records.has(step.runtimeId)));
          pass(test, expected,
            `same snapshot and ${remaining.length} original connections, four clean checkouts, seven identical Turtle hashes`);
        }
      } catch (error) {
        result('FAIL', test, expected, failureSummary(error));
        throw error;
      }
    }, () => browser?.close());
    report.status = 'pass';
  } catch (error) {
    report.status = 'fail';
    if (!report.steps.some(step => step.status === 'FAIL')) {
      result('FAIL', 'suite', currentStage, failureSummary(error));
    }
    // Only preflight credential rejection can trigger a retry. Never replay a run that has written records.
    process.exitCode = !readyToRun && error instanceof RejectedLogin ? (error.role === 'reader' ? 21 : 22) : 1;
  } finally {
    report.finished = new Date().toISOString();
    await evidence('run.json', report);
  }
}

main().catch(error => {
  console.error('FAIL | runner setup | expected: valid configuration and installed browser dependencies | actual: ' + failureSummary(error));
  console.error(usage);
  process.exitCode = 1;
});
