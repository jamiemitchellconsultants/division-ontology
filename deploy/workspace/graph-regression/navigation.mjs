import assert from 'node:assert/strict';
import { evidenceHref } from '../verify-graph-explorer.mjs';

export async function b1({ origin, snapshot, login, stage, pass }) {
  const page = await login('reader');
  const button = name => page.getByRole('button', { name, exact: true });
  const h1 = page.getByRole('heading', { level: 1 });
  const key = source => ({ sourceId: 'fixture/' + source, kind: 'concept', identifier: 'https://example.test/' + source + '/ontology#Order' });
  const encode = value => Buffer.from(JSON.stringify(value)).toString('base64url');

  async function searchOrder() {
    await page.getByRole('searchbox', { name: 'Semantic search' }).fill('Order');
    await button('Search').click();
    return page.getByRole('region', { name: 'Search results' });
  }

  async function openGraph(link, target) {
    const [reply] = await Promise.all([
      page.waitForResponse(r => r.request().method() === 'POST' &&
        new URL(r.url()).pathname === '/api/traverse' &&
        r.request().postDataJSON().start?.sourceId === target.sourceId),
      link.click()
    ]);
    assert.equal(reply.status(), 200);
    const graph = await reply.json();
    assert.equal(graph.snapshotId, snapshot);
    assert.deepEqual(reply.request().postDataJSON().start, target);
    const expected = 'Order · ' + target.sourceId;
    await page.getByRole('heading', { level: 1, name: expected, exact: true }).waitFor();
    const neighbours = page.getByRole('region', { name: 'Graph neighbours', exact: true });
    await neighbours.waitFor();
    const rows = await neighbours.locator('[data-graph-edge] > div:first-of-type').allTextContents();
    assert.equal(rows.length, graph.paths.length);
    assert.ok(rows.every(text => text.includes(' · ') && text.includes(' → ')));
    await button('Inspect focus').click();
    const details = page.getByRole('region', { name: 'Graph details', exact: true });
    await details.getByRole('heading', { name: expected, exact: true }).waitFor();
    await details.getByText(target.identifier, { exact: true }).first().waitFor();
    return { expected, actual: await h1.innerText(), connections: rows.length, result: 'PASS' };
  }

  stage('B1.1 search Order and open the Ecommerce Graph link');
  const ecommerce = await openGraph((await searchOrder()).locator('a[href="graph/' + encode(key('ecommerce')) + '"]').first(), key('ecommerce'));
  pass('B1.1', ecommerce.expected, ecommerce.actual + ', ' + ecommerce.connections + ' connections');
  stage('B1.2 open the Warehouse entity and select Open graph');
  await page.goto(origin);
  await (await searchOrder()).locator('a[href="entity/' + encode(key('warehouse')) + '"]').first().click();

  const warehouse = await openGraph(page.getByRole('link', { name: 'Open graph', exact: true }), key('warehouse'));
  pass('B1.2', warehouse.expected, warehouse.actual + ', ' + warehouse.connections + ' connections');
}

