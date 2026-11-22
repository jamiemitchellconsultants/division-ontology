import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { readFile, writeFile } from 'node:fs/promises';
import { join } from 'node:path';
import { inputState } from '../verify-graph-explorer.mjs';

const sources = ['fixture/accounting', 'fixture/ecommerce', 'fixture/warehouse'];
const identifier = /^[a-f0-9]{8}-(?:[a-f0-9]{4}-){3}[a-f0-9]{12}$/i;
export const readJson = async path => JSON.parse((await readFile(path, 'utf8')).replace(/^\uFEFF/, ''));
export const saveJson = (path, data) => writeFile(path, JSON.stringify(data, null, 2) + '\n', { mode: 0o600 });
const order = source => ({ sourceId: 'fixture/' + source, kind: 'concept', identifier: 'https://example.test/' + source + '/ontology#Order' });
export const from = order('ecommerce');
const to = order('warehouse');
export const graphUrl = origin => origin + '/graph/' + Buffer.from(JSON.stringify(from)).toString('base64url') + '?direction=outgoing';

export async function captureInputs(fixtureRoot, snapshot) {
  const state = await readJson(join(fixtureRoot, 'fixture-state.json'));
  assert.match(state.directory, /^fixture(?:-[a-f0-9]{32})?$/);
  assert.equal(state.manifestId, snapshot, 'Retained fixture snapshot');
  const path = join(fixtureRoot, state.directory);
  const bytes = await readFile(join(path, 'bundle', 'manifests', snapshot + '.json'));
  assert.equal(createHash('sha256').update(bytes).digest('hex'), snapshot, 'Manifest digest');
  const manifest = JSON.parse(bytes);
  assert.deepEqual(manifest.sources.map(source => source.id).sort(), sources);
  const inputs = await inputState(join(path, 'checkouts', 'fixture.json'));
  assert.deepEqual(Object.keys(inputs).sort(), ['division-core', ...sources]);
  assert.equal(inputs['division-core'].revision, manifest.divisionCommit);
  for (const source of manifest.sources) assert.equal(inputs[source.id].revision, source.commit);
  const count = Object.values(inputs).flatMap(input => Object.keys(input.turtle)).length;
  assert.equal(count, 7, 'Tracked Turtle input count');
  return { snapshot, inputs };
}

export async function api(page, authorization, path, body) {
  return page.evaluate(async ({ authorization, path, body }) => {
    const response = await fetch(path, {
      method: body === undefined ? 'GET' : 'POST',
      headers: { authorization, 'content-type': 'application/json' },
      body: JSON.stringify(body),
      redirect: 'error'
    });
    const json = await response.json().catch(() => null);
    return { status: response.status, json };
  }, { authorization, path, body });
}

export async function graphSession(page, origin, snapshot) {
  const [reply] = await Promise.all([
    page.waitForResponse(response => response.request().method() === 'POST' &&
      new URL(response.url()).pathname === '/api/traverse'),
    page.goto(graphUrl(origin))
  ]);
  assert.equal(reply.status(), 200);
  assert.equal((await reply.json()).snapshotId, snapshot);
  const authorization = await reply.request().headerValue('authorization');
  assert.ok(authorization?.startsWith('Bearer '), 'Authenticated graph request');
  return { page, authorization };
}

export async function traverse({ page, authorization }, snapshot) {
  const results = [];
  let cursor;
  do {
    const reply = await api(page, authorization, '/api/traverse', {
      start: from, direction: 'outgoing', maxDepth: 1, includeUnavailable: false, cursor
    });
    assert.equal(reply.status, 200, 'Traverse status');
    assert.equal(reply.json.snapshotId, snapshot);
    cursor = reply.json.nextCursor;
    results.push(reply.json.paths.map(path => path.steps[0]));
    assert.ok(results.length <= 20, 'Traversal page limit');
  } while (cursor);
  return results;
}

export async function history({ page, authorization }, id) {
  assert.match(id, identifier);
  const reply = await api(page, authorization, '/api/history?contributionId=' + id);
  assert.equal(reply.status, 200, 'Contribution history status');
  return reply.json.entries;
}

export function fixtureLedger(directory) {
  const records = new Map();
  const path = join(directory, 'records.jsonl');
  async function track(record, removed = false) {
    assert.match(record?.id ?? '', identifier);
    assert.ok(Number.isSafeInteger(record.version) && record.version >= 1);
    const previous = records.get(record.id);
    assert.ok(previous || (record.version === 1 && !removed), 'Only records created by this run can be tracked');
    const current = { id: record.id, version: record.version, removed };
    records.set(record.id, current);
    await writeFile(path, JSON.stringify(current) + '\n', { flag: 'a', mode: 0o600 });
  }

  async function cleanup({ page, authorization }) {
    const failures = [];
    for (const record of [...records.values()].reverse()) {
      if (record.removed) continue;
      try {
        const reply = await api(page, authorization, '/api/contributions/' + record.id + '/remove',
          { expectedVersion: record.version });
        assert.equal(reply.status, 200, 'Fixture cleanup status');
        assert.equal(reply.json.id, record.id);
        assert.equal(reply.json.version, record.version + 1);
        await track(reply.json, true);
      } catch {
        failures.push(record.id);
      }
    }
    return failures;
  }
  return { records, track, cleanup };
}

export async function seed({ page, authorization }, run, track) {
  for (let i = 0; i < 105; i++) {
    const reply = await api(page, authorization, '/api/contributions/relationships', {
      from, to, meaning: { customLabel: 'paging-acceptance-' + run + '-' + i,
        explanation: 'Temporary Graph Explorer regression relationship ' + run }
    });
    assert.equal(reply.status, 201, 'Fixture seed status');
    await track(reply.json);
  }
}
