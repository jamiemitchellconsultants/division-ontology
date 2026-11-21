import assert from 'node:assert/strict';
import { randomUUID } from 'node:crypto';
import { chromium } from 'playwright';

// Deployed acceptance for traversal cursor pagination (#119). Seeds runtime relationships through the
// manager API so the synthetic fixture has a node with more than one page, pages as the reader, and
// removes every seeded relationship before it exits.
const target = process.argv[2];
assert.ok(target === 'mac' || target === 'home-lab', 'Usage: node verify-traversal-paging.mjs mac|home-lab [--seed-only|--cleanup]');
const homeLab = target === 'home-lab';
const origin = homeLab ? process.env.WORKSPACE_VERIFY_ORIGIN
  : `http://workspace.localhost:${process.env.WORKSPACE_PORT ?? '8087'}`;
assert.ok(origin && (!homeLab || new URL(origin).protocol === 'https:'), 'Set the HTTPS home-lab fixture origin.');
const seedCount = 105;
const run = randomUUID().slice(0, 8);
const from = { sourceId: 'fixture/ecommerce', kind: 'concept', identifier: 'https://example.test/ecommerce/ontology#Order' };
const to = { sourceId: 'fixture/warehouse', kind: 'concept', identifier: 'https://example.test/warehouse/ontology#Order' };
const sameKey = (a, b) => a.sourceId === b.sourceId && a.kind === b.kind && a.identifier === b.identifier;
const signature = path => path.steps.map(step => step.key?.identifier ?? step.runtimeId).join('>');
const results = [];

function record(behaviour, detail) {
  results.push({ behaviour, status: 'pass', detail });
  console.log(JSON.stringify({ behaviour, status: 'pass', detail }));
}

const loopbackHosts = homeLab ? (process.env.WORKSPACE_VERIFY_LOOPBACK_HOSTS || '').split(',').filter(Boolean) : [];
const browserArgs = homeLab
  ? (loopbackHosts.length ? [`--host-resolver-rules=${loopbackHosts.map(host => `MAP ${host} 127.0.0.1`).join(',')}`, '--no-proxy-server'] : [])
  : ['--host-resolver-rules=MAP workspace.localhost 127.0.0.1'];
const browser = await chromium.launch({ executablePath: process.env.PLAYWRIGHT_CHROMIUM_EXECUTABLE, args: browserArgs });

async function login(role) {
  const context = await browser.newContext();
  const page = await context.newPage();
  const account = role.toUpperCase();
  const username = homeLab ? process.env[`WORKSPACE_VERIFY_${account}_USERNAME`] : `workspace-${role}`;
  const password = process.env[`WORKSPACE_${homeLab ? 'VERIFY' : 'DEV'}_${account}_PASSWORD`];
  assert.ok(username && password, `Provide the controlled ${role} fixture credentials in the environment.`);
  await page.goto(origin);
  await page.getByRole('button', { name: 'Sign in to the workspace' }).click();
  await page.locator('#username').fill(username);
  await page.locator('#password').fill(password);
  await page.locator('#kc-login').click();
  await page.getByRole('heading', { name: 'Published inventory' }).waitFor({ timeout: 15_000 });
  const session = page.waitForRequest(request => new URL(request.url()).pathname === '/api/session');
  await page.goto(`${origin}/entity/${Buffer.from(JSON.stringify(from)).toString('base64url')}`);
  const authorization = (await (await session).allHeaders()).authorization;
  assert.match(authorization ?? '', /^Bearer [^ ]+$/, `${role} session token`);
  return {
    close: () => context.close(),
    async call(path, body, method = body === undefined ? 'GET' : 'POST') {
      return page.evaluate(async ({ path, body, method, authorization }) => {
        const response = await fetch(path, {
          method, body: body === undefined ? undefined : JSON.stringify(body),
          headers: { authorization, 'content-type': 'application/json' }
        });
        const text = await response.text();
        return { status: response.status, link: response.headers.get('link') ?? '', data: text ? JSON.parse(text) : null };
      }, { path, body, method, authorization });
    }
  };
}

async function expectStatus(client, path, body, status) {
  const response = await client.call(path, body);
  assert.equal(response.status, status, `${path} returned ${response.status}: ${JSON.stringify(response.data)?.slice(0, 300)}`);
  return response;
}

async function journalHead(client) {
  let cursor = null;
  let head = 0;
  do {
    const query = new URLSearchParams({ limit: '100' });
    if (cursor) query.set('cursor', cursor);
    const page = (await expectStatus(client, `/api/history?${query}`, undefined, 200)).data;
    for (const entry of page.entries) head = Math.max(head, entry.position);
    cursor = page.nextCursor;
  } while (cursor);
  return head;
}