export async function b2({ origin, snapshot, login, stage, pass }) {
  const node = (id, label, source = 'ecommerce') => ({
    key: { sourceId: 'fixture/' + source, kind: 'node', identifier: 'https://example.test/' + source + '/graph#' + id },
    label: label + ' \u00b7 fixture/' + source
  });
  const processNode = node('component', 'Order processing');
  const storefront = node('container', 'Storefront API');
  const ecommerce = node('system', 'Ecommerce');
  const external = node('external', 'Warehouse fulfilment');
  const warehouse = node('system', 'Warehouse fulfilment', 'warehouse');
  const encode = ({ sourceId, kind, identifier }) =>
    Buffer.from(JSON.stringify({ sourceId, kind, identifier })).toString('base64url');
  const same = (a, b) => encode(a) === encode(b);
  const page = await login('reader');
  page.setDefaultTimeout(45000);
  const button = name => page.getByRole('button', { name, exact: true });
  const direction = page.getByLabel('Direction', { exact: true });
  async function read(target, expectedDirection, action) {
    const [reply] = await Promise.all([
      page.waitForResponse(r => r.request().method() === 'POST' && new URL(r.url()).pathname === '/api/traverse'),
      action()
    ]);
    assert.equal(reply.status(), 200);
    const request = reply.request().postDataJSON();
    assert.ok(same(request.start, target.key), 'The traversal focus should match the chosen node');
    assert.equal(request.direction, expectedDirection);
    assert.equal(request.maxDepth, 1);
    const result = await reply.json();
    assert.equal(result.snapshotId, snapshot);
    await page.getByRole('heading', { level: 1, name: target.label, exact: true }).waitFor();
    assert.equal(await direction.inputValue(), expectedDirection);
    return result;
  }

  async function follow(view, from, to, target, sense, kind) {
    stage('inspect and follow ' + from.label + ' -> ' + to.label);
    const step = view.paths.map(path => path.steps[0]).find(s =>
      s.key?.kind === kind && same(s.from, from.key) && same(s.to, to.key));
    assert.ok(step, 'Expected published connection');
    assert.equal(step.direction, sense);
    const arrow = from.label + ' \u2192 ' + to.label;
    const row = page.locator('[data-graph-edge^="p:"]')
      .filter({ has: page.getByText(arrow, { exact: true }) })
      .filter({ has: page.getByText(step.label, { exact: true }) });
    await row.waitFor();
    return read(target, sense, () => row.locator('[data-graph-follow]').click());
  }

  stage('B2.1 search for order placement and open Order processing');
  await page.getByRole('searchbox', { name: 'Semantic search' }).fill('What happens after a customer places an order?');
  await button('Search').click();
  const link = page.getByRole('region', { name: 'Search results' })
    .locator('a[href="graph/' + encode(processNode.key) + '"]').first();
  let result = await read(processNode, 'both', () => link.click());
  assert.ok(result.paths.length > 0 && result.paths.every(path => path.steps.length === 1));
  pass('B2.1', 'Order processing, Both, direct connections', processNode.label + ', Both, ' + result.paths.length + ' direct connection(s)');

  stage('B2.2 choose Incoming');
  result = await read(processNode, 'incoming', () => direction.selectOption('incoming'));
  result = await follow(result, storefront, processNode, storefront, 'incoming', 'assertion');
  result = await follow(result, ecommerce, storefront, ecommerce, 'incoming', 'assertion');
  pass('B2.2', 'Follow Storefront API then Ecommerce, preserving both arrow directions',
    storefront.label + ' then ' + ecommerce.label + '; both arrow directions verified');

  stage('B2.3 choose Outgoing');
  result = await read(ecommerce, 'outgoing', () => direction.selectOption('outgoing'));
  result = await follow(result, ecommerce, external, external, 'outgoing', 'assertion');
  await follow(result, external, warehouse, warehouse, 'outgoing', 'c4Binding');
  pass('B2.3', 'Follow the handoff and binding to Warehouse fulfilment in the Warehouse source',
    external.label + ' then ' + warehouse.label + '; both arrow directions verified');
}

export async function b3({ origin, snapshot, run, login, stage, pass }) {
  const key = source => ({ sourceId: 'fixture/' + source, kind: 'concept', identifier: 'https://example.test/' + source + '/ontology#Order' });
  const ecommerce = key('ecommerce'), warehouse = key('warehouse');
  const graph = origin + '/graph/' + Buffer.from(JSON.stringify(ecommerce)).toString('base64url');
  const seedPrefix = 'paging-acceptance-' + run + '-';
  const page = await login('reader');
  const button = name => page.getByRole('button', { name, exact: true });
  const heading = key => page.getByRole('heading', { name: 'Order \u00b7 ' + key.sourceId, exact: true, level: 1 });
  const direction = page.getByLabel('Direction', { exact: true });
  const details = page.getByRole('region', { name: 'Graph details', exact: true });
  const diagram = page.locator('[data-graph-diagram]');
  const rows = page.locator('[data-graph-edge]');
  const row = id => page.locator('[data-graph-edge="r:' + id + '"]');
  const ids = () => rows.evaluateAll(items => items.map(item => item.dataset.graphEdge));
  const response = () => page.waitForResponse(r =>
    r.request().method() === 'POST' && new URL(r.url()).pathname === '/api/traverse');
  async function read(action, target, sense, cursor = null, status = 200) {
    const [reply] = await Promise.all([response(), action()]);
    const request = reply.request().postDataJSON();
    assert.equal(request.start.sourceId, target.sourceId);
    assert.equal(request.start.identifier, target.identifier);
    assert.equal(request.direction, sense);
    assert.ok((request.cursor ?? null) === cursor, 'Traversal cursor differs');
    assert.equal(reply.status(), status);
    if (status !== 200) return;
    const result = await reply.json();
    assert.equal(result.snapshotId, snapshot);
    await heading(target).waitFor();
    return result;
  }
  async function first() {
    await read(() => page.goto(graph), ecommerce, 'both');
    const result = await read(() => direction.selectOption('outgoing'), ecommerce, 'outgoing');
    await rows.nth(result.paths.length - 1).waitFor();
    return result;
  }
  async function viewport() {
    const value = await page.waitForFunction(async () => {
      const host = document.querySelector('[data-graph-diagram]');
      if (!host) return false;
      const bridge = await import(new URL(host.dataset.graphModule, document.baseURI));
      const state = bridge.diagramFor(host)?.captureViewport();
      return state && { pan: state.pan, zoom: state.zoom };
    });
    try { return await value.jsonValue(); }
    finally { await value.dispose(); }
  }
  const flat = view => view.paths.map(path => path.steps[0]);
  const inspect = id => row(id).locator('[data-graph-inspect-edge]');
  const follow = id => read(() => row(id).locator('[data-graph-follow]').click(), warehouse, 'outgoing');

  stage('B3.1 zoom, pan, and load the next page');
  const initial = await first();
  assert.equal(initial.paths.length, 100);
  assert.ok(initial.nextCursor);
  const original = await viewport();
  await button('Zoom in').click();
  const zoomed = await viewport();
  assert.ok(zoomed.zoom > original.zoom);
  await diagram.scrollIntoViewIfNeeded();
  const box = await diagram.boundingBox();
  const start = { x: box.x + box.width * 0.87, y: box.y + box.height * 0.78 };
  await page.mouse.move(start.x, start.y);
  await page.mouse.down();
  await page.mouse.move(start.x - 75, start.y - 45, { steps: 10 });
  await page.mouse.up();
  assert.notDeepEqual((await viewport()).pan, zoomed.pan);
  const next = await read(() => button('Load more').click(), ecommerce, 'outgoing', initial.nextCursor);
  const seedRows = [...flat(initial), ...flat(next)].filter(step => step.label.startsWith(seedPrefix));
  assert.equal(new Set(seedRows.map(step => step.runtimeId)).size, 105);
  const late = flat(next).find(step => step.label.startsWith(seedPrefix));
  assert.ok(late?.runtimeId);
  await inspect(late.runtimeId).click();
  const inspected = details.getByRole('heading', { name: late.label, exact: true });
  await inspected.waitFor();
  const beforeBack = { ids: await ids(), viewport: await viewport() };
  await follow(late.runtimeId);
  pass('B3.1', 'Load more after zoom and pan, inspect an edge, and follow Warehouse Order',
    '100 to ' + beforeBack.ids.length + ' connections, 105 seed IDs, then Order \u00b7 fixture/warehouse');

  stage('B3.2 restore the cached graph with Back');
  await button('Back').click();
  await heading(ecommerce).waitFor();
  await inspected.waitFor();
  assert.equal(await direction.inputValue(), 'outgoing');
  assert.deepEqual(await ids(), beforeBack.ids);
  assert.equal(await inspect(late.runtimeId).getAttribute('aria-pressed'), 'true');
  const restored = await viewport();
  for (const [a, b] of [[restored.zoom, beforeBack.viewport.zoom],
    [restored.pan.x, beforeBack.viewport.pan.x], [restored.pan.y, beforeBack.viewport.pan.y]]) {
    assert.ok(Math.abs(a - b) < 1e-6, 'Back changed the viewport');
  }
  assert.equal(await page.locator('[data-graph-close-inspector]:focus').count(), 0);
  pass('B3.2', 'Back restores focus, direction, rows, viewport and inspector without moving focus into it',
    'Ecommerce Order, Outgoing, ' + restored.zoom.toFixed(3) + ' zoom, same pan, rows and selection');
}

