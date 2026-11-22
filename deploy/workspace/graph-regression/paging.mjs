import assert from 'node:assert/strict';

export async function b8({ origin, snapshot, run, lateRelationship, login, stage, pass }) {
  const focus = { sourceId: 'fixture/ecommerce', kind: 'concept', identifier: 'https://example.test/ecommerce/ontology#Order' };
  const graph = origin + '/graph/' + Buffer.from(JSON.stringify(focus)).toString('base64url');
  const page = await login('reader');
  const late = lateRelationship.id;
  const button = name => page.getByRole('button', { name, exact: true });
  const direction = page.getByLabel('Direction', { exact: true });
  const heading = page.getByRole('heading', { name: 'Order \u00b7 fixture/ecommerce', exact: true, level: 1 });
  const more = page.locator('[data-graph-load-more]');
  const enabledMore = page.locator('[data-graph-load-more]:enabled');
  const row = id => page.locator('[data-graph-edge="r:' + id + '"]');
  const inspect = id => row(id).locator('[data-graph-inspect-edge]');
  const details = name => page.getByRole('region', { name: 'Graph details', exact: true })
    .getByRole('heading', { name, exact: true });
  const ids = () => page.locator('[data-graph-edge]').evaluateAll(items => items.map(item => item.dataset.graphEdge));
  const flat = view => view.paths.map(path => path.steps[0]);
  const isSeed = step => step.label.startsWith('paging-acceptance-' + run + '-');

  async function read(action, test) {
    const [reply] = await Promise.all([
      page.waitForResponse(r => r.request().method() === 'POST' &&
        new URL(r.url()).pathname === '/api/traverse' && test(r.request().postDataJSON())),
      action()
    ]);
    assert.equal(reply.status(), 200);
    assert.equal(reply.request().postDataJSON().start.identifier, focus.identifier);
    const data = await reply.json();
    assert.equal(data.snapshotId, snapshot);
    await heading.waitFor();
    return data;
  }

  async function viewport() {
    const view = await page.waitForFunction(async () => {
      const host = document.querySelector('[data-graph-diagram]');
      if (!host) return false;
      const bridge = await import(new URL(host.dataset.graphModule, document.baseURI));
      const state = bridge.diagramFor(host)?.captureViewport();
      return state && { zoom: state.zoom, pan: state.pan };
    });
    try { return await view.jsonValue(); }
    finally { await view.dispose(); }
  }

  async function prepare() {
    await read(() => page.goto(graph), q => q.direction === 'both');
    const first = await read(() => direction.selectOption('outgoing'), q => q.direction === 'outgoing' && !q.cursor);
    assert.equal(first.paths.length, 100);
    assert.ok(first.nextCursor);
    await enabledMore.waitFor();
    const selected = flat(first).find(isSeed);
    assert.ok(selected?.runtimeId);
    await inspect(selected.runtimeId).click();
    await details(selected.label).waitFor();
    const original = await viewport();
    await button('Zoom in').click();
    const zoomed = await viewport();
    assert.ok(zoomed.zoom > original.zoom);
    const shown = await ids();
    assert.equal(shown.length, 100);
    assert.equal(await row(late).count(), 0);
    return { first, selected, shown, viewport: zoomed };
  }

  async function sameView(before) {
    await heading.waitFor();
    assert.equal(await direction.inputValue(), 'outgoing');
    assert.equal(await inspect(before.selected.runtimeId).getAttribute('aria-pressed'), 'true');
    await details(before.selected.label).waitFor();
    const after = await viewport();
    for (const [a, b] of [[after.zoom, before.viewport.zoom],
      [after.pan.x, before.viewport.pan.x], [after.pan.y, before.viewport.pan.y]]) {
      assert.ok(Math.abs(a - b) < 1e-6, 'Graph viewport changed');
    }
  }

  async function completed(before, final) {
    assert.equal(final.nextCursor ?? null, null);
    assert.ok(flat(final).some(step => step.runtimeId === late));
    await row(late).waitFor();
    await more.waitFor({ state: 'detached' });
    await sameView(before);
    const all = await ids();
    assert.equal(all.length, new Set(all).size);
    assert.equal(all.length, before.first.paths.length + final.paths.length);
    assert.ok(before.shown.every(id => all.includes(id)));
    const seeds = [...flat(before.first), ...flat(final)].filter(isSeed);
    assert.equal(seeds.length, 105);
    assert.equal(new Set(seeds.map(step => step.runtimeId)).size, 105);
    assert.ok(seeds.every(step => all.includes('r:' + step.runtimeId)));
    return all;
  }

  stage('B8.1 use the live continuation');
  const baseline = await prepare();
  const final = await read(() => button('Load more').click(), q => q.cursor === baseline.first.nextCursor);
  const liveIds = await completed(baseline, final);
  pass('B8.1', 'Find the known late relationship, keep the view and show each seed exactly once',
    '100 to ' + liveIds.length + ' rows, known late relationship present, all 105 seeds once, view preserved');

  stage('B8.2 insert an empty page with a continuation');
  const controlled = await prepare();
  assert.deepEqual(controlled.shown, baseline.shown);
  const sentinel = 'b8-browser-only-next-' + run;
  let emptyServed = false, finalForwarded = false;
  await page.route('**/api/traverse', async route => {
    try {
      const body = route.request().postDataJSON();
      if (!emptyServed && body.cursor === controlled.first.nextCursor) {
        emptyServed = true;
        await route.fulfill({ status: 200, json: { ...final, paths: [], entities: [], truncated: true, nextCursor: sentinel } });
      } else if (body.cursor === sentinel) {
        finalForwarded = true;
        await route.continue({ postData: { ...body, cursor: controlled.first.nextCursor } });
      } else {
        await route.continue();
      }
    } catch { await route.abort().catch(() => {}); }
  });
  const pending = q => q.direction === 'outgoing' && Boolean(q.cursor);
  const empty = await read(() => button('Load more').click(), pending);
  assert.ok(emptyServed && empty.nextCursor === sentinel);
  assert.equal(empty.paths.length, 0);
  await enabledMore.waitFor();
  assert.deepEqual(await ids(), controlled.shown);
  await sameView(controlled);
  pass('B8.2', 'An empty page leaves the loaded rows and Load more available',
    'Browser-only empty page kept all 100 rows, view and enabled Load more');

  stage('B8.3 use the empty page continuation to load the live final page');
  const last = await read(() => button('Load more').click(), pending);
  assert.ok(finalForwarded);
  const lastIds = await completed(controlled, last);
  assert.deepEqual(lastIds, liveIds);
  pass('B8.3', 'Keep all loaded rows and hide Load more after the final page',
    'Live final page showed all ' + lastIds.length + ' rows, 105 unique seeds, and no Load more');
}

