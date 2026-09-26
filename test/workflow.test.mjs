import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import YAML from 'yaml';

const workflow = (name) => YAML.parse(readFileSync(join(import.meta.dirname, '../.github/workflows', name), 'utf8'));
test('scheduled sync is manually runnable and restricts PR updates to generated outputs', () => {
  const sync = workflow('sync-catalog.yml');
  assert.equal(sync.on.schedule[0].cron, '*/30 * * * *');
  assert.ok(sync.on.workflow_dispatch);
  const steps = Object.values(sync.jobs)[0].steps;
  const create = steps.find((step) => step.uses?.startsWith('peter-evans/create-pull-request@'));
  assert.equal(create.with.branch, 'automation/catalog-refresh');
  assert.equal(create.with['add-paths'], 'dist/');
  const appTokens = steps.filter((step) => step.uses?.startsWith('actions/create-github-app-token@'));
  assert.equal(appTokens.length, 2);
  assert.equal(appTokens[0].with['permission-contents'], 'read');
  assert.equal(appTokens[1].with.repositories, 'division-ontology');
  assert.ok(!JSON.stringify(steps).includes('git config credential'));
});
