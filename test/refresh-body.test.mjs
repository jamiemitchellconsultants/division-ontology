import test from 'node:test';
import assert from 'node:assert/strict';
import { renderRefreshBody } from '../src/refresh-body.mjs';

const catalog = { sources: [{ id: 'example/booking', commit: 'a'.repeat(40), conceptCount: 2 }, { id: 'example/rooms', commit: 'b'.repeat(40), conceptCount: 4 }] };
test('refresh PR body records every source, review reason and one fingerprint', () => {
  const body = renderRefreshBody({ catalog, mode: 'review', reasons: ['linked concept changed'], fingerprint: '123456abcdef' });
  assert.match(body, /example\/booking.*2/);
  assert.match(body, /example\/rooms.*4/);
  assert.match(body, /linked concept changed/);
  assert.equal(body.match(/AI-Fingerprint: sha256:[0-9a-f]{12}/g)?.length, 1);
});
test('automatic body states that generated content was verified', () => {
  const body = renderRefreshBody({ catalog, mode: 'automatic', reasons: [], fingerprint: '123456abcdef' });
  assert.match(body, /automatic/i);
  assert.match(body, /fresh build/i);
});

test('the refresh body lists import status when granted sources have it', () => {
  const body = renderRefreshBody({ catalog: { sources: [], importStatus: [
    { source: 'o/a', status: 'invalid', attemptedCommit: 'b'.repeat(40), publishedCommit: null, reason: { code: 'config-missing', detail: '.division-ontology.json' } }] },
    mode: 'automatic', reasons: [], fingerprint: '0'.repeat(12) });
  assert.match(body, /### Import status/);
  assert.match(body, /\| o\/a \| invalid \| — \| `config-missing: \.division-ontology\.json` \|/);
});

test('import status reasons cannot break the table or mention users (review minor)', () => {
  const body = renderRefreshBody({ catalog: { sources: [], importStatus: [
    { source: 'o/a', status: 'invalid', attemptedCommit: null, publishedCommit: null, reason: { code: 'config-invalid', detail: 'unknown key a|b @someone' } }] },
    mode: 'automatic', reasons: [], fingerprint: '0'.repeat(12) });
  const line = body.split('\n').find((l) => l.startsWith('| o/a |'));
  assert.match(line, /`config-invalid: unknown key a\\\|b @someone`/);
  assert.equal(line.split(/(?<!\\)\|/).length, 6);
});