export async function b9({ origin, snapshot, login, stage, pass }) {
  const core = { sourceId: 'division-core', kind: 'concept', identifier: 'https://github.com/jamiemitchellconsultants/division-ontology#BusinessOrder' };
  const order = { sourceId: 'fixture/ecommerce', kind: 'concept', identifier: 'https://example.test/ecommerce/ontology#Order' };
  const url = key => origin + '/graph/' + Buffer.from(JSON.stringify(key)).toString('base64url');
  const routePattern = '**/api/traverse';
  const page = await login('reader');
  const button = name => page.getByRole('button', { name, exact: true });
  const exact = text => page.getByText(text, { exact: true });
  const rows = page.locator('[data-graph-edge]');
  const more = page.locator('[data-graph-load-more]');
  const empty = exact('No visible connections in this direction.');
  const coverage = exact('Source coverage is incomplete for this neighbourhood.');
  const incomplete = exact('Results are incomplete and further paging is unavailable.');

  async function traverse(action, key, direction) {
    const [reply] = await Promise.all([
      page.waitForResponse(r => r.request().method() === 'POST' && new URL(r.url()).pathname === '/api/traverse' &&
        r.request().postDataJSON().start.identifier === key.identifier && r.request().postDataJSON().direction === direction),
      action()
    ]);
    assert.equal(reply.status(), 200);
    const query = reply.request().postDataJSON();
    assert.equal(query.start.sourceId, key.sourceId);
    assert.equal(query.includeUnavailable, false);
    const body = await reply.json();
    assert.equal(body.snapshotId, snapshot);
    return { body, query };
  }
  async function open(key, label) {
    const result = await traverse(() => page.goto(url(key)), key, 'both');
    await page.getByRole('heading', { level: 1, name: label + ' \u00b7 ' + key.sourceId, exact: true }).waitFor();
    return result;
  }
  function completedEmpty(data) {
    assert.equal(data.paths.length, 0);
    assert.equal(data.truncated, false);
    assert.equal(data.nextCursor ?? null, null);
  }

  stage('B9.1 verify the completed empty BusinessOrder result');
  const coreResult = (await open(core, 'BusinessOrder')).body;
  completedEmpty(coreResult);
  assert.ok(!coreResult.gaps.some(gap => gap.sourceId === core.sourceId));
  await empty.waitFor();
  assert.equal(await coverage.count(), 0);
  assert.equal(await rows.count(), 0);
  pass('B9.1', 'A completed empty neighbourhood shows the directional empty message',
    'BusinessOrder showed No visible connections in this direction');

  stage('B9.2 verify local and elsewhere coverage gaps for Incoming');
  const baseline = await open(order, 'Order');
  const { body: incoming } = await traverse(() => page.getByLabel('Direction', { exact: true }).selectOption('incoming'), order, 'incoming');
  completedEmpty(incoming);
  const keys = [order, ...incoming.entities.map(entity => entity.key)];
  const gaps = [...new Map(incoming.gaps.map(gap => [JSON.stringify(gap), gap])).values()];
  const relevant = gaps.filter(gap => keys.some(key => key.sourceId === gap.sourceId && (!gap.nodeIri || gap.nodeIri === key.identifier)));
  const elsewhere = gaps.length - relevant.length;
  assert.ok(relevant.length > 0 && elsewhere > 0);
  await coverage.waitFor();
  const summary = page.locator('summary').filter({ hasText: /^Coverage gaps \(/ });
  assert.equal(await summary.innerText(), 'Coverage gaps (' + relevant.length + ')');
  await summary.click();
  const listed = summary.locator('..').locator('li');
  await listed.first().waitFor();
  const normal = text => text.replace(/\s+/g, ' ').trim();
  assert.deepEqual((await listed.allTextContents()).map(normal), relevant.map(gap =>
    normal(gap.sourceId + ' ' + (gap.nodeIri ?? '') + ' \u00b7 ' + gap.code + ' \u00b7 ' + gap.reason)));
  await page.getByText(elsewhere + ' coverage gaps elsewhere in the division.', { exact: false }).waitFor();
  assert.equal(await empty.count(), 0);
  assert.equal(await rows.count(), 0);
  pass('B9.2', 'An empty incoming result shows relevant gaps and a separate count for elsewhere',
    relevant.length + ' relevant gap details, ' + elsewhere + ' elsewhere, no isolation message');

  async function nextControlled(body) {
    await page.route(routePattern, async route => {
      try { await route.fulfill({ status: 200, json: body }); }
      catch { await route.abort().catch(() => {}); }
    }, { times: 1 });
    return (await open(order, 'Order')).body;
  }

  stage('B9.3a show the empty continuation prompt');
  const cursor = 'b9-browser-only-continuation';
  const continuation = await nextControlled({ ...baseline.body, paths: [], truncated: true, nextCursor: cursor });
  assert.equal(continuation.nextCursor, cursor);
  await exact('Check more results for visible connections.').waitFor();
  await page.locator('[data-graph-load-more]:enabled').waitFor();
  assert.equal(await rows.count(), 0);
  pass('B9.3a', 'An empty page with a continuation offers Load more',
    'Check more results appeared and Load more was enabled after a browser-only response');

  stage('B9.3b explain truncation without a cursor');
  const { nextCursor, ...withoutCursor } = baseline.body;
  const partial = await nextControlled({ ...withoutCursor, truncated: true });
  assert.equal(partial.nextCursor ?? null, null);
  await incomplete.waitFor();
  assert.equal(await rows.count(), partial.paths.length);
  assert.equal(await more.count(), 0);
  pass('B9.3b', 'A truncated page without a cursor explains the limit and preserves rows',
    partial.paths.length + ' rows remained, incomplete-results message appeared and Load more was absent');

  stage('B9.3c compare live default with a browser-only unavailable endpoint fixture');
  const defaults = await open(order, 'Order');
  const available = defaults.body.paths.flatMap(path => path.steps);
  assert.ok(available.length > 0);
  assert.ok(available.every(step => step.endpointUnavailable !== true));
  const marker = 'c8f05fdf-5fa2-48fc-a32c-33857c01f5c8';
  const sample = { ...available[0], key: null, runtimeId: marker,
    label: 'B9 controlled unavailable endpoint', endpointUnavailable: true };
  await page.route(routePattern, async route => {
    try {
      const query = route.request().postDataJSON();
      if (query.includeUnavailable !== true) { await route.abort(); return; }
      await route.fulfill({ status: 200, json: { ...defaults.body, paths: [{ steps: [sample] }, ...defaults.body.paths] } });
    } catch { await route.abort().catch(() => {}); }
  }, { times: 1 });
  const comparison = await page.evaluate(async body => {
    const response = await fetch('/api/traverse', {
      method: 'POST', headers: { 'content-type': 'application/json' }, body: JSON.stringify(body)
    });
    return { status: response.status, page: await response.json() };
  }, { ...defaults.query, includeUnavailable: true });
  assert.equal(comparison.status, 200);
  assert.ok(comparison.page.paths.some(path => path.steps.some(step => step.runtimeId === marker && step.endpointUnavailable)));
  assert.equal(await page.locator('[data-graph-edge="r:' + marker + '"]').count(), 0);
  pass('B9.3c', 'Default graph omits unavailable endpoints',
    'Live graph requested available endpoints only and showed none unavailable. Browser-only opt-in comparison contained the control record.');
}

export async function b10({ origin, snapshot, run, lateRelationship, login, track, stage, pass }) {
  const key = source => ({ sourceId: 'fixture/' + source, kind: 'concept', identifier: 'https://example.test/' + source + '/ontology#Order' });
  const ecommerce = key('ecommerce'), warehouse = key('warehouse');
  const url = focus => origin + '/graph/' + Buffer.from(JSON.stringify(focus)).toString('base64url');
  const note = 'B10 temporary cursor probe ' + run;
  const staleMessage = 'These graph results are out of date. Reload to check for changes.';
  const temporaryError = 'B10 browser-only continuation failure.';
  const button = (scope, name) => scope.getByRole('button', { name, exact: true }).first();
  let reader, manager;
  const details = page => page.getByRole('region', { name: 'Graph details', exact: true });
  const direction = page => page.getByLabel('Direction', { exact: true });
  const heading = (page, key) => page.getByRole('heading', { name: 'Order \u00b7 ' + key.sourceId, exact: true, level: 1 });
  const ids = page => page.locator('[data-graph-edge]').evaluateAll(rows => rows.map(row => row.dataset.graphEdge));
  const watch = (page, path, predicate = () => true) => page.waitForResponse(r => {
    const req = r.request();
    return req.method() === 'POST' && new URL(r.url()).pathname === path && predicate(req.postDataJSON());
  });
  async function read(page, action, focus, sense, cursor = null, status = 200) {
    const [reply] = await Promise.all([watch(page, '/api/traverse', q =>
      q.start.identifier === focus.identifier && q.direction === sense && (q.cursor ?? null) === cursor), action()]);
    assert.equal(reply.status(), status);
    const query = reply.request().postDataJSON();
    assert.equal(query.start.sourceId, focus.sourceId);
    if (status !== 200) return { reply, query };
    const body = await reply.json();
    assert.equal(body.snapshotId, snapshot);
    await heading(page, focus).waitFor();
    return { reply, query, body };
  }

  async function openReader() {
    await read(reader, () => reader.goto(url(ecommerce)), ecommerce, 'both');
    const result = await read(reader, () => direction(reader).selectOption('outgoing'), ecommerce, 'outgoing');
    assert.ok(result.body.nextCursor);
    await reader.locator('[data-graph-load-more]:enabled').waitFor();
    const shown = await ids(reader);
    assert.equal(shown.length, 100);
    return { ...result, shown };
  }
  const late = readerPage => readerPage.locator('[data-graph-edge="r:' + lateRelationship.id + '"]');
  stage('sign in as reader and manager');
  [reader, manager] = await Promise.all([
    login('reader'),
    login('manager')
  ]);

  stage('B10.1 capture a reader continuation then save a manager note');
  const before = await openReader();
  await read(manager, () => manager.goto(url(warehouse)), warehouse, 'both');
  await button(manager, 'Inspect focus').click();
  await button(manager, 'Add note').click();
  await manager.getByLabel('Annotation text').fill(note);
  const [saved] = await Promise.all([
    watch(manager, '/api/contributions/annotations'), button(manager, 'Add annotation').click()
  ]);
  assert.equal(saved.status(), 201);
  const created = await saved.json();
  await track(created);
  await button(manager, 'View saved note').waitFor();
  pass('B10.1', 'Manager saves while reader has an open continuation', 'HTTP 201, temporary note ' + created.id);

  stage('B10.2 exercise the actual stale cursor and Reload');
  const stale = await read(reader, () => button(reader, 'Load more').click(), ecommerce, 'outgoing', before.body.nextCursor, 409);
  let code = await stale.reply.json().then(problem => problem.code, () => null);
  if (!code) {
    const authorization = await stale.reply.request().headerValue('authorization');
    const result = await reader.evaluate(async ({ authorization, query }) => {
      const reply = await fetch('/api/traverse', {
        method: 'POST', headers: { authorization, 'content-type': 'application/json' }, body: JSON.stringify(query)
      });
      return { status: reply.status, code: (await reply.json()).code };
    }, { authorization, query: stale.query });
    assert.equal(result.status, 409);
    code = result.code;
  }
  assert.equal(code, 'staleCursor');
  await reader.getByText(staleMessage, { exact: true }).waitFor();
  assert.deepEqual(await ids(reader), before.shown);
  assert.equal(await button(reader, 'Load more').count(), 0);
  const reloaded = await read(reader, () => button(reader, 'Reload').click(), ecommerce, 'outgoing');
  assert.ok(reloaded.body.nextCursor && reloaded.body.nextCursor !== before.body.nextCursor);
  await reader.locator('[data-graph-load-more]:enabled').waitFor();
  assert.equal(await reader.getByText(staleMessage, { exact: true }).count(), 0);
  assert.equal(await direction(reader).inputValue(), 'outgoing');
  assert.deepEqual(await ids(reader), before.shown);
  pass('B10.2', 'Mark old rows out of date, then reload page one at the same focus and direction',
    'Live 409 staleCursor preserved 100 rows. Reload fetched Ecommerce Order, Outgoing, with a new cursor.');

  stage('B10.3 fail one continuation in the browser and retry it live');
  const fresh = await openReader();
  assert.equal(await late(reader).count(), 0);
  await reader.route('**/api/traverse', async route => {
    try {
      await route.fulfill({ status: 503, contentType: 'application/problem+json',
        json: { status: 503, code: 'unavailable', title: temporaryError } });
    } catch { await route.abort().catch(() => {}); }
  }, { times: 1 });
  const failed = await read(reader, () => button(reader, 'Load more').click(), ecommerce, 'outgoing', fresh.body.nextCursor, 503);
  await reader.getByRole('alert').filter({ hasText: temporaryError }).waitFor();
  assert.deepEqual(await ids(reader), fresh.shown);
  const retry = await read(reader, () => button(reader, 'Retry').click(), ecommerce, 'outgoing', fresh.body.nextCursor);
  assert.ok(JSON.stringify(retry.query) === JSON.stringify(failed.query), 'Retry changed the query');
  await late(reader).waitFor();
  await button(reader, 'Load more').waitFor({ state: 'detached' });
  const after = await ids(reader);
  assert.equal(after.length, 100 + retry.body.paths.length);
  assert.equal(new Set(after).size, after.length);
  assert.ok(fresh.shown.every(id => after.includes(id)));

  stage('B10.3 remove the temporary note and verify it is absent');
  await button(manager, 'View saved note').click();
  const card = details(manager).locator('article').filter({ has: manager.getByText(note, { exact: true }) });
  await button(card, 'Remove note').click();
  const [deletion] = await Promise.all([
    watch(manager, '/api/contributions/' + created.id + '/remove'), button(manager, 'Remove contribution').click()
  ]);
  assert.equal(deletion.status(), 200);
  await track(await deletion.json(), true);
  await read(reader, () => reader.goto(url(warehouse)), warehouse, 'both');
  await button(reader, 'Inspect focus').click();
  await details(reader).getByRole('heading', { name: 'Annotations', exact: true }).waitFor();
  assert.equal(await details(reader).getByText(note, { exact: true }).count(), 0);
  pass('B10.3', 'Keep early rows after a temporary failure and retry the same cursor',
    `Browser-only 503 kept 100 rows. Live retry reused the query and showed ${after.length} rows. Temporary note removed.`);
}
