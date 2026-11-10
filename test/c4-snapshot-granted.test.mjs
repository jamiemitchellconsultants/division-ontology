import test from 'node:test';
import assert from 'node:assert/strict';
import { sourceInventory } from '../src/c4/snapshot.mjs';

const manifest = { sources: [{ id: 'o/reg', url: 'https://github.com/o/reg.git', ref: 'main', ontologyPath: 'x.ttl', namespace: 'https://x.test/#' }] };
const entry = (id, origin) => ({ id, commit: 'a'.repeat(40), namespace: 'https://x.test/', conceptCount: 1, ...(origin ? { origin } : {}) });

test('registered sources keep their manifest entry and granted sources get a derived GitHub URL', () => {
  const inventory = sourceInventory({ sources: [entry('o/reg', 'registered'), entry('o/grant', 'granted')] }, manifest);
  assert.equal(inventory.get('o/reg').url, 'https://github.com/o/reg.git');
  assert.deepEqual(inventory.get('o/grant'), { id: 'o/grant', url: 'https://github.com/o/grant' });
});
test('catalogs without origin read as registered', () => {
  assert.ok(sourceInventory({ sources: [entry('o/reg')] }, manifest).has('o/reg'));
});
test('mismatched inventories still fail closed', () => {
  assert.throws(() => sourceInventory({ sources: [] }, manifest), /mismatch/);
  assert.throws(() => sourceInventory({ sources: [entry('o/reg'), entry('o/other')] }, manifest), /mismatch/);
  assert.throws(() => sourceInventory({ sources: [entry('o/reg', 'granted')] }, manifest), /mismatch/);
  assert.throws(() => sourceInventory({ sources: [entry('o/reg'), entry('o/reg')] }, manifest), /mismatch/);
});
