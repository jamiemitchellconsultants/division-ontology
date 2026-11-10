import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import YAML from 'yaml';

const flag = /DIVISION_SOURCE_APP_CLIENT_ID && 'enabled' \|\| 'disabled'/;
const readToken = (step) => {
  assert.equal(step.with['permission-contents'], 'read');
  assert.equal(step.with.repositories, undefined);
  assert.match(step.with['client-id'], /vars\.DIVISION_SOURCE_APP_CLIENT_ID \|\| vars\.DIVISION_SYNC_APP_CLIENT_ID/);
  assert.match(step.with['private-key'], /secrets\.DIVISION_SOURCE_APP_PRIVATE_KEY \|\| secrets\.DIVISION_SYNC_APP_PRIVATE_KEY/);
};
const workflow = (name) => YAML.parse(readFileSync(join(import.meta.dirname, '../.github/workflows', name), 'utf8'));
test('scheduled sync is manually runnable and restricts PR updates to generated outputs', () => {
  const sync = workflow('sync-catalog.yml');
  assert.equal(sync.on.schedule[0].cron, '0 */2 * * *');
  assert.ok(sync.on.workflow_dispatch);
  const steps = Object.values(sync.jobs)[0].steps;
  const create = steps.find((step) => step.uses?.startsWith('peter-evans/create-pull-request@'));
  assert.equal(create.with.branch, 'automation/catalog-refresh');
  assert.equal(create.with['add-paths'], 'dist/');
  const appTokens = steps.filter((step) => step.uses?.startsWith('actions/create-github-app-token@'));
  assert.equal(appTokens.length, 2);
  readToken(appTokens[0]);
  assert.match(steps.find((step) => step.id === 'build').env.DIVISION_SOURCE_APP, flag);
  assert.match(appTokens[1].with['client-id'], /DIVISION_SYNC_APP_CLIENT_ID/);
  assert.equal(appTokens[1].with.repositories, 'division-ontology');
  assert.ok(!JSON.stringify(steps).includes('git config credential'));
});

test('merge gate runs trusted base code and exposes source credentials only to generated refreshes', () => {
  const policy = workflow('merge-policy.yml');
  assert.ok(policy.on.pull_request_target);
  assert.equal(policy.on.pull_request_review, undefined);
  assert.deepEqual(policy.on.pull_request_target.branches, ['main']);
  assert.ok(policy.on.pull_request_target.types.includes('edited'));
  const gate = policy.jobs['merge-policy'];
  assert.ok(gate);
  const steps = gate.steps;
  assert.equal(steps[0].with.ref, '${{ github.event.repository.default_branch }}');
  assert.equal(steps[0].with['persist-credentials'], false);
  const candidate = steps.find((step) => step.name === 'Checkout candidate as data');
  assert.equal(candidate.with['persist-credentials'], false);
  assert.match(candidate.if, /generated_only/);
  const token = steps.find((step) => step.id === 'source-token');
  assert.match(token.if, /generated_only/);
  readToken(token);
  assert.match(steps.find((step) => step.name === 'Evaluate trusted policy').env.DIVISION_SOURCE_APP, flag);
  assert.ok(!steps.some((step) => step.run?.includes('_candidate/src/')));
});

test('trusted default-branch controller polls review state and retriggers policy via PR metadata', () => {
  const poller = workflow('review-recheck.yml');
  assert.ok(poller.on.schedule);
  assert.ok(poller.on.workflow_dispatch);
  const steps = poller.jobs.recheck.steps;
  assert.equal(steps[0].with.ref, '${{ github.event.repository.default_branch }}');
  assert.ok(steps.some((step) => step.uses?.startsWith('actions/create-github-app-token@')));
  assert.ok(steps.some((step) => step.run?.includes('src/review-recheck.mjs')));
});