export async function b33({ origin, snapshot, run, login, stage, pass }) {
  const key = source => ({
    sourceId: 'fixture/' + source, kind: 'concept',
    identifier: 'https://example.test/' + source + '/ontology#Order'
  });
  const ecommerce = key('ecommerce'), warehouse = key('warehouse');
  const graph = origin + '/graph/' + Buffer.from(JSON.stringify(ecommerce)).toString('base64url');
  const prefix = 'paging-acceptance-' + run + '-';
  const page = await login('reader');
  const button = name => page.getByRole('button', { name, exact: true });
  const heading = key => page.getByRole('heading', { name: 'Order \u00b7 ' + key.sourceId, exact: true, level: 1 });
  const response = path => page.waitForResponse(r =>
    r.request().method() === 'POST' && new URL(r.url()).pathname === path);
  async function read(action, focus, direction, cursor = null, status = 200) {
    const [reply] = await Promise.all([response('/api/traverse'), action()]);
    const request = reply.request().postDataJSON();
    assert.equal(request.start.identifier, focus.identifier);
    assert.equal(request.start.sourceId, focus.sourceId);
    assert.equal(request.direction, direction);
    assert.ok((request.cursor ?? null) === cursor);
    assert.equal(reply.status(), status);
    if (status !== 200) return;
    const data = await reply.json();
    assert.equal(data.snapshotId, snapshot);
    await heading(focus).waitFor();
    return data;
  }

  stage('prepare the saved Ecommerce frame');
  await read(() => page.goto(graph), ecommerce, 'both');
  const first = await read(() => page.getByLabel('Direction', { exact: true }).selectOption('outgoing'), ecommerce, 'outgoing');
  assert.ok(first.nextCursor);
  const next = await read(() => button('Load more').click(), ecommerce, 'outgoing', first.nextCursor);
  const seeded = data => data.paths.map(path => path.steps[0]).find(step => step.label.startsWith(prefix));
  const late = seeded(next);
  assert.ok(late?.runtimeId);
  const follow = id => read(() => page.locator('[data-graph-edge="r:' + id + '"] [data-graph-follow]').click(), warehouse, 'outgoing');

  async function controlled(path, result) {
    let applied = false;
    await page.route('**' + path, async route => {
      try { await route.fulfill(result); applied = true; }
      catch { await route.abort().catch(() => {}); }
    }, { times: 1 });
    return () => assert.ok(applied, 'The controlled response did not reach Chromium');
  }

  async function invalidateHistory() {
    const [live] = await Promise.all([response('/api/entities/detail'), button('Inspect focus').click()]);
    assert.equal(live.status(), 200);
    const detail = await live.json();
    assert.equal(detail.snapshotId, snapshot);
    assert.equal(detail.entity.key.identifier, warehouse.identifier);
    await button('Close details').click();
    const simulated = (snapshot[0] === '0' ? '1' : '0') + snapshot.slice(1);
    const used = await controlled('/api/entities/detail', { status: 200, json: { ...detail, snapshotId: simulated } });
    await button('Inspect focus').click();
    await page.getByText('These graph results are out of date. Reload to check for changes.', { exact: true }).waitFor();
    used();
  }

  stage('B3.3a refresh the stale saved frame');
  await follow(late.runtimeId);
  await invalidateHistory();
  const refreshed = await read(() => button('Back').click(), ecommerce, 'outgoing');
  await page.getByText('The saved graph view was refreshed.', { exact: true }).waitFor();
  await page.locator('[data-graph-load-more]:enabled').waitFor();
  assert.equal(refreshed.paths.length, 100);
  assert.equal(await page.locator('[data-graph-edge]').count(), 100);
  pass('B3.3a', 'Reload saved focus and direction from page one',
    'Ecommerce Order, Outgoing, 100 rows and refreshed-view message');

  stage('B3.3b handle the unavailable saved focus');
  const visible = seeded(refreshed);
  assert.ok(visible?.runtimeId);
  await follow(visible.runtimeId);
  await invalidateHistory();
  const used = await controlled('/api/traverse', { status: 404, contentType: 'application/problem+json',
    json: { status: 404, code: 'entityNotFound', title: 'Published entity was not found.' } });
  await read(() => button('Back').click(), ecommerce, 'outgoing', null, 404);
  await page.getByText(/Could not load the graph for .*Order.*fixture\/ecommerce/).waitFor();
  await heading(warehouse).waitFor();
  await page.getByRole('link', { name: 'Back to search', exact: true }).waitFor();
  await button('Return to current view').waitFor();
  used();
  pass('B3.3b', 'Identify unavailable focus and keep last successful view',
    'Ecommerce unavailable, Warehouse visible, search and return actions offered');
}

