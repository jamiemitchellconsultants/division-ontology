import assert from 'node:assert/strict';
import { readFile, writeFile } from 'node:fs/promises';
import { join } from 'node:path';
import { setTimeout as delay } from 'node:timers/promises';
import { chromium } from 'playwright';

const origin = `http://workspace.localhost:${process.env.WORKSPACE_PORT ?? '8087'}`;
const scenario = process.env.WORKSPACE_SCENARIO_DIR;
const persistence = process.argv[2] === '--check-persistence';
assert.ok(scenario, 'Set WORKSPACE_SCENARIO_DIR to a dedicated fixture directory.');
const snapshot = (await readFile(join(scenario, 'snapshot'), 'utf8')).trim();
const stateFile = join(scenario, 'scenario.json');
const from = { sourceId: 'fixture/ecommerce', kind: 'concept', identifier: 'https://example.test/ecommerce/ontology#Order' };
const to = { sourceId: 'fixture/warehouse', kind: 'concept', identifier: 'https://example.test/warehouse/ontology#Order' };
const entityPath = key => `/entity/${Buffer.from(JSON.stringify(key)).toString('base64url')}`;
const note = 'Finance approvers reconcile synthetic storefront orders before dispatch.';
const browser = await chromium.launch({
  executablePath: process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE,
  args: ['--host-resolver-rules=MAP workspace.localhost 127.0.0.1']
});

async function login(role) {
  const context = await browser.newContext();
  const page = await context.newPage();
  const failed = [];
  page.on('response', response => {
    if (response.status() >= 400) failed.push(`${response.status()} ${new URL(response.url()).pathname}`);
  });
  page.on('requestfailed', request => failed.push(`${request.failure().errorText} ${new URL(request.url()).pathname}`));
  const password = process.env[`WORKSPACE_DEV_${role.toUpperCase()}_PASSWORD`];
  assert.ok(password, `Set the ${role} fixture password in the environment.`);
  await page.goto(origin);
  await page.getByRole('button', { name: 'Sign in to the workspace' }).click();
  try {
    await page.locator('#username').fill(`workspace-${role}`);
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
  const started = Date.now();
  for (let attempt = 0; attempt < 90; attempt++) {
    const result = await client.request('/api/search', { query: note });
    if (result.hits.some(hit => hit.annotationId === id)) {
      console.log(`Qwen fixture annotation retrieved in ${Date.now() - started} ms after save.`);
      return;
    }
    await delay(1000);
  }
  throw new Error('The Qwen index did not retrieve the current fixture annotation.');
}

try {
  let state;
  if (persistence) {
    state = JSON.parse(await readFile(stateFile, 'utf8'));
  } else {
    const manager = await login('manager');
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
    await manager.close();
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
  console.log(`Mac ${persistence ? 'restart persistence' : 'browser and API'} smoke passed for snapshot ${snapshot}.`);
} finally {
  await browser.close();
}
