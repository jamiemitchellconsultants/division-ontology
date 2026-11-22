import assert from 'node:assert/strict';

const button = (scope, name) => scope.getByRole('button', { name, exact: true }).first();
const panel = page => page.getByRole('region', { name: 'Graph details', exact: true });
const exact = (scope, text) => scope.getByText(text, { exact: true });

export async function b7({ origin, snapshot, run, login, track, stage, pass }) {
  const system = { sourceId: 'fixture/ecommerce', kind: 'node', identifier: 'https://example.test/ecommerce/graph#system' };
  const external = { ...system, identifier: 'https://example.test/ecommerce/graph#external' };
  const graph = origin + '/graph/' + Buffer.from(JSON.stringify(system)).toString('base64url');
  const explanation = 'B7 temporary parallel relationship ' + run;
  let reader, manager, published, created;
  const row = (page, id) => page.locator('[data-graph-edge="' + id + '"]');
  const response = (page, path) => page.waitForResponse(reply =>
    reply.request().method() === 'POST' && new URL(reply.url()).pathname === path);
  async function open(page) {
    const [reply] = await Promise.all([response(page, '/api/traverse'), page.goto(graph)]);
    assert.equal(reply.status(), 200);
    const view = await reply.json();
    assert.equal(view.snapshotId, snapshot);
    await page.getByRole('heading', { name: 'Ecommerce \u00b7 fixture/ecommerce', exact: true, level: 1 }).waitFor();
    return view.paths.map(path => path.steps[0]);
  }

  async function point(page, id) {
    const host = page.locator('[data-graph-diagram]');
    await host.scrollIntoViewIfNeeded();
    const handle = await page.waitForFunction(async id => {
      const element = document.querySelector('[data-graph-diagram]');
      if (!element) return false;
      const module = await import(new URL(element.dataset.graphModule, document.baseURI));
      const position = module.diagramFor(element)?.renderedPosition(id);
      return position && { x: position.x + element.clientLeft, y: position.y + element.clientTop };
    }, id);
    const current = await handle.jsonValue();
    await handle.dispose();
    const box = await host.boundingBox();
    return { x: box.x + current.x, y: box.y + current.y };
  }

  async function publishedDetails(page) {
    const details = panel(page);
    const citation = details.locator(':scope > .origin').first();
    await details.getByText(/^Published relationship/).waitFor();
    await exact(citation, published.provenance.sourceCommit).waitFor();
    await exact(citation, published.provenance.evidence[0].path).waitFor();
    assert.equal(await details.getByText(explanation, { exact: false }).count(), 0);
  }

  async function runtimeDetails(page, subject) {
    const details = panel(page);
    await details.getByText(/^Workspace relationship/).waitFor();
    await exact(details, created.id).waitFor();
    await details.getByText(explanation, { exact: false }).waitFor();
    const author = await details.getByText(/^Author /).innerText();
    assert.ok(subject && author.includes(subject));
    const history = details.getByRole('link', { name: 'Relationship history', exact: true });
    assert.equal(await history.getAttribute('href'), 'history/' + created.id);
    return history;
  }
  stage('sign in as reader and manager and confirm the snapshot');
  [reader, manager] = await Promise.all([
    login('reader'),
    login('manager')
  ]);

  stage('B7.1 save a temporary relationship using the published label and endpoints');
  published = (await open(reader)).find(step =>
    step.key?.kind === 'assertion' && step.from.identifier === system.identifier && step.to.identifier === external.identifier);
  assert.ok(published?.provenance?.evidence?.length);
  const publishedId = 'p:' + Buffer.from(JSON.stringify({
    sourceId: published.key.sourceId, kind: published.key.kind, identifier: published.key.identifier
  })).toString('base64url');
  await row(reader, publishedId).waitFor();
  await open(manager);
  await button(manager, 'Inspect focus').click();
  await button(manager, 'Create relationship').click();
  const choice = key => JSON.stringify([key.sourceId, key.kind, key.identifier]);
  await manager.getByLabel('From entity').selectOption(choice(system));
  await manager.getByLabel('To entity').selectOption(choice(external));
  await manager.getByLabel('Custom label').fill(published.label);
  await manager.getByLabel('Explanation').fill(explanation);
  const [saved] = await Promise.all([
    response(manager, '/api/contributions/relationships'), button(manager, 'Add relationship').click()
  ]);
  assert.equal(saved.status(), 201);
  created = await saved.json();
  await track(created);
  const runtimeId = 'r:' + created.id;
  await manager.locator('[data-graph-save-status]').getByText('Change saved.', { exact: true }).waitFor();
  pass('B7.1', 'Save matching label and endpoints with a distinct explanation',
    'HTTP 201 and Change saved, temporary relationship ' + created.id);

  stage('B7.2 inspect both list rows and both curves');
  const live = (await open(reader)).find(step => step.runtimeId === created.id);
  assert.ok(live);
  assert.deepEqual(live.from, published.from);
  assert.deepEqual(live.to, published.to);
  assert.equal(live.label, published.label);
  const author = live.contribution?.author?.subject;
  await row(reader, publishedId).locator('[data-graph-inspect-edge]').click();
  await publishedDetails(reader);
  await row(reader, runtimeId).locator('[data-graph-inspect-edge]').click();
  await runtimeDetails(reader, author);

  const a = await point(reader, publishedId), b = await point(reader, runtimeId);
  assert.ok(Math.hypot(a.x - b.x, a.y - b.y) > 2);
  await reader.mouse.click(a.x, a.y);
  await publishedDetails(reader);
  const latest = await point(reader, runtimeId);
  await reader.mouse.click(latest.x, latest.y);
  const history = await runtimeDetails(reader, author);
  await history.click();
  await reader.getByRole('heading', { name: 'relationshipCreated \u00b7 Revision 1', exact: true }).waitFor();
  pass('B7.2', 'Both rows and curves open their own details and the workspace history is readable',
    'Four distinct inspections showed pinned published evidence or workspace explanation and author. Creation history opened.');

  stage('B7.3 remove the temporary relationship and refresh');
  await button(manager, 'View saved relationship').click();
  await runtimeDetails(manager, author);
  await button(manager, 'Remove relationship').click();
  const [removal] = await Promise.all([
    response(manager, '/api/contributions/' + created.id + '/remove'), button(manager, 'Remove contribution').click()
  ]);
  assert.equal(removal.status(), 200);
  await track(await removal.json(), true);
  const refreshed = await open(reader);
  assert.ok(refreshed.some(step => step.key?.identifier === published.key.identifier));
  assert.ok(!refreshed.some(step => step.runtimeId === created.id));
  await row(reader, publishedId).waitFor();
  assert.equal(await row(reader, runtimeId).count(), 0);
  pass('B7.3', 'Remove only the temporary relationship and retain the published handoff',
    'HTTP 200. Fresh reader view showed the published handoff and no temporary relationship.');
}