export async function b4({ origin, snapshot, run, login, stage, pass }) {
  const focus = { sourceId: 'fixture/ecommerce', kind: 'concept', identifier: 'https://example.test/ecommerce/ontology#Order' };
  const graph = origin + '/graph/' + Buffer.from(JSON.stringify(focus)).toString('base64url');
  const page = await login('reader');
  const button = name => page.getByRole('button', { name, exact: true });
  const direction = page.getByLabel('Direction', { exact: true });
  const heading = source => page.getByRole('heading', { name: 'Order \u00b7 fixture/' + source, exact: true, level: 1 });
  const row = id => page.locator('[data-graph-edge="r:' + id + '"]');
  const selected = id => row(id).locator('[data-graph-inspect-edge]');
  const response = () => page.waitForResponse(r =>
    r.request().method() === 'POST' && new URL(r.url()).pathname === '/api/traverse');
  const seed = data => data.paths.map(path => path.steps[0]).find(step => step.label.startsWith('paging-acceptance-' + run + '-'));

  async function read(action, source, sense, cursor = null) {
    const [reply] = await Promise.all([response(), action()]);
    const request = reply.request().postDataJSON();
    assert.equal(reply.status(), 200);
    assert.equal(request.start.sourceId, 'fixture/' + source);
    assert.equal(request.direction, sense);
    assert.ok((request.cursor ?? null) === cursor);
    const result = await reply.json();
    assert.equal(result.snapshotId, snapshot);
    await heading(source).waitFor();
    return result;
  }

  async function tabTo(control) {
    for (let n = 0; n < 45; n++) {
      if (await control.evaluate(el => el === document.activeElement)) return;
      await page.keyboard.press('Tab');
    }
    throw new Error('The target was not reached by Tab');
  }

  async function hasFocus(control) {
    const handle = await control.elementHandle();
    try { await page.waitForFunction(el => document.activeElement === el, handle); }
    finally { await handle.dispose(); }
  }

  async function inspectByKeyboard(step) {
    await button('Inspect focus').focus();
    await tabTo(selected(step.runtimeId));
    await page.keyboard.press('Enter');
    await page.getByRole('region', { name: 'Graph details', exact: true })
      .getByRole('heading', { name: step.label, exact: true }).waitFor();
    await hasFocus(button('Close details'));
  }

  stage('B4.1 Tab to inspect and Enter into details');
  const first = await read(() => page.goto(graph), 'ecommerce', 'both');
  const initialEdge = seed(first);
  assert.ok(initialEdge?.runtimeId);
  const activeRow = row(initialEdge.runtimeId);
  assert.match(await activeRow.innerText(), /Outgoing.*Workspace relationship/s);
  await activeRow.getByText('Order \u00b7 fixture/ecommerce \u2192 Order \u00b7 fixture/warehouse', { exact: true }).waitFor();
  await inspectByKeyboard(initialEdge);
  pass('B4.1', 'Tab reaches a source-qualified row and Enter opens details with Close details focused',
    'Outgoing workspace edge showed both sources and focused Close details');

  stage('B4.2 close the inspector, follow, and go Back by keyboard');
  await page.keyboard.press('Enter');
  await hasFocus(selected(initialEdge.runtimeId));
  const follow = activeRow.locator('[data-graph-follow]');
  await tabTo(follow);
  await read(() => page.keyboard.press('Enter'), 'warehouse', 'both');
  await button('Sign out').focus();
  await tabTo(button('Back'));
  await page.keyboard.press('Enter');
  await heading('ecommerce').waitFor();
  assert.equal(await direction.inputValue(), 'both');
  pass('B4.2', 'Closing returns to the same row, Follow opens Warehouse, and Back restores Ecommerce',
    'Original inspect control regained focus, then Warehouse Order, then Ecommerce Order');
}

