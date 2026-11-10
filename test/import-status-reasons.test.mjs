import test from 'node:test';
import assert from 'node:assert/strict';
import { REASON_CODES, STATUSES, reason, safeDetail } from '../src/import-status.mjs';

test('status values and reason codes are the closed sets from ADR 0011', () => {
  assert.deepEqual(STATUSES, ['current', 'invalid', 'not configured', 'access removed']);
  assert.deepEqual(REASON_CODES, ['config-missing', 'config-invalid', 'ontology-unreadable', 'ontology-invalid-turtle',
    'concept-outside-namespace', 'no-concepts', 'namespace-conflict', 'access-removed']);
});
test('reason rejects an unknown code and keeps an ordinary detail', () => {
  assert.throws(() => reason('boom', 'x'), /unknown reason code boom/);
  assert.deepEqual(reason('config-missing', '.division-ontology.json'), { code: 'config-missing', detail: '.division-ontology.json' });
  assert.deepEqual(reason('no-concepts'), { code: 'no-concepts', detail: '' });
});
test('safeDetail redacts token shapes and URL credentials', () => {
  const token = `ghp_${'a1B2'.repeat(8)}`;
  for (const text of [`token ${token} leaked`, 'github_pat_' + 'Z9'.repeat(20), 'header x-access-token here', 'https://user:hunter2@github.com/o/r']) {
    const out = safeDetail(text);
    assert.match(out, /\[redacted\]/);
    assert.doesNotMatch(out, /ghp_|github_pat_|x-access-token|hunter2/);
  }
});
test('safeDetail strips control characters and bounds the length', () => {
  assert.equal(safeDetail('a\u0000b\nc'), 'a b c');
  const out = safeDetail('x'.repeat(500));
  assert.equal(out.length, 200);
  assert.ok(out.endsWith('…'));
});
