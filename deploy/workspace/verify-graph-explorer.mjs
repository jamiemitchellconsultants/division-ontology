import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { createHash, randomUUID } from 'node:crypto';
import { readFile } from 'node:fs/promises';
import { join } from 'node:path';

const ecommerce = 'fixture/ecommerce';
const warehouse = 'fixture/warehouse';
const concept = source => ({ sourceId: `fixture/${source}`, kind: 'concept', identifier: `https://example.test/${source}/ontology#Order` });
const node = (source, id) => ({ sourceId: `fixture/${source}`, kind: 'node', identifier: `https://example.test/${source}/graph#${id}` });
const order = concept('ecommerce');
const warehouseOrder = concept('warehouse');
const processNode = node('ecommerce', 'component');
const storefront = node('ecommerce', 'container');
const system = node('ecommerce', 'system');
const external = node('ecommerce', 'external');
const warehouseSystem = node('warehouse', 'system');
const encode = ({ sourceId, kind, identifier }) => Buffer.from(JSON.stringify({ sourceId, kind, identifier })).toString('base64url');
const graphPath = key => `/graph/${encode(key)}`;
const nodeId = key => `n:${encode(key)}`;
const sameKey = (first, second) => encode(first) === encode(second);
const graphHost = '[data-graph-diagram]';
const row = (page, id) => page.locator(`[data-graph-edge="${id}"]`);
const details = page => page.getByRole('region', { name: 'Graph details', exact: true });
const button = (page, name) => page.getByRole('button', { name, exact: true }).first();
const heading = (page, label, source) => page.getByRole('heading', { level: 1, name: `${label} · ${source}`, exact: true });
const result = (scenario, status = 'pass', reason) =>
  console.log(JSON.stringify({ feature: 145, scenario, status, ...(reason ? { reason } : {}) }));

const traverse = (client, start, direction = 'both') => client.request('/api/traverse', {
  start, direction, maxDepth: 1, includeUnavailable: false
});

async function findConnection(client, start, target, kind, direction = 'both') {
  const view = await traverse(client, start, direction);
  const step = view.paths.map(path => path.steps[0]).find(candidate =>
    candidate.key?.kind === kind && (sameKey(candidate.from, target) || sameKey(candidate.to, target)));
  assert.ok(step, `Missing ${kind} between ${start.identifier} and ${target.identifier}`);
  return step;
}

async function enter(page, origin, key, label) {
  await page.goto(origin + graphPath(key));
  await heading(page, label, key.sourceId).waitFor();
}

async function edgeId(page, step) {
  if (step.runtimeId) return `r:${step.runtimeId}`;
  const match = await page.waitForFunction(expected => {
    for (const row of document.querySelectorAll('[data-graph-edge^="p:"]')) {
      const id = row.dataset.graphEdge;
      const encoded = id.slice(2).replace(/-/g, '+').replace(/_/g, '/');
      const key = JSON.parse(new TextDecoder().decode(Uint8Array.from(atob(encoded), byte => byte.charCodeAt(0))));
      if (key.sourceId === expected.sourceId && key.kind === expected.kind && key.identifier === expected.identifier) return id;
    }
    return null;
  }, step.key);
  try {
    return await match.jsonValue();
  } finally {
    await match.dispose();
  }
}

async function follow(page, step, key, label) {
  await row(page, await edgeId(page, step)).locator('[data-graph-follow]').click();
  await heading(page, label, key.sourceId).waitFor();
}

async function waitForDiagram(page, id = null) {
  await page.waitForFunction(async ({ selector, id }) => {
    const host = document.querySelector(selector);
    if (!host) return false;
    const module = await import(new URL('graph-diagram.mjs', document.baseURI).href);
    const diagram = module.diagramFor(host);
    return id === null ? Boolean(diagram) : Boolean(diagram?.renderedPosition(id));
  }, { selector: graphHost, id });
}