export async function b43({ origin, snapshot, run, lateRelationship, login, stage, pass }) {
  const focus = { sourceId: 'fixture/ecommerce', kind: 'concept', identifier: 'https://example.test/ecommerce/ontology#Order' };
  const graph = origin + '/graph/' + Buffer.from(JSON.stringify(focus)).toString('base64url');
  const page = await login('reader');
  const button = name => page.getByRole('button', { name, exact: true });
  const direction = page.getByLabel('Direction', { exact: true });
  const heading = page.getByRole('heading', { name: 'Order \u00b7 fixture/ecommerce', exact: true, level: 1 });
  const row = id => page.locator('[data-graph-edge="r:' + id + '"]');
  const rows = page.locator('[data-graph-edge]');
  const watch = (filter, timeout) => page.waitForResponse(reply => {
    const req = reply.request();
    return req.method() === 'POST' && new URL(req.url()).pathname === '/api/traverse' && filter(req.postDataJSON());
  }, { timeout });
  async function capture(reply) {
    assert.equal(reply.status(), 200);
    assert.equal(reply.request().postDataJSON().start.identifier, focus.identifier);
    const data = await reply.json();
    assert.equal(data.snapshotId, snapshot);
    return data;
  }

  stage('open the busy Ecommerce graph in Both');
  const [first] = await Promise.all([watch(q => q.direction === 'both', 30000), page.goto(graph)]);
  await capture(first);
  await heading.waitFor();
  assert.equal(await direction.inputValue(), 'both');

  stage('change Direction to Outgoing by keyboard');
  const outgoingQuery = q => q.direction === 'outgoing' && !q.cursor;
  const observed = [];
  const trackDirection = req => {
    if (req.method() === 'POST' && new URL(req.url()).pathname === '/api/traverse') {
      const value = req.postDataJSON().direction;
      if (['both', 'incoming', 'outgoing'].includes(value)) observed.push(value);
    }
  };
  page.on('request', trackDirection);
  async function attempt(keys) {
    const pending = watch(outgoingQuery, 4500).catch(() => null);
    await direction.focus();
    for (const key of keys) await page.keyboard.press(key);
    const reply = await pending;
    if (!reply) console.log('Keyboard diagnostic: selected=' + await direction.inputValue() +
      '; traversal directions=' + (observed.join(',') || 'none'));
    return reply;
  }

  let outgoingReply = await attempt(['ArrowDown', 'ArrowDown', 'Enter', 'Tab']);
  let input = 'ArrowDown, ArrowDown, Enter, Tab';
  if (!outgoingReply) {
    await page.keyboard.press('Escape');
    outgoingReply = await attempt(['Alt+ArrowDown', 'End', 'Enter', 'Tab']);
    input = 'Alt+ArrowDown, End, Enter, Tab';
  }
  if (!outgoingReply && process.platform !== 'win32') {
    await page.keyboard.press('Escape');
    outgoingReply = await attempt(['o']);
    input = 'type-ahead';
  }
  assert.ok(outgoingReply, 'No Outgoing traversal was observed');
  const outgoing = await capture(outgoingReply);
  assert.equal(await direction.inputValue(), 'outgoing');
  assert.equal(outgoing.paths.length, 100);


  async function tabTo(control) {
    for (let n = 0; n < 40; n++) {
      if (await control.evaluate(el => el === document.activeElement)) return;
      await page.keyboard.press('Tab');
    }
    throw new Error('Target was not reached by Tab');
  }
  async function zoom() {
    return page.locator('[data-graph-diagram]').evaluate(async host => {
      const module = await import(new URL(host.dataset.graphModule, document.baseURI));
      return module.diagramFor(host).captureViewport().zoom;
    });
  }
  async function pressZoom(name) {
    await direction.focus();
    await tabTo(button(name));
    await page.keyboard.press('Enter');
    return zoom();
  }

  stage('inspect a seeded edge and load more by keyboard');
  const edge = outgoing.paths.map(path => path.steps[0]).find(step => step.label.startsWith('paging-acceptance-' + run + '-'));
  assert.ok(edge?.runtimeId && outgoing.nextCursor);
  const inspect = row(edge.runtimeId).locator('[data-graph-inspect-edge]');
  await button('Inspect focus').focus();
  await tabTo(inspect);
  await page.keyboard.press('Enter');
  await page.getByRole('region', { name: 'Graph details', exact: true })
    .getByRole('heading', { name: edge.label, exact: true }).waitFor();
  await page.locator('[data-graph-close-inspector]:focus').waitFor();
  await page.keyboard.press('Enter');
  await row(edge.runtimeId).locator('[data-graph-inspect-edge]:focus').waitFor();
  const before = await rows.count();
  assert.equal(before, 100);
  assert.equal(await row(lateRelationship.id).count(), 0);
  await direction.focus();
  await tabTo(button('Load more'));
  const [next] = await Promise.all([
    watch(q => q.direction === 'outgoing' && q.cursor === outgoing.nextCursor, 30000),
    page.keyboard.press('Enter')
  ]);
  await capture(next);
  await row(lateRelationship.id).waitFor();
  await button('Load more').waitFor({ state: 'detached' });
  const after = await rows.count();
  assert.ok(after >= 105);

  stage('zoom in, out, and reset by keyboard');
  await button('Zoom in').waitFor();
  const base = await zoom();
  const larger = await pressZoom('Zoom in');
  const smaller = await pressZoom('Zoom out');
  const beforeReset = await pressZoom('Zoom in');
  const reset = await pressZoom('Reset zoom');
  assert.ok(larger > base && smaller < larger && Math.abs(reset - beforeReset) > 1e-6);
  assert.equal(await inspect.getAttribute('aria-pressed'), 'true');
  assert.equal(await direction.inputValue(), 'outgoing');
  await heading.waitFor();
  const expected = 'Keyboard direction, paging and zoom preserve Ecommerce focus and selected relationship';
  const actual = `Outgoing, ${before} to ${after} rows, zoom ${base.toFixed(2)} / ${larger.toFixed(2)} / ${smaller.toFixed(2)} / reset ${reset.toFixed(2)}, same focus and selection`;
  pass('B4.3', expected, actual + ' (' + input + ')');
}

