import test from 'node:test';
import assert from 'node:assert/strict';
import { DataFactory } from 'n3';
import { renderOutputs } from '../src/output.mjs';

test('N-Quads ordering includes literal language when lexical forms match', async () => {
  const { namedNode, literal, quad } = DataFactory;
  const subject = namedNode('https://example.test/s');
  const predicate = namedNode('https://example.test/p');
  const graph = namedNode('https://example.test/g');
  const english = quad(subject, predicate, literal('same', 'en'), graph);
  const french = quad(subject, predicate, literal('same', 'fr'), graph);
  const catalog = { core: { version: '1', conceptCount: 0 }, sources: [], concepts: [], relations: [], rules: [], links: [] };
  const first = await renderOutputs(catalog, [english, french]);
  const second = await renderOutputs(catalog, [french, english]);
  assert.equal(first['combined.nq'], second['combined.nq']);
});