async function canvasPoint(page, id) {
  await waitForDiagram(page, id);
  const host = page.locator(graphHost);
  await host.scrollIntoViewIfNeeded();
  const box = await host.boundingBox();
  const point = await host.evaluate(async (element, id) => {
    const module = await import(new URL('graph-diagram.mjs', document.baseURI).href);
    const position = module.diagramFor(element)?.renderedPosition(id);
    return position && { x: position.x + element.clientLeft, y: position.y + element.clientTop };
  }, id);
  assert.ok(box && point, `Diagram position for ${id}`);
  return { x: box.x + point.x, y: box.y + point.y };
}

async function viewport(page) {
  await waitForDiagram(page);
  return page.locator(graphHost).evaluate(async host => {
    const module = await import(new URL('graph-diagram.mjs', document.baseURI).href);
    const { pan, zoom } = module.diagramFor(host).captureViewport();
    return { pan, zoom };
  });
}

async function waitForViewport(page, expected) {
  await page.waitForFunction(async ({ selector, expected }) => {
    const host = document.querySelector(selector);
    if (!host) return false;
    const module = await import(new URL('graph-diagram.mjs', document.baseURI).href);
    const current = module.diagramFor(host)?.captureViewport();
    return current && Math.abs(current.zoom - expected.zoom) < 1e-6 &&
      Math.abs(current.pan.x - expected.pan.x) < 1e-6 && Math.abs(current.pan.y - expected.pan.y) < 1e-6;
  }, { selector: graphHost, expected });
}

async function keyboardPress(page, control) {
  await control.focus();
  await page.keyboard.press('Enter');
}

export async function verifyGraphDelivery({ page, origin }) {
  const response = await page.goto(origin + graphPath(order));
  assert.equal(response?.status(), 200, 'The direct graph URL must serve the workspace entry point');
  assert.match(await response.text(), /id="app"/, 'The direct graph URL must serve the Blazor host');
  const paths = ['/graph-diagram.mjs', '/vendor/cytoscape/3.35.0/cytoscape.esm.min.mjs'];
  const assets = await page.evaluate(async paths => Promise.all(paths.map(async path => {
    const response = await fetch(path);
    return { url: response.url, status: response.status, type: response.headers.get('content-type') };
  })), paths);
  for (const [index, asset] of assets.entries()) {
    assert.equal(new URL(asset.url).origin, new URL(origin).origin);
    assert.equal(asset.status, 200, paths[index]);
    assert.match(asset.type ?? '', /^(?:text|application)\/javascript\b/i, `${paths[index]} must be JavaScript`);
  }
  await heading(page, 'Order', ecommerce).waitFor();
}

async function checkDiagramFallback(page, origin) {
  const module = '**/graph-diagram.mjs';
  const block = route => route.abort();
  await page.route(module, block);
  try {
    await enter(page, origin, processNode, 'Order processing');
    await page.getByText('The diagram could not start; use the neighbour list.').waitFor();
    await page.locator(`[data-graph-inspect-node="${nodeId(storefront)}"]`).first().click();
    await details(page).getByRole('heading', { name: `Storefront API · ${ecommerce}`, exact: true }).waitFor();
  } finally {
    await page.unroute(module, block);
  }
}

async function search(page, query) {
  await page.getByRole('searchbox', { name: 'Semantic search' }).fill(query);
  await button(page, 'Search').click();
  const results = page.getByRole('region', { name: 'Search results' });
  await results.waitFor();
  return results;
}