export async function b5({ origin, snapshot, run, login, stage, pass }) {
  const graphKey = source => ({
    sourceId: 'fixture/' + source, kind: 'concept',
    identifier: 'https://example.test/' + source + '/ontology#Order'
  });
  const ecommerce = graphKey('ecommerce'), warehouse = graphKey('warehouse');
  const graph = origin + '/graph/' + Buffer.from(JSON.stringify(ecommerce)).toString('base64url');
  const failureText = 'B5 controlled temporary follow failure.';
  const routePattern = '**/api/traverse';
  const page = await login('reader');
  const button = name => page.getByRole('button', { name, exact: true });
  const direction = page.getByLabel('Direction', { exact: true });
  const heading = key => page.getByRole('heading', { name: 'Order \u00b7 ' + key.sourceId, exact: true, level: 1 });
  const rows = page.locator('[data-graph-edge]');
  const ids = () => rows.evaluateAll(items => items.map(e => e.dataset.graphEdge));
  const watch = query => page.waitForResponse(reply => {
    const req = reply.request();
    return req.method() === 'POST' && new URL(req.url()).pathname === '/api/traverse' && query(req.postDataJSON());
  });
  async function traverse(action, key, sense, expectedStatus = 200) {
    const [reply] = await Promise.all([
      watch(query => query.start.identifier === key.identifier && query.direction === sense),
      action()
    ]);
    assert.equal(reply.status(), expectedStatus);
    const query = reply.request().postDataJSON();
    if (expectedStatus !== 200) return { query };
    const body = await reply.json();
    assert.equal(body.snapshotId, snapshot);
    await heading(key).waitFor();
    return { body, query };
  }

  let release;
  try {
    stage('capture a live Incoming response in Chromium');
    await traverse(() => page.goto(graph), ecommerce, 'both');
    const incoming = await traverse(() => direction.selectOption('incoming'), ecommerce, 'incoming');
    await traverse(() => direction.selectOption('both'), ecommerce, 'both');

    stage('hold Incoming, load Outgoing, then release Incoming');
    const gate = new Promise(resolve => { release = resolve; });
    let finish;
    const finished = new Promise(resolve => { finish = resolve; });
    const hold = async route => {
      try {
        if (route.request().postDataJSON().direction !== 'incoming') {
          await route.continue();
          return;
        }
        await gate;
        await route.fulfill({ status: 200, json: incoming.body });
        finish('released to Chromium');
      } catch {
        finish('discarded by Chromium');
        await route.abort().catch(() => {});
      }
    };
    await page.route(routePattern, hold);
    await Promise.all([
      page.waitForRequest(req => req.method() === 'POST' && new URL(req.url()).pathname === '/api/traverse' &&
        req.postDataJSON().direction === 'incoming'),
      direction.selectOption('incoming')
    ]);
    assert.equal(await direction.inputValue(), 'incoming');
    const outgoing = await traverse(() => direction.selectOption('outgoing'), ecommerce, 'outgoing');
    await page.locator('[data-graph-load-more]:enabled').waitFor();
    const before = await ids();
    assert.equal(before.length, outgoing.body.paths.length);
    assert.equal(before.length, 100);
    release();
    const disposition = await finished;
    await page.unroute(routePattern, hold);
    assert.equal(await direction.inputValue(), 'outgoing');
    assert.deepEqual(await ids(), before);
    pass('B5.1', 'The late Incoming response leaves Outgoing and its loaded rows in place',
      incoming.body.paths.length + ' captured Incoming paths, ' + disposition + ', Outgoing kept all ' + before.length + ' rows');

    const edge = outgoing.body.paths.map(path => path.steps[0]).find(step =>
      step.label.startsWith('paging-acceptance-' + run + '-'));
    assert.ok(edge?.runtimeId);
    const follow = page.locator('[data-graph-edge="r:' + edge.runtimeId + '"] [data-graph-follow]');
    async function failFollow() {
      await page.route(routePattern, async route => {
        try {
          await route.fulfill({ status: 503, contentType: 'application/problem+json',
            json: { status: 503, code: 'unavailable', title: failureText } });
        } catch { await route.abort().catch(() => {}); }
      }, { times: 1 });
      const failure = await traverse(() => follow.click(), warehouse, 'outgoing', 503);
      await page.getByText(/Could not load the graph for .*Order.*fixture\/warehouse/).waitFor();
      await page.getByRole('alert').filter({ hasText: failureText }).waitFor();
      await button('Retry').waitFor();
      await button('Return to current view').waitFor();
      await heading(ecommerce).waitFor();
      assert.equal(await direction.inputValue(), 'outgoing');
      assert.deepEqual(await ids(), before);
      return failure.query;
    }

    stage('temporary follow failure and Return to current view');
    const failed = await failFollow();
    await button('Return to current view').click();
    await button('Retry').waitFor({ state: 'detached' });
    assert.deepEqual(await ids(), before);
    pass('B5.2', 'Identify the failed Warehouse follow, preserve the graph and offer both recovery actions',
      'Browser-only 503 named Warehouse, Ecommerce kept 100 rows, Return cleared the error');

    stage('retry the same Warehouse follow against the live API');
    assert.deepEqual(await failFollow(), failed);
    const retry = await traverse(() => button('Retry').click(), warehouse, 'outgoing');
    assert.deepEqual(retry.query, failed);
    assert.equal(await direction.inputValue(), 'outgoing');
    await button('Retry').waitFor({ state: 'detached' });
    pass('B5.3', 'Retry sends the same query and opens Warehouse Order',
      'Identical query returned HTTP 200 and opened Warehouse Order in Outgoing');
  } finally { release?.(); }
}