export async function b11({ origin, snapshot, run, login, track, stage, pass }) {
  const focus = { sourceId: 'fixture/ecommerce', kind: 'concept', identifier: 'https://example.test/ecommerce/ontology#Order' };
  const graph = origin + '/graph/' + Buffer.from(JSON.stringify(focus)).toString('base64url');
  const label = 'Order \u00b7 fixture/ecommerce';
  const code = run;
  const note = 'B11 temporary role test ' + code;
  const draft = 'B11 denied draft test ' + code;
  const apiPath = '/api/contributions/annotations';
  let reader, manager;
  const editor = page => panel(page).getByRole('region', { name: 'Contribution editor', exact: true });
  const card = (page, text) => panel(page).locator('article').filter({ has: page.getByText(text, { exact: true }) });
  const watch = (page, path) => page.waitForResponse(response =>
    response.request().method() === 'POST' && new URL(response.url()).pathname === path);
  async function open(page) {
    const [reply] = await Promise.all([watch(page, '/api/traverse'), page.goto(graph)]);
    assert.equal(reply.status(), 200);
    const view = await reply.json();
    assert.equal(view.snapshotId, snapshot);
    await page.getByRole('heading', { level: 1, name: label, exact: true }).waitFor();
    return { reply, paths: view.paths };
  }
  async function inspectFocus(page) {
    await button(page, 'Inspect focus').click();
    await panel(page).getByRole('heading', { name: label, exact: true }).waitFor();
    await panel(page).getByRole('heading', { name: 'Annotations', exact: true }).waitFor();
  }
  async function openHistory(page, link, id, event) {
    assert.equal(await link.getAttribute('href'), 'history/' + id);
    await link.click();
    await page.getByRole('heading', { name: 'Contribution history', exact: true }).waitFor();
    await page.getByRole('heading', { name: event + ' \u00b7 Revision 1', exact: true }).waitFor();
  }
  stage('sign in separately as reader and manager');
  [reader, manager] = await Promise.all([
    login('reader'),
    login('manager')
  ]);
  stage('create the temporary note and identify one seeded relationship');
  const initial = await open(manager);
  const edge = initial.paths.map(path => path.steps[0]).find(step => step.label.startsWith('paging-acceptance-' + run + '-'));
  assert.ok(edge?.runtimeId);
  await inspectFocus(manager);
  await button(manager, 'Add note').click();
  await editor(manager).getByLabel('Annotation text').fill(note);
  const [saved] = await Promise.all([watch(manager, apiPath), button(manager, 'Add annotation').click()]);
  assert.equal(saved.status(), 201);
  const record = await saved.json();
  await track(record);
  await button(manager, 'View saved note').waitFor();
  stage('B11.1 compare the reader and manager screens and histories');
  for (const [page, canManage] of [[reader, false], [manager, true]]) {
    await open(page);
    await inspectFocus(page);
    const thisNote = card(page, note);
    await thisNote.waitFor();
    for (const [scope, name] of [[panel(page), 'Add note'], [panel(page), 'Create relationship'],
      [thisNote, 'Edit note'], [thisNote, 'Remove note']]) {
      assert.equal(await button(scope, name).count(), canManage ? 1 : 0);
    }
    if (!canManage) assert.equal(await panel(page).locator('[data-graph-editor]').count(), 0);
    await openHistory(page, thisNote.getByRole('link', { name: 'Annotation history', exact: true }),
      record.id, 'annotationCreated');
    await open(page);
    await page.locator('[data-graph-inspect-edge="r:' + edge.runtimeId + '"]').click();
    await panel(page).getByText(/^Workspace relationship/).waitFor();
    await panel(page).getByText(edge.runtimeId, { exact: true }).waitFor();
    for (const name of ['Edit relationship', 'Remove relationship']) {
      assert.equal(await button(panel(page), name).count(), canManage ? 1 : 0);
    }
    await openHistory(page, panel(page).getByRole('link', { name: 'Relationship history', exact: true }),
      edge.runtimeId, 'relationshipCreated');
  }
  pass('B11.1', 'Both roles can inspect and read history, only manager sees write controls',
    'Both opened note and relationship histories. Only manager saw add, create, edit and remove.');

  stage('B11.2 deny one open editor save and verify the draft');
  const readerAuth = await (await open(reader)).reply.request().headerValue('authorization');
  const managerAuth = await initial.reply.request().headerValue('authorization');
  assert.ok(readerAuth && managerAuth && readerAuth !== managerAuth);
  await open(manager);
  await inspectFocus(manager);
  await button(manager, 'Add note').click();
  await editor(manager).getByLabel('Annotation category').selectOption('wayOfWorking');
  await editor(manager).getByLabel('Annotation text').fill(draft);

  let intercepted = false;
  await manager.route('**' + apiPath, async route => {
    try {
      const request = route.request();
      if (request.method() !== 'POST' || request.postDataJSON().text !== draft) { await route.abort(); return; }
      intercepted = true;
      await route.continue({ headers: { ...await request.allHeaders(), authorization: readerAuth } });
    } catch { await route.abort().catch(() => {}); }
  }, { times: 1 });
  const [denied] = await Promise.all([watch(manager, apiPath), button(manager, 'Add annotation').click()]);
  if (denied.status() === 201) {
    const unexpected = await denied.json();
    await track(unexpected);
  }
  assert.ok(intercepted);
  assert.equal(denied.status(), 403);
  await editor(manager).getByRole('alert').filter({ hasText: 'Your account does not have the role' }).waitFor();
  const checkDraft = async () => {
    assert.equal(await editor(manager).getByLabel('Annotation text').inputValue(), draft);
    assert.equal(await editor(manager).getByLabel('Annotation category').inputValue(), 'wayOfWorking');
  };
  await checkDraft();
  assert.equal(await manager.locator('[data-graph-save-status]').count(), 0);
  await button(editor(manager), 'Close editor').click();
  await button(manager, 'Add note').click();
  await checkDraft();
  await button(editor(manager), 'Close editor').click();

  stage('B11.2 remove the temporary note and verify the reader view');
  await button(card(manager, note), 'Remove note').click();
  const [removed] = await Promise.all([
    watch(manager, '/api/contributions/' + record.id + '/remove'),
    button(manager, 'Remove contribution').click()
  ]);
  assert.equal(removed.status(), 200);
  await track(await removed.json(), true);
  await open(reader);
  await inspectFocus(reader);
  assert.equal(await card(reader, note).count(), 0);
  assert.equal(await card(reader, draft).count(), 0);
  pass('B11.2', 'Refuse the save under reader permission and preserve the draft',
    'Live 403. Text and category survived reopening. Reader saw neither the denied draft nor the removed test note.');
}