async function checkEntryAndHandoff(reader, origin) {
  const { page } = reader;
  await page.goto(origin);
  const processResults = await search(page, 'What happens after a customer places an order?');
  await processResults.locator(`a[href="${graphPath(processNode).slice(1)}"]`).first().click();
  await heading(page, 'Order processing', ecommerce).waitFor();
  await page.getByLabel('Direction', { exact: true }).selectOption('incoming');
  const containment = await findConnection(reader, processNode, storefront, 'assertion', 'incoming');
  const containingSystem = await findConnection(reader, storefront, system, 'assertion', 'incoming');
  const containmentId = await edgeId(page, containment);
  const nodeControl = row(page, containmentId).locator('[data-graph-inspect-node]');
  await waitForDiagram(page, nodeId(storefront));
  const zoomBefore = (await viewport(page)).zoom;
  await keyboardPress(page, button(page, 'Zoom in'));
  await page.waitForFunction(async ({ selector, zoomBefore }) => {
    const host = document.querySelector(selector);
    const module = await import(new URL('graph-diagram.mjs', document.baseURI).href);
    return module.diagramFor(host)?.captureViewport().zoom > zoomBefore;
  }, { selector: graphHost, zoomBefore });
  const zoomed = await viewport(page);

  const point = await canvasPoint(page, nodeId(storefront));
  await page.mouse.click(point.x, point.y);
  await details(page).getByRole('heading', { name: `Storefront API · ${ecommerce}`, exact: true }).waitFor();
  await heading(page, 'Order processing', ecommerce).waitFor();
  await button(page, 'Close details').click();
  const doubleTap = await canvasPoint(page, nodeId(storefront));
  await page.mouse.dblclick(doubleTap.x, doubleTap.y);
  await heading(page, 'Storefront API', ecommerce).waitFor();
  await keyboardPress(page, button(page, 'Back'));
  await heading(page, 'Order processing', ecommerce).waitFor();
  assert.equal(await page.getByLabel('Direction', { exact: true }).inputValue(), 'incoming');
  await waitForViewport(page, zoomed);

  await keyboardPress(page, nodeControl);
  await details(page).getByRole('heading', { name: `Storefront API · ${ecommerce}`, exact: true }).waitFor();
  await keyboardPress(page, button(page, 'Close details'));
  await page.waitForFunction(id => document.activeElement?.closest('[data-graph-edge]')?.dataset.graphEdge === id &&
    document.activeElement.hasAttribute('data-graph-inspect-node'), containmentId);
  await keyboardPress(page, row(page, containmentId).locator('[data-graph-follow]'));
  await heading(page, 'Storefront API', ecommerce).waitFor();
  await follow(page, containingSystem, system, 'Ecommerce');
  await page.getByLabel('Direction', { exact: true }).selectOption('outgoing');

  const handoff = await findConnection(reader, system, external, 'assertion', 'outgoing');
  assert.ok(sameKey(handoff.from, system) && sameKey(handoff.to, external), 'Published handoff direction');
  await checkEvidenceAndCoverage(reader, handoff);
  await follow(page, handoff, external, 'Warehouse fulfilment');
  const binding = await findConnection(reader, external, warehouseSystem, 'c4Binding', 'outgoing');
  await follow(page, binding, warehouseSystem, 'Warehouse fulfilment');

  await page.goto(origin);
  const orders = await search(page, 'Order');
  const fromSearch = orders.locator(`a[href="${graphPath(order).slice(1)}"]`).first();
  const fromWarehouse = orders.locator(`a[href="${graphPath(warehouseOrder).slice(1)}"]`).first();
  await fromSearch.waitFor();
  await fromWarehouse.waitFor();
  await fromWarehouse.click();
  await heading(page, 'Order', warehouse).waitFor();
  await enter(page, origin, order, 'Order');
  result('Entry, containment, handoff, keyboard and viewport');
  return handoff;
}

