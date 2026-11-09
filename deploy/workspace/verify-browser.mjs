import assert from 'node:assert/strict';
import { readFile, writeFile } from 'node:fs/promises';
import { join } from 'node:path';
import { setTimeout as delay } from 'node:timers/promises';
import { chromium } from 'playwright';

const target = process.argv[2];
const homeLab = target === 'home-lab';
const origin = homeLab ? process.env.WORKSPACE_VERIFY_ORIGIN
  : `http://workspace.localhost:${process.env.WORKSPACE_PORT ?? '8087'}`;
const modelRevision = homeLab ? process.env.WORKSPACE_VERIFY_MODEL_REVISION
  : 'ac6da0dfba84a81fdbfbaf330198c33cd77c4cdfc53e8bc50eb581914a15621d';
const appVersion = process.env.WORKSPACE_VERIFY_APP_VERSION ?? (homeLab ? null : 'working-tree');
const scenario = process.env.WORKSPACE_SCENARIO_DIR;
const persistence = process.argv[3] === '--check-persistence';
assert.ok(origin && (!homeLab || new URL(origin).protocol === 'https:'), 'Set the HTTPS home-lab fixture origin.');
assert.ok(modelRevision && appVersion, 'Record the fixture embedding model revision and application version.');
assert.ok(scenario, 'Set WORKSPACE_SCENARIO_DIR to a dedicated fixture directory.');
const snapshot = (await readFile(join(scenario, 'snapshot'), 'utf8')).trim();
const stateFile = join(scenario, 'scenario.json');
const from = { sourceId: 'fixture/ecommerce', kind: 'concept', identifier: 'https://example.test/ecommerce/ontology#Order' };
const to = { sourceId: 'fixture/warehouse', kind: 'concept', identifier: 'https://example.test/warehouse/ontology#Order' };
const entityPath = key => `/entity/${Buffer.from(JSON.stringify(key)).toString('base64url')}`;
const note = 'Finance approvers reconcile synthetic storefront orders before dispatch.';
const pilotQueries = [
  ['What happens after a customer places an order?', 'ecommerce', 'node', 'graph#component'],
  ['How does Accounting learn that an order has shipped?', 'accounting', 'concept', 'ontology#Order']
];
const browser = await chromium.launch({
  executablePath: process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE,
  args: homeLab ? [] : ['--host-resolver-rules=MAP workspace.localhost 127.0.0.1']
});

async function login(role) {
  const context = await browser.newContext();
  const page = await context.newPage();
  const failed = [];
  page.on('response', response => {
    if (response.status() >= 400) failed.push(`${response.status()} ${new URL(response.url()).pathname}`);
  });
  page.on('requestfailed', request => failed.push(`${request.failure().errorText} ${new URL(request.url()).pathname}`));
  const account = role.toUpperCase();
  const username = homeLab ? process.env[`WORKSPACE_VERIFY_${account}_USERNAME`] : `workspace-${role}`;
  const password = process.env[`WORKSPACE_${homeLab ? 'VERIFY' : 'DEV'}_${account}_PASSWORD`];
  assert.ok(username && password, `Provide the controlled ${role} fixture credentials in the environment.`);
  await page.goto(origin);
  await page.getByRole('button', { name: 'Sign in to the workspace' }).click();
  try {
    await page.locator('#username').fill(username);
    await page.locator('#password').fill(password);
    await page.locator('#kc-login').click();
  } catch {
    throw new Error(`The ${role} browser could not complete the Keycloak form.`);
  }
  try {
    await page.getByRole('heading', { name: 'Published inventory' }).waitFor({ timeout: 15_000 });
  } catch {
    const at = new URL(page.url()).pathname;
    const message = await page.locator('[role=alert], #kc-content, main').first().innerText({ timeout: 1000 }).catch(() => '');
    throw new Error(`${role} sign-in stopped at ${at}. ${failed.slice(-8).join(', ')}. ${message.slice(0, 400)}`);
  }
  await page.getByText(snapshot, { exact: true }).waitFor();
  const request = page.waitForRequest(candidate => new URL(candidate.url()).pathname === '/api/session');
  await page.goto(origin + entityPath(from));
  const headers = await (await request).allHeaders();
  assert.match(headers.authorization ?? '', /^Bearer [^ ]+$/);
  await page.getByRole('heading', { name: 'Order', exact: true }).waitFor();
  return {
    page,
    close: () => context.close(),
    async request(path, body, status = 200) {
      const response = await page.evaluate(async ({ path, body, authorization }) => {
        const response = await fetch(path, {
          method: body === undefined ? 'GET' : 'POST',
          body: body === undefined ? undefined : JSON.stringify(body),
          headers: { authorization, 'content-type': 'application/json' }
        });
        return { status: response.status, data: response.ok ? await response.json() : null };
      }, { path, body, authorization: headers.authorization });
      assert.equal(response.status, status, path);
      return response.data;
    }
  };
}

