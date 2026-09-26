import test from 'node:test';
import assert from 'node:assert/strict';
import { updateReviewMarker } from '../src/review-recheck.mjs';

const body = 'A reviewed source proposal.\n\nAI-Fingerprint: sha256:123456abcdef\n';
const review = { id: 4, state: 'APPROVED', commit_id: 'a'.repeat(40), submitted_at: '2026-09-26T12:00:00Z' };
test('new approval changes PR metadata once and leaves the fingerprint footer intact', () => {
  const first = updateReviewMarker(body, [review]);
  assert.notEqual(first, body);
  assert.match(first, /<!-- division-policy-reviews:[0-9a-f]{12} -->/);
  assert.match(first, /AI-Fingerprint: sha256:123456abcdef\n$/);
  assert.equal(updateReviewMarker(first, [review]), first);
});
test('dismissal changes marker and causes trusted gate to rerun', () => {
  const approved = updateReviewMarker(body, [review]);
  const dismissed = updateReviewMarker(approved, [{ ...review, state: 'DISMISSED' }]);
  assert.notEqual(dismissed, approved);
});