async function checkEvidenceAndCoverage(reader, handoff) {
  const { page } = reader;
  const settings = await page.evaluate(async () => (await fetch('/appsettings.json')).json());
  await row(page, await edgeId(page, handoff)).locator('[data-graph-inspect-edge]').click();
  await details(page).getByText(handoff.provenance.sourceCommit, { exact: true }).waitFor();
  const evidence = handoff.provenance.evidence[0];
  assert.ok(evidence, 'The fixture handoff has source evidence');
  await details(page).getByText(evidence.path, { exact: true }).waitFor();
  const root = settings.EvidenceRepositories?.[handoff.provenance.key.sourceId];
  const configured = /^https:\/\/github\.com\/[A-Za-z0-9_.-]+\/[A-Za-z0-9_.-]+$/.test(root ?? '');
  if (configured) {
    const path = evidence.path.split('/').map(encodeURIComponent).join('/');
    assert.equal(await details(page).getByRole('link', { name: 'View evidence', exact: true }).first().getAttribute('href'),
      `${root}/blob/${handoff.provenance.sourceCommit}/${path}#L${evidence.startLine}-L${evidence.endLine}`);
  } else {
    await details(page).getByText('Evidence link unavailable', { exact: false }).first().waitFor();
    assert.equal(await details(page).getByRole('link', { name: 'View evidence' }).count(), 0);
  }

  const current = await traverse(reader, system, 'outgoing');
  const keys = [system, ...current.entities.map(entity => entity.key)];
  const gaps = [...new Map(current.gaps.map(gap => [JSON.stringify(gap), gap])).values()];
  const relevant = gaps.filter(gap =>
    keys.some(key => key.sourceId === gap.sourceId) &&
    (gap.nodeIri == null || keys.some(key => key.identifier === gap.nodeIri)));
  const section = page.locator('details').filter({ has: page.locator('summary', { hasText: /^Coverage gaps \(/ }) });
  if (relevant.length) {
    await page.getByText('Source coverage is incomplete for this neighbourhood.').waitFor();
    const shown = await section.locator('li').allTextContents();
    const normalise = value => value.replace(/\s+/g, ' ').trim();
    assert.deepEqual(shown.map(normalise), relevant.map(gap =>
      normalise(`${gap.sourceId} ${gap.nodeIri ?? ''} · ${gap.code} · ${gap.reason}`)));
    assert.equal(await page.getByText(/No visible connections|isolated/i).count(), 0);
  } else {
    assert.equal(await section.count(), 0);
  }
  const elsewhere = gaps.length - relevant.length;
  if (elsewhere) await page.getByText(`${elsewhere} coverage gaps elsewhere in the division.`, { exact: false }).waitFor();
  else assert.equal(await page.getByText(/coverage gaps elsewhere/).count(), 0);
  await button(page, 'Close details').click();
  result('Published evidence and scoped source coverage', 'pass', configured ? 'Evidence map configured' : 'Plain citation verified');
}

async function seed(manager, records, from, to, customLabel, explanation) {
  const created = await manager.request('/api/contributions/relationships', { from, to, meaning: { customLabel, explanation } }, 201);
  records.set(created.id, created.version);
  return created;
}

async function cleanup(manager, records) {
  const failures = [];
  for (const [id, version] of [...records].reverse()) {
    try {
      await manager.request(`/api/contributions/${id}/remove`, { expectedVersion: version });
      records.delete(id);
    } catch (error) {
      failures.push(error);
    }
  }
  if (failures.length) throw new AggregateError(failures, 'Some temporary graph records could not be removed');
}

async function checkParallelEdges(manager, reader, origin, published) {
  const records = new Map();
  try {
    const { page } = reader;
    const explanation = 'Synthetic parallel relationship for the graph explorer.';
    const created = await seed(manager, records, published.from, published.to, published.label, explanation);
    const runtimeId = `r:${created.id}`;
    await enter(page, origin, published.from, 'Ecommerce');
    const publishedId = await edgeId(page, published);
    await row(page, publishedId).locator('[data-graph-inspect-edge]').click();
    await details(page).getByText(published.provenance.sourceCommit, { exact: true }).waitFor();
    await row(page, runtimeId).locator('[data-graph-inspect-edge]').click();
    await details(page).getByText(explanation, { exact: false }).waitFor();
    await details(page).locator(`a[href="history/${created.id}"]`).waitFor();

    const publishedPoint = await canvasPoint(page, publishedId);
    const runtimePoint = await canvasPoint(page, runtimeId);
    assert.ok(Math.hypot(publishedPoint.x - runtimePoint.x, publishedPoint.y - runtimePoint.y) > 2, 'Parallel edges have separate hit targets');
    await page.mouse.click(publishedPoint.x, publishedPoint.y);
    await details(page).getByText(published.provenance.sourceCommit, { exact: true }).waitFor();
    await page.mouse.click(runtimePoint.x, runtimePoint.y);
    await details(page).getByText(explanation, { exact: false }).waitFor();
    result('Parallel published and workspace edges');
  } finally {
    await cleanup(manager, records);
  }
}

async function checkPaging(manager, reader, origin, runId) {
  const records = new Map();
  try {
    const { page } = reader;
    for (let index = 0; index < 101; index++) {
      await seed(manager, records, order, warehouseOrder, `Graph paging ${runId} ${index}`, 'Synthetic graph continuation.');
    }
    const initial = await traverse(reader, order, 'outgoing');
    assert.ok(initial.nextCursor);
    const loaded = new Set(initial.paths.map(path => path.steps[0].runtimeId));
    const later = [...records.keys()].find(id => !loaded.has(id));
    assert.ok(later, 'At least one seeded relationship is absent from the first page.');

    await enter(page, origin, order, 'Order');
    await page.getByLabel('Direction', { exact: true }).selectOption('outgoing');
    await page.locator('[data-graph-load-more]:enabled').waitFor();
    const before = await viewport(page);
    assert.equal(await row(page, `r:${later}`).count(), 0);
    await button(page, 'Load more').click();
    await row(page, `r:${later}`).waitFor();
    await heading(page, 'Order', ecommerce).waitFor();
    await waitForViewport(page, before);

    await page.getByLabel('Direction', { exact: true }).selectOption('both');
    await page.locator('[data-graph-load-more]:enabled').waitFor();
    await seed(manager, records, order, warehouseOrder, `Graph invalidation ${runId}`, 'Invalidate the open continuation.');
    await button(page, 'Load more').click();
    await page.getByText('These graph results are out of date.', { exact: false }).waitFor();
    const request = page.waitForRequest(candidate => new URL(candidate.url()).pathname === '/api/traverse');
    await button(page, 'Reload').click();
    const body = (await request).postDataJSON();
    assert.deepEqual(body.start, order);
    assert.equal(body.direction, 'both');
    assert.equal(body.cursor, null);
    await page.locator('[data-graph-load-more]:enabled').waitFor();
    assert.equal(await page.getByText('These graph results are out of date.', { exact: false }).count(), 0);
    result('101 relationships, continuation and live invalidation');
  } finally {
    await cleanup(manager, records);
  }
}

async function saveInGraph(page, submit, path, method, records) {
  const pending = page.waitForResponse(response => new URL(response.url()).pathname === path && response.request().method() === method);
  await button(page, submit).click();
  const response = await pending;
  assert.ok(response.ok(), `Graph save ${method} ${path} (HTTP ${response.status()})`);
  const saved = await response.json();
  records.set(saved.id, saved.version);
  await page.locator(`[data-graph-save-status] a[href="history/${saved.id}"]`).waitFor();
  return saved;
}

async function checkRuntimeWrites(manager, reader, origin, runId) {
  const records = new Map();
  try {
    const { page } = manager;
    const note = `Graph explorer note ${runId}`;
    const label = `Graph explorer relationship ${runId}`;
    const explanation = 'Dispatch follows the confirmed handoff.';
    const edited = 'Dispatch begins after stock reservation.';
    await enter(page, origin, order, 'Order');
    await button(page, 'Inspect focus').click();
    await button(page, 'Add note').click();
    await page.getByLabel('Annotation text').fill(note);
    const savedNote = await saveInGraph(page, 'Add annotation', '/api/contributions/annotations', 'POST', records);

    await enter(reader.page, origin, order, 'Order');
    await button(reader.page, 'Inspect focus').click();
    await details(reader.page).getByText(note, { exact: true }).waitFor();

    await button(page, 'Inspect focus').click();
    await button(page, 'Create relationship').click();
    await page.getByLabel('To entity').selectOption(JSON.stringify([warehouseOrder.sourceId, warehouseOrder.kind, warehouseOrder.identifier]));
    await page.getByLabel('Custom label').fill(label);
    await page.getByLabel('Explanation').fill(explanation);
    const savedEdge = await saveInGraph(page, 'Add relationship', '/api/contributions/relationships', 'POST', records);
    const relationshipId = `r:${savedEdge.id}`;
    await enter(reader.page, origin, order, 'Order');
    await row(reader.page, relationshipId).locator('[data-graph-inspect-edge]').click();
    await details(reader.page).getByText(explanation, { exact: false }).waitFor();

    await button(page, 'View saved relationship').click();
    await button(page, 'Edit relationship').click();
    await page.getByLabel('Explanation').fill(edited);
    await saveInGraph(page, 'Save relationship', `/api/contributions/${savedEdge.id}`, 'PUT', records);
    await enter(reader.page, origin, order, 'Order');
    await row(reader.page, relationshipId).locator('[data-graph-inspect-edge]').click();
    await details(reader.page).getByText(edited, { exact: false }).waitFor();

    for (const [id, type] of [[savedNote.id, 'annotationCreated'], [savedEdge.id, 'relationshipEdited']]) {
      const history = await reader.request(`/api/history?contributionId=${id}`);
      assert.ok(history.entries.some(event => event.type === type), `History for ${type}`);
    }
    result('Workspace contribution save, edit, history and separate reader');
  } finally {
    await cleanup(manager, records);
  }
}

async function checkReaderRefusal(reader, origin) {
  await enter(reader.page, origin, order, 'Order');
  await button(reader.page, 'Inspect focus').click();
  await details(reader.page).getByRole('heading', { name: `Order · ${ecommerce}`, exact: true }).waitFor();
  assert.equal(await details(reader.page).locator('[data-graph-editor]').count(), 0);
  await reader.request('/api/contributions/annotations', {
    target: order, text: 'Reader graph write refusal', category: 'businessExplanation'
  }, 403);
  result('Reader editor controls and API refusal');
}

async function inputState(fixturePath) {
  const { divisionRoot, commit, checkouts } = JSON.parse(await readFile(fixturePath, 'utf8'));
  const { sources } = JSON.parse(await readFile(join(divisionRoot, 'sources.json'), 'utf8'));
  assert.deepEqual(Object.keys(checkouts).sort(), sources.map(source => source.id).sort());
  const git = (cwd, ...args) => execFileSync('git', ['-c', 'core.fsmonitor=false', ...args], { cwd, encoding: 'utf8' });
  const state = {};
  for (const [id, cwd] of [['division-core', divisionRoot], ...Object.entries(checkouts)]) {
    const revision = git(cwd, 'rev-parse', 'HEAD').trim();
    const status = git(cwd, 'status', '--porcelain');
    assert.equal(status, '', `Synthetic checkout ${id} is clean`);
    const files = git(cwd, 'ls-files', '-z', '--', '*.ttl').split('\0').filter(Boolean);
    const expected = id === 'division-core' ? 'core/ontology.ttl' : sources.find(source => source.id === id).ontologyPath;
    assert.ok(files.includes(expected), `${id} includes ${expected}`);
    const turtle = {};
    for (const file of files) turtle[file] = createHash('sha256').update(await readFile(join(cwd, file))).digest('hex');
    state[id] = { revision, status, turtle };
  }
  assert.equal(state['division-core'].revision, commit, 'The adopting checkout matches the fixture revision');
  return state;
}

export async function verifyGraphExplorer({ manager, reader, origin, snapshot, fixturePath, writes }) {
  const initialSnapshot = (await traverse(reader, order)).snapshotId;
  assert.equal(initialSnapshot, snapshot, 'Graph reads the snapshot activated by this fixture');
  const inputs = fixturePath ? await inputState(fixturePath) : null;
  const runId = randomUUID().slice(0, 8);
  try {
    await verifyGraphDelivery({ page: reader.page, origin });
    await checkDiagramFallback(reader.page, origin);
    result('Direct route, same-origin modules and native fallback');
    const handoff = await checkEntryAndHandoff(reader, origin);
    await checkReaderRefusal(reader, origin);
    if (writes) {
      await checkParallelEdges(manager, reader, origin, handoff);
      await checkPaging(manager, reader, origin, runId);
      await checkRuntimeWrites(manager, reader, origin, runId);
    } else {
      for (const name of ['Parallel published and workspace edges', '101 relationships, continuation and live invalidation',
        'Workspace contribution save, edit, history and separate reader'])
        result(name, 'skipped', 'Home-lab writes require WORKSPACE_VERIFY_GRAPH_WRITES=1');
    }
  } finally {
    assert.equal((await traverse(reader, order)).snapshotId, initialSnapshot, 'Graph use preserves the active published snapshot');
    result('Published snapshot preserved');
    if (fixturePath) {
      assert.deepEqual(await inputState(fixturePath), inputs);
      result('Adopting and source Turtle bytes and Git revisions preserved');
    } else {
      result('Adopting and source Turtle bytes and Git revisions preserved', 'skipped', 'Source checkouts are local to the Mac fixture');
    }
  }
}
