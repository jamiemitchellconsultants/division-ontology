import test from 'node:test';
import assert from 'node:assert/strict';
import { evaluateMergePolicy, validatePrTarget } from '../src/merge-policy.mjs';

const headSha = 'a'.repeat(40);
const approval = (state = 'APPROVED', commitId = headSha, user = 'jamiemitchellconsultants', id = 1) => ({ state, commit_id: commitId, user: { login: user }, id });
const evaluate = (changedPaths, mode, reviews = []) => evaluateMergePolicy({ changedPaths, classification: { mode, reasons: [] }, reviews, headSha, stewardLogins: ['jamiemitchellconsultants', 'james-mitchell-ba'] });

test('verified generated refresh needs no review', () => assert.equal(evaluate(['dist/catalog.json'], 'automatic').allowed, true));
test('linked drift waits for a current steward approval', () => {
  assert.equal(evaluate(['dist/catalog.json'], 'review').allowed, false);
  assert.equal(evaluate(['dist/catalog.json'], 'review', [approval()]).allowed, true);
});
test('other files require current steward approval', () => {
  assert.equal(evaluate(['sources.json'], 'review').allowed, false);
  assert.equal(evaluate(['sources.json'], 'review', [approval()]).allowed, true);
});
test('alternate steward approval satisfies the policy for the current head', () => {
  assert.equal(evaluate(['sources.json'], 'review', [approval('APPROVED', headSha, 'james-mitchell-ba')]).allowed, true);
});
test('stale and unrelated approvals do not satisfy policy', () => {
  assert.equal(evaluate(['sources.json'], 'review', [approval('APPROVED', 'b'.repeat(40))]).allowed, false);
  assert.equal(evaluate(['sources.json'], 'review', [approval('APPROVED', 'b'.repeat(40), 'james-mitchell-ba')]).allowed, false);
  assert.equal(evaluate(['sources.json'], 'review', [approval('APPROVED', headSha, 'someone-else')]).allowed, false);
});
test('later alternate steward changes requested supersedes an approval', () => assert.equal(evaluate(['sources.json'], 'review', [approval(), approval('CHANGES_REQUESTED', headSha, 'james-mitchell-ba', 2)]).allowed, false));
test('one steward cannot approve over another steward\'s outstanding changes request', () => {
  assert.equal(evaluate(['sources.json'], 'review', [approval('CHANGES_REQUESTED'), approval('APPROVED', headSha, 'james-mitchell-ba', 2)]).allowed, false);
});
test('mixed generated and authored files are never silently merged', () => assert.equal(evaluate(['sources.json', 'dist/catalog.json'], 'automatic', [approval()]).allowed, false));
test('no diff is not an automatic merge', () => assert.equal(evaluate([], 'none').allowed, false));
test('privileged policy rejects a PR targeting an unprotected branch or another repository', () => {
  const pr = { base: { ref: 'main', repo: { full_name: 'example/division-ontology' } } };
  assert.doesNotThrow(() => validatePrTarget(pr, 'example/division-ontology'));
  assert.throws(() => validatePrTarget({ base: { ...pr.base, ref: 'feature' } }, 'example/division-ontology'), /protected main/i);
  assert.throws(() => validatePrTarget({ base: { ...pr.base, repo: { full_name: 'example/other' } } }, 'example/division-ontology'), /repository/i);
});