export async function b12({ origin, snapshot, run, login, track, stage, pass }) {
  const key = source => ({ sourceId: 'fixture/' + source, kind: 'concept', identifier: 'https://example.test/' + source + '/ontology#Order' });
  const from = key('ecommerce'), to = key('warehouse');
  const graph = origin + '/graph/' + Buffer.from(JSON.stringify(from)).toString('base64url');
  const tag = run;
  const note = 'B12 Order note ' + tag + '. Confirm stock before dispatch.';
  const label = 'B12 order handoff ' + tag;
  const explanation = 'B12 handoff ' + tag + '. Dispatch follows confirmed stock.';
  const choice = key => JSON.stringify([key.sourceId, key.kind, key.identifier]);
  let manager, reader, token, releaseReload;
  const orderHeading = page => page.getByRole('heading', { name: 'Order \u00b7 fixture/ecommerce', exact: true, level: 1 });
  const direction = page => page.getByLabel('Direction', { exact: true });
  const more = page => page.locator('[data-graph-load-more]:enabled');
  const row = (page, id) => page.locator('[data-graph-edge="r:' + id + '"]');
  const status = page => page.locator('[data-graph-save-status]');
  const paths = view => view.paths.map(path => path.steps[0]);
  const watch = (page, path, predicate = () => true) => page.waitForResponse(r =>
    r.request().method() === 'POST' && new URL(r.url()).pathname === path && predicate(r.request().postDataJSON()));

  async function readGraph(page, action, predicate) {
    const [reply] = await Promise.all([watch(page, '/api/traverse', predicate), action()]);
    assert.equal(reply.status(), 200);
    assert.equal(reply.request().postDataJSON().start.identifier, from.identifier);
    const view = await reply.json();
    assert.equal(view.snapshotId, snapshot);
    await orderHeading(page).waitFor();
    return { reply, view };
  }

  async function open(page) {
    await readGraph(page, () => page.goto(graph), q => q.direction === 'both');
    const result = await readGraph(page, () => direction(page).selectOption('outgoing'), q => q.direction === 'outgoing' && !q.cursor);
    await more(page).waitFor();
    return result;
  }

  async function save(name, path) {
    const [reply] = await Promise.all([watch(manager, path), button(manager, name).click()]);
    assert.equal(reply.status(), 201);
    const record = await reply.json();
    await track(record);
    await status(manager).getByText('Change saved.', { exact: true }).waitFor();
    return { record, query: reply.request().postDataJSON() };
  }

  async function verifyNote(page, id) {
    const noteCard = panel(page).locator('article').filter({ has: page.getByText(note, { exact: true }) });
    await noteCard.getByText(id, { exact: true }).waitFor();
  }

  async function verifyRelationship(page, id) {
    const scope = panel(page);
    await scope.getByRole('heading', { name: label, exact: true }).waitFor();
    await scope.getByText(/^Workspace relationship.*Outgoing/).waitFor();
    await scope.getByText('Order \u00b7 fixture/ecommerce \u2192 Order \u00b7 fixture/warehouse', { exact: true }).waitFor();
    await scope.getByText(label + ' ' + explanation, { exact: true }).waitFor();
    await scope.getByText(id, { exact: true }).waitFor();
    assert.equal(await scope.getByRole('link', { name: 'Relationship history', exact: true }).getAttribute('href'), 'history/' + id);
  }
  try {
    stage('sign in separately as manager and reader');
    [manager, reader] = await Promise.all([
      login('manager'),
      login('reader')
    ]);

    stage('B12.1 save and open the note');
    token = await (await open(manager)).reply.request().headerValue('authorization');
    assert.ok(token);
    await button(manager, 'Inspect focus').click();
    await button(manager, 'Add note').click();
    await manager.getByLabel('Annotation text').fill(note);
    const savedNote = await save('Add annotation', '/api/contributions/annotations');
    assert.deepEqual(savedNote.query.target, from);
    assert.equal(savedNote.query.text, note);
    await button(status(manager), 'View saved note').click();
    await verifyNote(manager, savedNote.record.id);
    await more(manager).waitFor();
    pass('B12.1', 'Save and use View saved note', 'HTTP 201, Change saved, exact note opened: ' + savedNote.record.id);

    stage('B12.2 save the directed relationship and hold the reload');
    await button(panel(manager), 'Create relationship').click();
    const fromControl = manager.getByLabel('From entity');
    await fromControl.waitFor();
    assert.equal(await fromControl.inputValue(), choice(from));
    const toControl = manager.getByLabel('To entity');
    await toControl.selectOption(choice(to));
    assert.equal(await toControl.inputValue(), choice(to));
    await manager.getByLabel('Custom label').fill(label);
    await manager.getByLabel('Explanation').fill(explanation);

    const gate = new Promise(resolve => { releaseReload = resolve; });
    let held;
    const atReload = new Promise(resolve => { held = resolve; });
    await manager.route('**/api/traverse', async route => {
      try {
        const query = route.request().postDataJSON();
        assert.equal(query.direction, 'outgoing');
        assert.ok(!query.cursor);
        held();
        await gate;
        await route.continue();
      } catch { await route.abort().catch(() => {}); }
    }, { times: 1 });

    const savedEdge = await save('Add relationship', '/api/contributions/relationships');
    assert.deepEqual(savedEdge.query.from, from);
    assert.deepEqual(savedEdge.query.to, to);
    assert.equal(savedEdge.query.meaning.customLabel, label);
    assert.equal(savedEdge.query.meaning.explanation, explanation);
    let timer;
    try {
      await Promise.race([atReload, new Promise((_, reject) => {
        timer = setTimeout(() => reject(new Error('Reload was not held')), 15000);
      })]);
    } finally { clearTimeout(timer); }

    const id = savedEdge.record.id;
    assert.equal(await row(manager, id).count(), 0);
    await button(status(manager), 'View saved relationship').click();
    await verifyRelationship(manager, id);
    const reloaded = await readGraph(manager, () => releaseReload(), q => q.direction === 'outgoing' && !q.cursor);
    await more(manager).waitFor();
    await verifyRelationship(manager, id);
    const onFirst = paths(reloaded.view).some(step => step.runtimeId === id);
    assert.equal(await row(manager, id).count(), onFirst ? 1 : 0);
    pass('B12.2', 'Open the exact saved relationship while absent from the displayed page and keep it open after reload',
      'Ecommerce to Warehouse. Inspector stayed open. Live first page ' + (onFirst ? 'included' : 'omitted') + ' the record: ' + id);

    stage('B12.3 find the note and the relationship as a separate reader');
    const fresh = await open(reader);
    const readerToken = await fresh.reply.request().headerValue('authorization');
    assert.ok(readerToken && readerToken !== token);
    await button(reader, 'Inspect focus').click();
    await verifyNote(reader, savedNote.record.id);
    await button(panel(reader), 'Close details').click();
    let view = fresh.view, page = 1;
    while (!paths(view).some(step => step.runtimeId === id) && view.nextCursor && page < 4) {
      const cursor = view.nextCursor;
      view = (await readGraph(reader, () => button(reader, 'Load more').click(), q => q.cursor === cursor)).view;
      page++;
    }
    const found = paths(view).find(step => step.runtimeId === id);
    assert.equal(found?.origin, 'runtime');
    assert.deepEqual(found.from, from);
    assert.deepEqual(found.to, to);
    await row(reader, id).locator('[data-graph-inspect-edge]').click();
    await verifyRelationship(reader, id);
    pass('B12.3', 'A separate reader discovers the exact note, relationship and workspace origin',
      'Exact note and relationship verified, relationship on reader page ' + page);
    return { from, to, direction: 'outgoing',
      note: { id: savedNote.record.id, version: savedNote.record.version, text: note },
      relationship: { id, version: savedEdge.record.version, label, explanation } };
  } finally { releaseReload?.(); }
}

