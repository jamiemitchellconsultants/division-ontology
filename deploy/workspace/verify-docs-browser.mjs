import assert from 'node:assert/strict';
import { randomUUID } from 'node:crypto';
import { chromium } from 'playwright';

const origin = `http://workspace.localhost:${process.env.WORKSPACE_PORT ?? '8087'}`;
const browser = await chromium.launch({
  executablePath: process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE,
  args: ['--host-resolver-rules=MAP workspace.localhost 127.0.0.1']
});
const inventory = '/api/inventory';
const annotations = '/api/contributions/annotations';
const history = '/api/history';
let clientSecretPrompt = false;

async function docsPage() {
  const context = await browser.newContext();
  const page = await context.newPage();
  page.setDefaultTimeout(15_000);
  await page.goto(origin + '/api/docs');
  await page.locator('.opblock-summary-path[data-path="/api/inventory"]').waitFor();
  return page;
}

async function signIn(page, role) {
  const password = process.env[`WORKSPACE_DEV_${role.toUpperCase()}_PASSWORD`];
  assert.ok(password, `Provide the disposable ${role} password in the environment.`);
  await page.getByRole('button', { name: 'Authorize', exact: true }).first().click();
  const oauth = page.locator('.auth-container').filter({ hasText: 'workspaceSignIn' });
  await oauth.waitFor();
  if (await oauth.locator('#client_secret_authorizationCode').isVisible()) {
    clientSecretPrompt = true;
    console.log(`B9 FAIL (${role}): Swagger shows a client_secret field for PKCE. Continuing with it blank.`);
  }
  const [popup] = await Promise.all([
    page.waitForEvent('popup'),
    oauth.getByRole('button', { name: 'Apply given OAuth2 credentials' }).click()
  ]);
  await popup.locator('#username').fill(`workspace-${role}`);
  const authorize = new URL(popup.url());
  assert.equal(authorize.origin, origin);
  assert.equal(authorize.pathname, '/realms/workspace/protocol/openid-connect/auth');
  assert.equal(authorize.searchParams.get('response_type'), 'code');
  assert.equal(authorize.searchParams.get('code_challenge_method'), 'S256');
  assert.ok(authorize.searchParams.get('code_challenge'));
  assert.equal(authorize.searchParams.get('redirect_uri'), origin + '/api/docs/oauth2-redirect.html');
  await popup.locator('#password').fill(password);
  await popup.locator('#kc-login').click();
  await oauth.locator('button').filter({ hasText: /^Logout$/ }).waitFor();
  await oauth.getByRole('button', { name: 'Close', exact: true }).click();
  console.log(`B9 (${role}): Keycloak PKCE round trip returned to Swagger authorized.`);
}

async function execute(page, method, path, { parameters = {}, body } = {}) {
  const operation = page.locator('.opblock').filter({
    has: page.locator(`.opblock-summary-path[data-path="${path}"]`)
  }).filter({ has: page.locator('.opblock-summary-method').getByText(method, { exact: true }) });
  const run = operation.getByRole('button', { name: 'Execute', exact: true });
  if (!(await operation.locator('.opblock-body').isVisible())) {
    await operation.locator('.opblock-summary').click();
  }
  if (!(await run.isVisible())) await operation.getByRole('button', { name: 'Try it out' }).click();
  for (const [name, value] of Object.entries(parameters)) {
    await operation.locator(`tr[data-param-name="${name}"] input`).fill(value);
  }
  if (body !== undefined) await operation.locator('textarea.body-param__text').fill(body);
  const pending = page.waitForResponse(response =>
    new URL(response.url()).pathname === path && response.request().method() === method);
  await run.click();
  const response = await pending;
  assert.equal(new URL(response.url()).origin, origin);
  return response;
}

async function problem(response, status, code) {
  assert.equal(response.status(), status);
  assert.match(response.headers()['content-type'], /^application\/problem\+json/);
  const body = await response.json();
  assert.equal(body.status, status);
  assert.equal(body.code, code);
  assert.ok(body.title);
  if (status === 401) assert.equal(response.headers()['www-authenticate'], 'Bearer');
}