async function follow(client, request) {
  const pages = [];
  let target = '/api/traverse';
  let cursor;
  while (target) {
    const started = performance.now();
    const response = await expectStatus(client, target, { ...request, cursor }, 200);
    const page = { ...response.data, link: response.link, milliseconds: Math.round(performance.now() - started) };
    pages.push(page);
    assert.equal(page.truncated, page.nextCursor !== undefined, 'truncated matches nextCursor');
    const next = response.link.split(',').map(part => part.trim()).find(part => part.endsWith('rel="next"'));
    assert.equal(Boolean(next), page.nextCursor !== undefined, 'next link present exactly when nextCursor is');
    target = next ? next.slice(next.indexOf('<') + 1, next.indexOf('>')) : null;
    cursor = page.nextCursor;
    assert.ok(pages.length <= 50, 'paging did not finish');
  }
  return pages;
}

function assertRestart(response, status, code) {
  assert.equal(response.status, status, `${code} status`);
  assert.equal(response.data.code, code);
  assert.match(response.data.title, /Restart the traversal from the first page/);
  assert.equal(response.data.paths, undefined);
}

if (process.argv[3] === '--cleanup') {
  const cleaner = await login('manager');
  const latest = new Map();
  let cursor = null;
  do {
    const query = new URLSearchParams({ limit: '100' });
    if (cursor) query.set('cursor', cursor);
    const page = (await expectStatus(cleaner, `/api/history?${query}`, undefined, 200)).data;
    for (const entry of page.entries) latest.set(entry.contributionId, entry.after);
    cursor = page.nextCursor;
  } while (cursor);
  const leftovers = [...latest.values()].filter(item => !item.removed &&
    item.relationship?.meaning?.customLabel?.startsWith('paging-acceptance-'));
  for (const item of leftovers)
    await expectStatus(cleaner, `/api/contributions/${item.id}/remove`, { expectedVersion: item.version }, 200);
  await cleaner.close();
  await browser.close();
  console.log(`Removed ${leftovers.length} leftover paging-acceptance relationships.`);
  process.exit(0);
}