async function waitForIndexed(client, id) {
  const started = performance.now();
  for (let attempt = 0; attempt < 90; attempt++) {
    const queriedAt = performance.now();
    const result = await client.request('/api/search', { query: note });
    if (result.hits.some(hit => hit.annotationId === id)) {
      console.log(`Qwen ${modelRevision}: fixture annotation retrieved in ${Math.round(performance.now() - started)} ms, including an exact query of ${Math.round(performance.now() - queriedAt)} ms.`);
      return;
    }
    await delay(1000);
  }
  throw new Error('The Qwen index did not retrieve the current fixture annotation.');
}

async function checkOrderFulfilment(reader) {
  const { page } = reader;
  await page.goto(origin);
  for (const [question, source, kind, identifier] of pilotQueries) {
    const pending = page.waitForResponse(response => new URL(response.url()).pathname === '/api/search');
    await page.getByRole('searchbox', { name: 'Semantic search' }).fill(question);
    await page.getByRole('button', { name: 'Search', exact: true }).click();
    const response = await pending;
    assert.ok(response.ok(), `B13 search failed: ${question} (HTTP ${response.status()})`);
    const result = await response.json();
    assert.equal(result.snapshotId, snapshot, 'B13 search snapshot');
    const key = { sourceId: `fixture/${source}`, kind, identifier: `https://example.test/${source}/${identifier}` };
    const hit = result.hits.find(item => item.entityKey.sourceId === key.sourceId &&
      item.entityKey.kind === key.kind && item.entityKey.identifier === key.identifier);
    assert.ok(hit, `B13 missing search lead: ${question} (${key.sourceId} ${key.identifier})`);
    assert.match(hit.provenance.sourceCommit, /^[a-f0-9]{40}$/, 'B13 search result source commit');
    await page.getByRole('region', { name: 'Search results' }).locator(`a[href="${entityPath(key).slice(1)}"]`).first().waitFor();
    console.log(JSON.stringify({ behaviour: 'B13', question, source: key.sourceId, identifier: key.identifier,
      sourceCommit: hit.provenance.sourceCommit, snapshot }));
  }

  const start = { sourceId: 'fixture/ecommerce', kind: 'node', identifier: 'https://example.test/ecommerce/graph#system' };
  await page.goto(origin + entityPath(start));
  await page.getByRole('heading', { name: 'Ecommerce', exact: true }).waitFor();
  await page.getByLabel('Maximum depth').fill('4');
  const pending = page.waitForResponse(response => new URL(response.url()).pathname === '/api/traverse');
  await page.getByRole('button', { name: 'Traverse', exact: true }).click();
  const response = await pending;
  assert.ok(response.ok(), `B13 traversal failed (HTTP ${response.status()})`);
  const paths = await response.json();
  assert.equal(paths.snapshotId, snapshot, 'B13 traversal snapshot');
  const handoff = paths.paths.find(({ steps }) => steps.length === 4 &&
    steps[1].key?.kind === 'c4Binding' && steps[1].to.sourceId === 'fixture/warehouse' &&
    steps[3].key?.kind === 'c4Binding' && steps[3].to.sourceId === 'fixture/accounting');
  assert.ok(handoff, 'B13 missing Ecommerce → Warehouse → Accounting handoff');
  for (const step of handoff.steps) {
    assert.match(step.provenance.sourceCommit, /^[a-f0-9]{40}$/, 'B13 handoff source commit');
    assert.ok(step.provenance.evidence.some(evidence => evidence.path === 'docs/architecture.md'),
      `B13 missing evidence for ${step.label}`);
  }
  await page.getByRole('heading', { name: 'Entity context', exact: true }).waitFor();
  console.log(JSON.stringify({ behaviour: 'B13', snapshot, handoff: handoff.steps.map(step => ({
    from: step.from.sourceId, to: step.to.sourceId, kind: step.key.kind, sourceCommit: step.provenance.sourceCommit
  })) }));
}