try {
  const reader = await docsPage();
  await problem(await execute(reader, 'GET', inventory), 401, 'authenticationRequired');
  console.log('B18/B19 PASS: opened docs anonymously and the read was refused.');

  await signIn(reader, 'reader');
  const readerSession = await execute(reader, 'GET', '/api/session');
  assert.equal(readerSession.status(), 200);
  assert.deepEqual(await readerSession.json(), { canRead: true, canManage: false });
  const read = await execute(reader, 'GET', inventory, { parameters: { limit: '1' } });
  assert.equal(read.status(), 200);
  const data = await read.json();
  const authorization = await read.request().headerValue('authorization');
  assert.ok(authorization?.startsWith('Bearer '), 'Swagger sends a bearer token.');
  const direct = await reader.request.get(read.url(), { headers: { authorization } });
  assert.equal(direct.status(), 200);
  assert.ok(JSON.stringify(await direct.json()) === JSON.stringify(data), 'Swagger and direct read match.');
  const target = data.entities[0]?.key;
  assert.ok(target, 'Publish a disposable fixture snapshot first.');
  console.log('B10 PASS: reader inventory in Swagger matched the direct API response.');

  const manager = await docsPage();
  await signIn(manager, 'manager');
  const session = await execute(manager, 'GET', '/api/session');
  assert.equal(session.status(), 200);
  assert.deepEqual(await session.json(), { canRead: true, canManage: true });
  const managerAuthorization = await session.request().headerValue('authorization');
  const encodedClaims = managerAuthorization?.split('.')[1];
  assert.ok(encodedClaims);
  const { iss: issuer, sub: subject, aud: audience } = JSON.parse(Buffer.from(encodedClaims, 'base64url'));
  assert.ok(issuer && subject);
  assert.ok([audience].flat().includes('workspace-api'), 'Swagger uses the workspace API audience.');
  console.log('B21 PASS (browser): the token has the workspace audience and the sessions report the expected roles.');
  const text = `Swagger acceptance fixture ${randomUUID()}`;
  const annotation = { target, category: 'businessExplanation', text };
  await problem(await execute(manager, 'POST', annotations, { body: '{' }), 400, 'unreadableRequest');
  const created = await execute(manager, 'POST', annotations, { body: JSON.stringify(annotation) });
  assert.equal(created.status(), 201);
  const { id, position } = await created.json();
  assert.equal(created.headers().location, `/api/history?contributionId=${id}`);
  const saved = await execute(manager, 'GET', history, { parameters: { contributionId: id } });
  assert.equal(saved.status(), 200);
  const change = (await saved.json()).entries.find(entry => entry.contributionId === id);
  assert.ok(change);
  assert.equal(change.position, position);
  assert.ok(change.after.annotation.text === text, 'History contains the saved annotation.');
  assert.ok(change.actor.subject === subject && change.actor.issuer === issuer,
    'History attributes the change to the manager.');
  console.log('B11 PASS: manager annotation returned 201 and appeared in attributed history.');

  const readerText = `Rejected Swagger fixture ${randomUUID()}`;
  await problem(await execute(reader, 'POST', annotations, {
    body: JSON.stringify({ ...annotation, text: readerText })
  }), 403, 'roleRequired');
  const recent = await manager.request.get(origin + history, {
    headers: { authorization: managerAuthorization }
  });
  assert.equal(recent.status(), 200);
  assert.ok(!(await recent.json()).entries.some(entry => entry.after.annotation?.text === readerText));
  console.log('B12 PASS (reader): 403 roleRequired and no matching saved change.');
  console.log('B22 PASS (browser): 400, 401 and 403 returned problem details with the expected codes.');

  await reader.getByRole('button', { name: 'Authorize', exact: true }).first().click();
  const oauth = reader.locator('.auth-container').filter({ hasText: 'workspaceSignIn' });
  await oauth.locator('button').filter({ hasText: /^Logout$/ }).click();
  await oauth.getByRole('button', { name: 'Close', exact: true }).click();
  const afterLogout = await execute(reader, 'GET', inventory);
  await problem(afterLogout, 401, 'authenticationRequired');
  assert.ok(!(await afterLogout.request().headerValue('authorization')), 'Logout drops the bearer header.');
  console.log('B13 PASS (logout): Swagger stopped sending the token and the read returned 401.');

  await reader.context().clearCookies();
  await signIn(reader, 'reader');
  assert.equal((await execute(reader, 'GET', inventory)).status(), 200);
  console.log('B13 PASS (reauthorization): sign-in restored access in the same Swagger page.');

  if (clientSecretPrompt) process.exitCode = 1;
} finally {
  await browser.close();
}