const seeded = [];
let manager;
let reader;
try {
  manager = await login('manager');
  reader = await login('reader');

  for (let i = 0; i < seedCount; i++) {
    const created = await expectStatus(manager, '/api/contributions/relationships', {
      from, to, meaning: { customLabel: `paging-acceptance-${run}-${i}`, explanation: 'Seeded by traversal paging acceptance; removed after the run.' }
    }, 201);
    seeded.push(created.data);
  }
  const ids = new Set(seeded.map(item => item.id));
  if (process.argv[3] === '--seed-only') {
    console.log(`Seeded ${seeded.length} relationships from ${from.identifier} to ${to.identifier} (run ${run}).`);
    console.log(`Remove them afterwards with: node deploy/workspace/verify-traversal-paging.mjs ${target} --cleanup`);
    seeded.length = 0;
  } else {

    const spec = (await expectStatus(reader, '/api/openapi/v1.json', undefined, 200)).data;
    const op = spec.paths['/api/traverse'].post;
    assert.ok(spec.components.schemas.TraversalRequest.properties.cursor, 'cursor field');
    assert.ok(spec.components.schemas.TraversalResult.properties.nextCursor, 'nextCursor field');
    assert.ok(op.responses['422']['x-workspace-codes'].includes('invalidCursor'));
    assert.ok(op.responses['422']['x-workspace-codes'].includes('invalidTraversal'));
    assert.ok(op.responses['409']['x-workspace-codes'].includes('staleCursor'));
    assert.ok(op.requestBody.content['application/json'].examples.firstPage.value.cursor == null, 'firstPage has no cursor');
    assert.ok(op.requestBody.content['application/json'].examples.nextPage.value.cursor);
    for (const link of ['next', 'entityDetail', 'relationshipDetail']) assert.ok(op.responses['200'].links[link], `${link} link`);
    assert.match(spec['x-agent-discovery'].linkRelations.next, /traverseGraph/);
    record('B14', 'served spec documents cursor, nextCursor, invalidCursor, staleCursor, examples and links');

    const headBefore = await journalHead(reader);
    const outgoing = { start: from, direction: 'outgoing', maxDepth: 1, includeUnavailable: false };
    const pages = await follow(reader, outgoing);
    const signatures = pages.flatMap(page => page.paths.map(signature));
    assert.ok(pages.length >= 2, 'more than one page');
    assert.equal(pages[0].paths.length, 100);
    assert.equal(new Set(signatures).size, signatures.length, 'no path repeated');
    assert.deepEqual([...ids].filter(id => !signatures.includes(id)), [], 'every seeded path reached');
    assert.equal(pages.at(-1).nextCursor, undefined);
    assert.equal(pages.at(-1).truncated, false);
    record('B1', `${signatures.length} paths over ${pages.length} pages; all ${seedCount} seeded runtime paths once`);
    record('B5', `first page ${pages[0].paths.length} paths, truncated true, nextCursor issued`);
    record('B6', 'truncated equals nextCursor presence on every page; final page truncated false');
    record('B13', 'next Link header on every non-final page only');
    record('B15', `followed ${pages.length - 1} next links from the response headers`);

    const firstRuntime = signatures.findIndex(item => ids.has(item));
    assert.ok(signatures.slice(firstRuntime).every(item => ids.has(item) || !item.startsWith('https://')),
      'published steps precede workspace steps');
    record('B4', `published paths precede workspace paths (first workspace path at position ${firstRuntime + 1})`);

    for (const page of pages) {
      const endpoints = page.paths.flatMap(path => path.steps.flatMap(step => [step.from, step.to]));
      for (const key of endpoints) {
        const entity = page.entities.find(candidate => sameKey(candidate.key, key));
        assert.ok(entity && entity.label && entity.provenance && 'available' in entity && Array.isArray(entity.annotations),
          `page lacks context for ${key.identifier}`);
      }
    }
    record('B7', 'every page carries label, provenance, availability and annotations for its endpoints');

    const incoming = await follow(reader, { ...outgoing, start: to, direction: 'incoming' });
    const incomingIds = incoming.flatMap(page => page.paths.map(signature)).filter(id => ids.has(id));
    assert.equal(new Set(incomingIds).size, seedCount);
    assert.equal(incomingIds.length, seedCount);
    const both = (await follow(reader, { ...outgoing, direction: 'both' })).flatMap(page => page.paths.map(signature));
    assert.equal(new Set(both).size, both.length);
    const unavailable = (await follow(reader, { ...outgoing, includeUnavailable: true })).flatMap(page => page.paths.map(signature));
    assert.deepEqual(unavailable, signatures, 'includeUnavailable over an all-available fixture matches');
    record('B3', `incoming ${incoming.length} pages, both ${both.length} paths, includeUnavailable parity`);

    const deep = await follow(reader, { ...outgoing, maxDepth: 2 });
    const deepPaths = deep.flatMap(page => page.paths);
    const deepSignatures = deepPaths.map(signature);
    assert.equal(new Set(deepSignatures).size, deepSignatures.length);
    assert.ok(deepPaths.every((path, index) => index === 0 || deepPaths[index - 1].steps.length <= path.steps.length),
      'breadth-first order across pages');
    record('B2', `depth-2 walk ${deepSignatures.length} paths over ${deep.length} pages, breadth-first, none repeated`);

    const cursor = pages[0].nextCursor;
    const once = await expectStatus(reader, '/api/traverse', { ...outgoing, cursor }, 200);
    const twice = await expectStatus(reader, '/api/traverse', { ...outgoing, cursor }, 200);
    assert.deepEqual(once.data.paths.map(signature), twice.data.paths.map(signature));
    assert.deepEqual(once.data.paths.map(signature), pages[1].paths.map(signature));
    record('B12', 'resubmitting a cursor returns the same next page');

    for (const changed of [{ direction: 'both' }, { maxDepth: 2 }, { includeUnavailable: true }, { start: to }]) {
      assertRestart(await reader.call('/api/traverse', { ...outgoing, ...changed, cursor }), 422, 'invalidCursor');
    }
    record('B8', 'changed start, direction, maxDepth and includeUnavailable each fail 422 invalidCursor');
    for (const malformed of ['not a cursor', cursor.slice(0, cursor.length / 2)]) {
      assertRestart(await reader.call('/api/traverse', { ...outgoing, cursor: malformed }), 422, 'invalidCursor');
    }
    record('B9', 'malformed cursors fail 422 invalidCursor without results');

    assert.equal(await journalHead(reader), headBefore, 'reader paging appended no journal events');
    record('B18', `journal head unchanged at ${headBefore} across reader paging and rejected cursors`);

    record('B16', `page latencies ${pages.map(page => page.milliseconds).join('/')} ms (proxy; examined-row counts are test-only)`);

    const note = await expectStatus(manager, '/api/contributions/annotations',
      { target: to, category: 'businessExplanation', text: `Paging staleness probe ${run}.` }, 201);
    assertRestart(await reader.call('/api/traverse', { ...outgoing, cursor }), 409, 'staleCursor');
    await expectStatus(manager, `/api/contributions/${note.data.id}/remove`, { expectedVersion: note.data.version }, 200);
    record('B11', 'an unrelated annotation made an outstanding cursor fail 409 staleCursor');
  }
} finally {
  const failures = [];
  for (const item of seeded) {
    const removed = await manager.call(`/api/contributions/${item.id}/remove`, { expectedVersion: item.version });
    if (removed.status !== 200) failures.push(`${item.id}: ${removed.status}`);
  }
  await reader?.close();
  await manager?.close();
  await browser.close();
  if (failures.length) console.error(`Could not remove seeded relationships: ${failures.join(', ')}`);
  else if (seeded.length) console.log(`Removed ${seeded.length} seeded relationships for run ${run}.`);
}
if (results.length) console.log(`${target} traversal paging acceptance passed: ${results.map(result => result.behaviour).join(', ')}.`);
