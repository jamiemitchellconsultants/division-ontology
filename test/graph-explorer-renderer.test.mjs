import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { readFile } from 'node:fs/promises';
import test from 'node:test';

const web = new URL('../runtime/RuntimeKnowledge.Web/wwwroot/', import.meta.url);
const vendor = new URL('vendor/cytoscape/3.35.0/', web);

test('the browser package preserves the pinned upstream module and license', async () => {
  for (const [file, hash] of [
    ['cytoscape.esm.min.mjs', 'aa33b0103bbdb721aeb1506000cb6e056692400baba7264c552d000b09da01f2'],
    ['LICENSE', 'eb319c6e6f233607f71e8e2f450391751883cfc0eeb3ca7ef574c13d1d9c2203']
  ]) {
    assert.equal(createHash('sha256').update(await readFile(new URL(file, vendor))).digest('hex'), hash);
  }
});

test('append preserves positions and diagram actions use stable identities', async () => {
  assert.equal(typeof globalThis.document, 'undefined');
  const { placeNeighbours, bindGraphActions, diagramFor } = await import(new URL('graph-diagram.mjs', web));
  assert.equal(diagramFor({}), null);
  const { default: cytoscape } = await import(new URL('cytoscape.esm.min.mjs', vendor));
  const first = placeNeighbours(['n:a', 'n:b'], {});
  const appended = placeNeighbours(['n:a', 'n:b', 'n:c'], first);
  for (const id of ['n:a', 'n:b', 'n:c']) {
    assert.ok(Number.isFinite(appended[id].x) && Number.isFinite(appended[id].y));
  }
  assert.notDeepEqual(first['n:a'], first['n:b']);
  assert.deepEqual(appended['n:a'], first['n:a']);
  assert.deepEqual(appended['n:b'], first['n:b']);
  const cy = cytoscape({ headless: true, elements: [
    { data: { id: 'n:a' } }, { data: { id: 'n:b' } },
    { data: { id: 'p:assertion', source: 'n:a', target: 'n:b' } },
    { data: { id: 'r:contribution', source: 'n:a', target: 'n:b' } }
  ] });
  const actions = [];
  try {
    bindGraphActions(cy, {
      follow: id => actions.push(['follow', id]),
      inspect: id => actions.push(['inspect', id])
    });
    for (const id of ['n:b', 'p:assertion', 'r:contribution']) cy.getElementById(id).emit('tap');
    for (const id of ['n:b', 'p:assertion']) cy.getElementById(id).emit('dbltap');
    assert.deepEqual(actions, [
      ['inspect', 'n:b'], ['inspect', 'p:assertion'], ['inspect', 'r:contribution'], ['follow', 'n:b']
    ]);
  } finally {
    cy.destroy();
  }
});