export async function b13({ origin, snapshot, savedRecords, login, track, stage, pass }) {
  const focusLabel = 'Order \u00b7 fixture/ecommerce';
  const heading = page => page.getByRole('heading', { name: focusLabel, exact: true, level: 1 });
  const sense = page => page.getByLabel('Direction', { exact: true });
  const more = page => page.locator('[data-graph-load-more]:enabled');
  const watch = (page, method, path, predicate = () => true) => page.waitForResponse(r => {
    const req = r.request();
    return req.method() === method && new URL(req.url()).pathname === path && predicate(req);
  });
  const watchGraph = (page, predicate) => watch(page, 'POST', '/api/traverse', req => predicate(req.postDataJSON()));
  const { from, to, note, relationship, direction } = savedRecords;
  assert.ok(note.text.startsWith('B12 Order note ') && relationship.label.startsWith('B12 order handoff '));
  assert.equal(direction, 'outgoing');
  const graph = origin + '/graph/' + Buffer.from(JSON.stringify(from)).toString('base64url');
  const text = 'B13 revised handoff. Reserve stock before dispatch. Record ' + relationship.id.slice(0, 8) + '.';
  const row = page => page.locator('[data-graph-edge="r:' + relationship.id + '"]');
  const noteCard = page => panel(page).locator('article').filter({ has: exact(page, note.text) });

  async function view(page, reply) {
    assert.equal(reply.status(), 200);
    assert.deepEqual(reply.request().postDataJSON().start, from);
    const data = await reply.json();
    assert.equal(data.snapshotId, snapshot);
    await heading(page).waitFor();
    return data;
  }

  async function open(page) {
    const [first] = await Promise.all([watchGraph(page, q => q.direction === 'both'), page.goto(graph)]);
    await view(page, first);
    const [outgoing] = await Promise.all([
      watchGraph(page, q => q.direction === direction && !q.cursor), sense(page).selectOption(direction)
    ]);
    const data = await view(page, outgoing);
    await more(page).waitFor();
    return data;
  }

  async function walk(page, first, stopAtTarget = false) {
    let result = first, count = 0, found = null, total = 0;
    while (true) {
      count++;
      total += result.paths.length;
      if (result.paths.some(path => path.steps[0]?.runtimeId === relationship.id)) {
        found = count;
        await row(page).waitFor();
        if (stopAtTarget) break;
      }
      if (!result.nextCursor) {
        await page.locator('[data-graph-load-more]').waitFor({ state: 'detached' });
        break;
      }
      assert.ok(count < 5, 'Unexpected fixture page count');
      const cursor = result.nextCursor;
      const [pageReply] = await Promise.all([
        watchGraph(page, q => q.cursor === cursor), button(page, 'Load more').click()
      ]);
      result = await view(page, pageReply);
    }
    return { count, found, total };
  }

  async function verifyRelationship(page, explanation) {
    await panel(page).getByRole('heading', { name: relationship.label, exact: true }).waitFor();
    await panel(page).getByText(/^Workspace relationship.*Outgoing/).waitFor();
    await exact(panel(page), relationship.id).waitFor();
    await exact(panel(page), 'Order \u00b7 fixture/ecommerce \u2192 Order \u00b7 fixture/warehouse').waitFor();
    await exact(panel(page), relationship.label + ' ' + explanation).waitFor();
  }

  async function mutate(page, method, id, version, submit) {
    const path = '/api/contributions/' + id + (method === 'POST' ? '/remove' : '');
    const pendingReload = watchGraph(page, () => true).catch(() => null);
    const [write] = await Promise.all([watch(page, method, path), button(page, submit).click()]);
    assert.equal(write.status(), 200);
    const record = await write.json();
    assert.equal(record.id, id);
    await track(record, method === 'POST');
    const payload = write.request().postDataJSON();
    assert.equal(payload.expectedVersion, version);
    assert.equal(record.version, version + 1);
    const reload = await pendingReload;
    assert.ok(reload, 'No post-write reload was observed');
    const fresh = await view(page, reload);
    const query = reload.request().postDataJSON();
    assert.equal(query.cursor ?? null, null);
    assert.equal(query.direction, direction);
    await exact(page.locator('[data-graph-save-status]'), 'Change saved.').waitFor();
    await more(page).waitFor();
    assert.equal(await sense(page).inputValue(), direction);
    assert.equal(fresh.paths.length, 100);
    assert.equal(await page.locator('[data-graph-edge]').count(), 100);
    return { record, payload };
  }

  async function history(page, href, id, expected) {
    assert.equal(href, 'history/' + id);
    const [reply] = await Promise.all([
      watch(page, 'GET', '/api/history', req => new URL(req.url()).searchParams.get('contributionId') === id),
      page.goto(origin + '/' + href)
    ]);
    assert.equal(reply.status(), 200);
    const data = await reply.json();
    assert.deepEqual(data.entries.map(entry => [entry.type, entry.revision]), expected);
    assert.ok(data.entries.every(entry => entry.contributionId === id && entry.snapshotId === snapshot));
    for (const [event, revision] of expected) {
      await page.getByRole('heading', { name: event + ' \u00b7 Revision ' + revision, exact: true }).waitFor();
    }
    return data.entries;
  }

  stage('sign in as reader and manager');
  const [manager, reader] = await Promise.all([
    login('manager'),
    login('reader')
  ]);

  stage('B13.1 load both pages and edit the recorded relationship');
  const before = await walk(manager, await open(manager));
  assert.ok(before.found && before.total > 100);
  await row(manager).locator('[data-graph-inspect-edge]').click();
  await verifyRelationship(manager, relationship.explanation);
  const edgeHistory = await panel(manager).getByRole('link', { name: 'Relationship history', exact: true }).getAttribute('href');
  await button(panel(manager), 'Edit relationship').click();
  const explanation = panel(manager).getByLabel('Explanation');
  assert.equal(await explanation.inputValue(), relationship.explanation);
  await explanation.fill(text);
  const revised = await mutate(manager, 'PUT', relationship.id, relationship.version, 'Save relationship');
  assert.deepEqual(revised.payload.from, from);
  assert.deepEqual(revised.payload.to, to);
  assert.equal(revised.payload.meaning.explanation, text);

  stage('B13.1 check the edited explanation in a fresh reader view');
  const edited = await walk(reader, await open(reader), true);
  assert.ok(edited.found);
  await row(reader).locator('[data-graph-inspect-edge]').click();
  await verifyRelationship(reader, text);
  pass('B13.1', 'Edit the relationship, refresh page one at the same focus and direction, and show the edit to the reader',
    'Revision 2, page one refreshed in Ecommerce Outgoing, reader verified the new explanation on page ' + edited.found);

  stage('B13.2 remove the recorded relationship and note');
  await button(manager.locator('[data-graph-save-status]'), 'View saved relationship').click();
  await verifyRelationship(manager, text);
  await button(panel(manager), 'Remove relationship').click();
  const removedEdge = await mutate(manager, 'POST', relationship.id, revised.record.version, 'Remove contribution');
  await button(manager, 'Inspect focus').click();
  const card = noteCard(manager);
  await exact(card, note.id).waitFor();
  const noteHistory = await card.getByRole('link', { name: 'Annotation history', exact: true }).getAttribute('href');
  await button(card, 'Remove note').click();
  const removedNote = await mutate(manager, 'POST', note.id, note.version, 'Remove contribution');

  stage('B13.2 verify fresh results and the preserved histories');
  const remaining = await walk(reader, await open(reader));
  assert.equal(remaining.found, null);
  assert.equal(remaining.total, before.total - 1);
  assert.equal(await row(reader).count(), 0);
  await button(reader, 'Inspect focus').click();
  await panel(reader).getByRole('heading', { name: 'Annotations', exact: true }).waitFor();
  assert.equal(await noteCard(reader).count(), 0);
  assert.equal(await exact(panel(reader), note.id).count(), 0);
  const expectedNote = [['annotationCreated', 1], ['annotationRemoved', removedNote.record.version]];
  const expectedEdge = [['relationshipCreated', 1], ['relationshipEdited', 2], ['relationshipRemoved', removedEdge.record.version]];
  const notes = await history(reader, noteHistory, note.id, expectedNote);
  const edges = await history(reader, edgeHistory, relationship.id, expectedEdge);
  assert.equal(notes[0].after.annotation.text, note.text);
  assert.equal(notes[1].after.removed, true);
  assert.equal(edges[1].before.relationship.meaning.explanation, relationship.explanation);
  assert.equal(edges[1].after.relationship.meaning.explanation, text);
  assert.equal(edges[2].before.relationship.meaning.explanation, text);
  assert.equal(edges[2].after.removed, true);
  pass('B13.2', 'Both records disappear from fresh results and all history events remain accessible',
    'Both absent, ' + remaining.total + ' other relationships remain. Reader verified both histories.');
  return { noteHistory: expectedNote, relationshipHistory: expectedEdge };
}