export async function b6({ origin, snapshot, login, stage, pass }) {
  const focus = { sourceId: 'fixture/ecommerce', kind: 'node', identifier: 'https://example.test/ecommerce/graph#system' };
  const target = 'https://example.test/ecommerce/graph#external';
  const name = 'Ecommerce \u00b7 fixture/ecommerce';
  const graph = origin + '/graph/' + Buffer.from(JSON.stringify(focus)).toString('base64url');
  const page = await login('reader');
  const button = name => page.getByRole('button', { name, exact: true });
  const panel = page.getByRole('region', { name: 'Graph details', exact: true });
  const heading = page.getByRole('heading', { name, exact: true, level: 1 });
  const exact = (scope, text) => scope.getByText(text, { exact: true });
  const ids = () => page.locator('[data-graph-edge]').evaluateAll(rows => rows.map(row => row.dataset.graphEdge));
  const watch = path => page.waitForResponse(r => r.request().method() === 'POST' && new URL(r.url()).pathname === path);

  stage('load the Ecommerce system and capture the graph viewport');
  const [graphResponse] = await Promise.all([watch('/api/traverse'), page.goto(graph)]);
  assert.equal(graphResponse.status(), 200);
  const neighbourhood = await graphResponse.json();
  assert.equal(neighbourhood.snapshotId, snapshot);
  const handoff = neighbourhood.paths.map(path => path.steps[0]).find(step =>
    step.key?.kind === 'assertion' && step.from.identifier === focus.identifier && step.to.identifier === target);
  assert.ok(handoff?.provenance?.evidence?.length);
  await heading.waitFor();
  const direction = page.getByLabel('Direction', { exact: true });
  assert.equal(await direction.inputValue(), 'both');

  async function viewport() {
    const handle = await page.waitForFunction(async () => {
      const host = document.querySelector('[data-graph-diagram]');
      if (!host) return false;
      const module = await import(new URL(host.dataset.graphModule, document.baseURI));
      const state = module.diagramFor(host)?.captureViewport();
      return state && { pan: state.pan, zoom: state.zoom };
    });
    try { return await handle.jsonValue(); }
    finally { await handle.dispose(); }
  }

  const original = await viewport();
  await button('Zoom in').click();
  const before = await viewport();
  assert.ok(before.zoom > original.zoom);
  const initialIds = await ids();
  assert.ok(initialIds.length > 0);

  stage('B6.1 inspect the focus and the published handoff');
  const [focusResponse] = await Promise.all([watch('/api/entities/detail'), button('Inspect focus').click()]);
  assert.equal(focusResponse.status(), 200);
  const focusDetail = await focusResponse.json();
  assert.equal(focusDetail.snapshotId, snapshot);
  assert.equal(focusDetail.entity.key.identifier, focus.identifier);
  const focusOrigin = panel.locator(':scope > .origin').first();
  await panel.getByRole('heading', { name, exact: true }).waitFor();
  await exact(focusOrigin, focus.identifier).waitFor();
  await exact(focusOrigin, focusDetail.entity.provenance.sourceCommit).waitFor();
  if (focusDetail.entity.description) await exact(panel, focusDetail.entity.description).waitFor();

  const endpoints = name + ' \u2192 Warehouse fulfilment \u00b7 fixture/ecommerce';
  const handoffRow = page.locator('[data-graph-edge^="p:"]')
    .filter({ has: exact(page, endpoints) }).filter({ has: exact(page, handoff.label) });
  await handoffRow.locator('[data-graph-inspect-edge]').click();
  await panel.getByRole('heading', { name: handoff.label, exact: true }).waitFor();
  await panel.getByText(/^Published relationship.*Outgoing/).waitFor();
  await exact(panel, endpoints).waitFor();
  const provenance = panel.locator(':scope > .origin').first();
  await exact(provenance, handoff.key.identifier).waitFor();
  await exact(provenance, handoff.provenance.sourceCommit).waitFor();
  pass('B6.1', 'Focus and handoff show source, identifiers, revision, and direction',
    'Ecommerce and its published outgoing Warehouse fulfilment handoff showed their exact identifiers and revisions');

  stage('B6.2 verify the configured evidence behaviour');
  const citation = handoff.provenance.evidence[0];
  const citationRow = provenance.getByRole('list', { name: 'Source evidence' }).locator('li')
    .filter({ has: exact(page, citation.path) }).first();
  await citationRow.waitFor();
  assert.ok((await citationRow.innerText()).includes('lines ' + citation.startLine + '\u2013' + citation.endLine));
  const settings = await page.evaluate(async () => {
    const response = await fetch('/appsettings.json');
    if (!response.ok) throw new Error('Could not read graph settings');
    const body = await response.json();
    return body.EvidenceRepositories?.['fixture/ecommerce'] ?? null;
  });
  const link = citationRow.getByRole('link', { name: 'View evidence', exact: true });
  let evidenceResult;
  if (settings) {
    assert.match(settings, /^https:\/\/github\.com\/[A-Za-z0-9_.-]+\/[A-Za-z0-9_.-]+$/);
    const href = evidenceHref(settings, handoff.provenance.sourceCommit, citation);
    assert.equal(await link.getAttribute('href'), href);
    const destination = new URL(href);
    const [popup] = await Promise.all([
      page.waitForEvent('popup'),
      page.context().waitForEvent('request', { predicate: req => {
        const requested = new URL(req.url());
        return req.isNavigationRequest() && requested.origin === destination.origin && requested.pathname === destination.pathname;
      } }),
      link.click()
    ]);
    await popup.close();
    evidenceResult = 'New tab requested the exact pinned commit and file with the expected line range in the link';
  } else {
    await citationRow.getByText(/Evidence link unavailable/).waitFor();
    assert.equal(await link.count(), 0);
    evidenceResult = 'Plain citation and Evidence link unavailable';
  }
  pass('B6.2', 'Use the live source map or show the plain citation when unset',
    evidenceResult + ', ' + citation.path + ' lines ' + citation.startLine + '-' + citation.endLine);

  stage('B6.3 close details and compare the view');
  await button('Close details').click();
  await panel.waitFor({ state: 'detached' });
  await heading.waitFor();
  assert.equal(await direction.inputValue(), 'both');
  assert.deepEqual(await ids(), initialIds);
  assert.deepEqual(await viewport(), before);
  pass('B6.3', 'Close details preserves focus, direction, rows and viewport',
    'Ecommerce, Both, same ' + initialIds.length + ' connection IDs, pan and zoom');
}
