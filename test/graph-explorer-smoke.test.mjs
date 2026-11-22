import assert from 'node:assert/strict';
import test from 'node:test';
import * as smoke from '../deploy/workspace/verify-graph-explorer.mjs';

test('graph smoke retains the browser failure when the snapshot check also fails', async () => {
  const primary = new Error('Graph route failed');
  let reads = 0;
  const reader = {
    page: { async goto() { throw primary; } },
    async request() { return { snapshotId: ++reads === 1 ? 'initial' : 'changed' }; }
  };
  await assert.rejects(smoke.verifyGraphExplorer({ reader, origin: 'http://localhost', snapshot: 'initial' }), error => {
    assert.ok(error instanceof AggregateError);
    assert.equal(error.errors[0], primary);
    assert.match(error.errors[1].message, /preserves the active published snapshot/);
    return true;
  });
});

test('cleanup runs after an assertion failure and retains both failures in order', async () => {
  const primary = new Error('Assertion failed');
  const cleanup = new Error('Could not remove a temporary record');
  const checks = [];
  await assert.rejects(smoke.withFinalChecks(
    async () => { throw primary; },
    async () => { checks.push('cleanup'); throw cleanup; },
    async () => { checks.push('invariants'); }
  ), error => {
    assert.ok(error instanceof AggregateError);
    assert.deepEqual(error.errors, [primary, cleanup]);
    return true;
  });
  assert.deepEqual(checks, ['cleanup', 'invariants']);
  await assert.rejects(smoke.withFinalChecks(async () => { throw primary; }, async () => {}), error => error === primary);
});

test('graph smoke encodes evidence path segments like the workspace evidence links', () => {
  assert.equal(smoke.evidenceHref('https://github.com/fixture/ecommerce', 'commit', {
    path: "docs/order (v2)!'*.md", startLine: 2, endLine: 4
  }), 'https://github.com/fixture/ecommerce/blob/commit/docs/order%20%28v2%29%21%27%2A.md#L2-L4');
});