export async function b14({ origin, snapshot, run, login, enterCredentials, track, stage, pass }) {
  const key = source => ({ sourceId: 'fixture/' + source, kind: 'concept', identifier: 'https://example.test/' + source + '/ontology#Order' });
  const from = key('ecommerce'), to = key('warehouse');
  const graph = origin + '/graph/' + Buffer.from(JSON.stringify(from)).toString('base64url');
  const tag = run;
  const initial = 'B14 initial note ' + tag;
  const updated = 'B14 saved edit ' + tag;
  const staleDraft = 'B14 stale draft ' + tag;
  const deniedDraft = 'B14 denied draft ' + tag;
  const sessionDraft = 'B14 sign-in draft ' + tag;
  const edgeLabel = 'B14 unavailable endpoint ' + tag;
  const edgeDraft = 'B14 draft explanation ' + tag;
  const unavailable = { ...to, identifier: to.identifier + '-missing-' + tag };
  const choice = key => JSON.stringify([key.sourceId, key.kind, key.identifier]);
  let a, reader, readerAuth, note;
  const editor = page => panel(page).getByRole('region', { name: 'Contribution editor', exact: true });
  const textarea = page => editor(page).getByLabel('Annotation text');
  const direction = page => page.getByLabel('Direction', { exact: true });
  const noteCard = page => panel(page).locator('article').filter({ has: exact(page, note.id) });
  const savedStatus = page => page.locator('[data-graph-save-status]');
  const watch = (page, method, path) => page.waitForResponse(r =>
    r.request().method() === method && new URL(r.url()).pathname === path);

  async function open(page) {
    const [reply] = await Promise.all([watch(page, 'POST', '/api/traverse'), page.goto(graph + '?direction=outgoing')]);
    assert.equal(reply.status(), 200);
    assert.equal((await reply.json()).snapshotId, snapshot);
    await page.getByRole('heading', { name: 'Order \u00b7 fixture/ecommerce', exact: true, level: 1 }).waitFor();
    assert.equal(await direction(page).inputValue(), 'outgoing');
    return reply.request().headerValue('authorization');
  }
  async function inspect(page) {
    await button(page, 'Inspect focus').click();
    await panel(page).getByRole('heading', { name: 'Annotations', exact: true }).waitFor();
  }
  async function edit(page, expected) {
    await inspect(page);
    await exact(noteCard(page), expected).waitFor();
    await button(noteCard(page), 'Edit note').click();
    assert.equal(await textarea(page).inputValue(), expected);
  }
  async function save(page, method, path, name, status) {
    const [reply] = await Promise.all([watch(page, method, path), button(page, name).click()]);
    assert.equal(reply.status(), status);
    const record = await reply.json();
    await track(record, path.endsWith('/remove'));
    await exact(savedStatus(page), 'Change saved.').waitFor();
    await page.locator('[data-graph-load-more]:enabled').waitFor();
    return record;
  }
  async function journalHead() {
    return reader.evaluate(async authorization => {
      let cursor, head = 0;
      do {
        const params = new URLSearchParams({ limit: '100' });
        if (cursor) params.set('cursor', cursor);
        const reply = await fetch('/api/history?' + params, { headers: { authorization } });
        if (!reply.ok) throw new Error('History read rejected');
        const data = await reply.json();
        for (const entry of data.entries) head = Math.max(head, entry.position);
        cursor = data.nextCursor;
      } while (cursor);
      return head;
    }, readerAuth);
  }
  async function history() {
    return reader.evaluate(async ({ id, authorization }) => {
      const reply = await fetch('/api/history?contributionId=' + id, { headers: { authorization } });
      if (!reply.ok) throw new Error('Contribution history read rejected');
      return (await reply.json()).entries;
    }, { id: note.id, authorization: readerAuth });
  }
  async function reject(page, { path, method, name, expectedStatus, match, change }) {
    if (change) {
      await page.route('**' + path, async route => {
        try {
          const req = route.request();
          if (req.method() !== method || !match(req.postDataJSON())) { await route.abort(); return; }
          await route.continue(await change(req));
        } catch { await route.abort().catch(() => {}); }
      }, { times: 1 });
    }
    const [reply] = await Promise.all([watch(page, method, path), button(page, name).click()]);
    assert.ok(match(reply.request().postDataJSON()));
    if (reply.ok()) {
      const unexpected = await reply.json();
      if (unexpected.id) await track(unexpected);
    }
    assert.equal(reply.status(), expectedStatus);
  }
  stage('sign in to two manager contexts and a reader context');
  let b;
  [a, b, reader] = await Promise.all([
    login('manager'), login('manager'), login('reader')
  ]);
  assert.notEqual(a.context(), b.context());

  stage('B14.1 save a note and open revision 1 in two contexts');
  readerAuth = await open(reader);
  const managerAuth = await open(a);
  assert.ok(readerAuth && managerAuth && readerAuth !== managerAuth);
  await inspect(a);
  await button(panel(a), 'Add note').click();
  await textarea(a).fill(initial);
  note = await save(a, 'POST', '/api/contributions/annotations', 'Add annotation', 201);
  assert.equal(note.version, 1);
  const path = '/api/contributions/' + note.id;
  await Promise.all([open(a), open(b)]);
  await Promise.all([edit(a, initial), edit(b, initial)]);
  await textarea(a).fill(updated);
  await textarea(b).fill(staleDraft);
  const revised = await save(a, 'PUT', path, 'Save annotation', 200);
  assert.equal(revised.version, 2);
  const head = await journalHead();
  assert.equal(head, revised.position);

  stage('B14.1 show the conflict beside the draft and require review');
  await reject(b, { path, method: 'PUT', name: 'Save annotation', expectedStatus: 409,
    match: body => body.expectedVersion === 1 && body.text === staleDraft });
  const current = editor(b).getByRole('region', { name: 'Current server record', exact: true });
  await exact(current, updated).waitFor();
  await current.getByText(/The current revision is 2/).waitFor();
  assert.equal(await textarea(b).inputValue(), staleDraft);
  assert.equal(await button(editor(b), 'Save annotation').isDisabled(), true);
  await button(current, 'I reviewed the current revision').click();
  await editor(b).locator('button[type=submit]:enabled').waitFor();
  assert.equal(await textarea(b).inputValue(), staleDraft);
  assert.equal(await journalHead(), head);
  pass('B14.1', 'Reject the stale edit, show the current revision and require review',
    'Live 409 showed revision 2 beside the retained draft and blocked save until review');

  stage('B14.2a refuse an unavailable relationship endpoint');
  await open(a);
  await inspect(a);
  await button(panel(a), 'Create relationship').click();
  await editor(a).getByLabel('To entity').selectOption(choice(to));
  await editor(a).getByLabel('Custom label').fill(edgeLabel);
  await editor(a).getByLabel('Explanation').fill(edgeDraft);
  await reject(a, { path: '/api/contributions/relationships', method: 'POST', name: 'Add relationship', expectedStatus: 422,
    match: body => body.meaning?.customLabel === edgeLabel,
    change: async req => ({ postData: { ...req.postDataJSON(), to: unavailable } }) });
  await editor(a).getByRole('alert').filter({ hasText: 'Select an available published entity' }).waitFor();
  assert.equal(await editor(a).getByLabel('To entity').inputValue(), choice(to));
  assert.equal(await editor(a).getByLabel('Custom label').inputValue(), edgeLabel);
  assert.equal(await editor(a).getByLabel('Explanation').inputValue(), edgeDraft);
  assert.equal(await savedStatus(a).count(), 0);
  assert.equal(await journalHead(), head);

  stage('B14.2b refuse a save made with reader permissions');
  await open(a);
  await edit(a, updated);
  await textarea(a).fill(deniedDraft);
  await reject(a, { path, method: 'PUT', name: 'Save annotation', expectedStatus: 403,
    match: body => body.text === deniedDraft,
    change: async req => ({ headers: { ...await req.allHeaders(), authorization: readerAuth } }) });
  await editor(a).getByRole('alert').filter({ hasText: 'Your account does not have the role' }).waitFor();
  assert.equal(await textarea(a).inputValue(), deniedDraft);
  assert.equal(await savedStatus(a).count(), 0);
  assert.equal(await journalHead(), head);

  stage('B14.2c refuse a save without current authorization');
  await textarea(a).fill(sessionDraft);
  await reject(a, { path, method: 'PUT', name: 'Save annotation', expectedStatus: 401,
    match: body => body.text === sessionDraft,
    change: async req => ({ headers: { ...await req.allHeaders(), authorization: '' } }) });
  const again = button(editor(a), 'Sign in again');
  await again.waitFor();
  assert.equal(await textarea(a).inputValue(), sessionDraft);
  assert.equal(await savedStatus(a).count(), 0);
  assert.equal(await journalHead(), head);
  pass('B14.2', 'Unavailable endpoint, denied write and missing authorization preserve drafts and append no history',
    'Live 422, 403 and 401 with the expected messages. Drafts intact, journal position unchanged after each.');

  stage('B14.3 sign in again and resume the same editor');
  const pendingReturn = a.waitForURL(url => url.pathname === new URL(graph).pathname &&
    url.searchParams.get('direction') === 'outgoing' && url.searchParams.get('editor') === 'edit-note' &&
    url.searchParams.get('contributionId') === note.id, { timeout: 30000 });
  const loginForm = a.locator('#kc-login').waitFor().then(() => true, () => false);
  let authorize = 0;
  a.on('request', request => {
    if (/\/protocol\/openid-connect\/auth$/.test(new URL(request.url()).pathname)) authorize++;
  });
  await again.click();
  if (await Promise.race([pendingReturn.then(() => false), loginForm])) await enterCredentials(a, 'manager');
  await pendingReturn;
  await button(editor(a), 'Save annotation').waitFor();
  assert.equal(await textarea(a).inputValue(), sessionDraft);
  assert.equal(await direction(a).inputValue(), 'outgoing');
  assert.equal(await savedStatus(a).count(), 0);
  assert.ok(authorize > 0);
  assert.equal(await journalHead(), head);

  stage('B14.3 remove the temporary note and verify its history');
  await button(editor(a), 'Close editor').click();
  await button(noteCard(a), 'Remove note').click();
  const removed = await save(a, 'POST', path + '/remove', 'Remove contribution', 200);
  const events = await history();
  assert.deepEqual(events.map(entry => entry.type), ['annotationCreated', 'annotationEdited', 'annotationRemoved']);
  assert.equal(events.at(-1).after.removed, true);
  await open(reader);
  await inspect(reader);
  assert.equal(await noteCard(reader).count(), 0);
  pass('B14.3', 'Sign in again restores the same tab and draft, then cleanup removes the test note',
    'Same editor, Outgoing and exact draft restored. Note removed and history contained only create, accepted edit and remove.');
}