async function checkArchitecturalRelationship(manager, reader) {
  const component = source => ({
    sourceId: `fixture/${source}`, kind: 'node', identifier: `https://example.test/${source}/graph#component`
  });
  const from = component('ecommerce');
  const to = component('warehouse');
  const publishedType = { sourceId: from.sourceId, kind: 'relationshipType',
    identifier: 'https://ontology-kit.dev/schema#communicates-with' };
  const created = await manager.request('/api/contributions/relationships', {
    from, to, meaning: { publishedType }
  }, 201);
  try {
    const detail = await reader.request('/api/relationships/detail', { runtimeId: created.id });
    const step = detail.relationship;
    assert.equal(detail.snapshotId, snapshot, 'B19 relationship snapshot');
    assert.equal(step.origin, 'runtime', 'B19 relationship origin');
    assert.deepEqual(step.from, from, 'B19 architectural source component');
    assert.deepEqual(step.to, to, 'B19 architectural destination component');
    assert.ok(step.contribution.author.subject, 'B19 relationship author');
    assert.ok(Number.isFinite(Date.parse(step.contribution.createdAt)), 'B19 creation time');
    const data = step.contribution.relationship;
    for (const [key, provenance] of [[from, data.fromProvenance], [to, data.toProvenance]]) {
      assert.deepEqual(provenance.key, key, 'B19 endpoint provenance key');
      assert.equal(provenance.snapshotId, snapshot, 'B19 endpoint snapshot');
      assert.match(provenance.sourceCommit, /^[a-f0-9]{40}$/, 'B19 endpoint source commit');
    }
    assert.deepEqual(data.meaning.publishedType, publishedType, 'B20 published meaning');
    console.log(JSON.stringify({ behaviours: ['B19', 'B20'], snapshot, from, to, publishedType }));
  } finally {
    await manager.request(`/api/contributions/${created.id}/remove`, { expectedVersion: created.version });
  }
}

try {
  let state;
  let manager;
  if (persistence) {
    state = JSON.parse(await readFile(stateFile, 'utf8'));
  } else {
    manager = await login('manager');
    assert.deepEqual(await manager.request('/api/session'), { canRead: true, canManage: true });
    await manager.page.getByLabel('Annotation category').selectOption('businessRbac');
    await manager.page.getByLabel('Annotation text').fill(note);
    await manager.page.getByRole('button', { name: 'Add annotation' }).click();
    await manager.page.getByText(note, { exact: true }).waitFor();
    const entity = await manager.request('/api/entities/detail', { key: from });
    const annotation = entity.annotations.find(record => record.annotation?.text === note);
    assert.ok(annotation);

    const relationship = await manager.request('/api/contributions/relationships', {
      from, to, meaning: { customLabel: 'Routes to warehouse', explanation: 'Synthetic storefront order handoff' }
    }, 201);
    state = { snapshot, annotationId: annotation.id, relationshipId: relationship.id };
    await writeFile(stateFile, JSON.stringify(state));
    await waitForIndexed(manager, annotation.id);
  }

  const reader = await login('reader');
  assert.deepEqual(await reader.request('/api/session'), { canRead: true, canManage: false });
  assert.equal(await reader.page.getByRole('button', { name: 'Add annotation' }).count(), 0);
  await reader.request('/api/contributions/annotations', {
    target: from, category: 'businessExplanation', text: 'Reader attempt'
  }, 403);
  await reader.page.getByRole('button', { name: 'Traverse' }).click();
  await reader.page.getByRole('link', { name: 'Routes to warehouse' }).waitFor();
  const paths = await reader.request('/api/traverse', { start: from });
  assert.equal(paths.snapshotId, state.snapshot);
  assert.ok(paths.paths.some(path => path.steps.some(step => step.runtimeId === state.relationshipId)));
  assert.ok(paths.entities.some(entity => entity.annotations.some(annotation => annotation.id === state.annotationId)));
  const history = await reader.request(`/api/history?contributionId=${state.relationshipId}`);
  assert.equal(history.entries.length, 1);
  assert.equal(history.entries[0].type, 'relationshipCreated');
  if (persistence) await waitForIndexed(reader, state.annotationId);
  else {
    await checkOrderFulfilment(reader);
    await checkArchitecturalRelationship(manager, reader);
    await manager.close();
  }
  console.log(`${target} ${persistence ? 'persistence' : 'browser and API'} smoke passed for application ${appVersion}, snapshot ${snapshot}.`);
} finally {
  await browser.close();
}
