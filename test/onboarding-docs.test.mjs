import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

const text = (path) => readFileSync(path, 'utf8');
test('setup documents both Apps and their variables', () => {
  const doc = text('docs/automation-setup.md');
  for (const pattern of [/DIVISION_SOURCE_APP_CLIENT_ID/, /DIVISION_SOURCE_APP_PRIVATE_KEY/, /Metadata: read/, /\.division-ontology\.json/, /source-read App/]) assert.match(doc, pattern);
});
test('onboarding guidance leads with the grant', () => {
  const doc = text('docs/Add-Repo-With-Internal-Ontology-To-Division-Ontology-Three-Step-Instructions.md');
  assert.ok(doc.indexOf('Fast path') >= 0 && doc.indexOf('Fast path') < doc.indexOf('## 1.'));
  assert.match(doc, /\.division-ontology\.json/);
});
test('README, AGENTS and GOVERNANCE describe the single owner action and its limits', () => {
  assert.match(text('README.md'), /\.division-ontology\.json/);
  assert.match(text('AGENTS.md'), /source-read App/);
  assert.match(text('GOVERNANCE.md'), /granted/i);
  assert.match(text('GOVERNANCE.md'), /namespace/i);
});
test('docs state the limits found in review: own account only, paired credentials, local builds', () => {
  assert.match(text('docs/automation-setup.md'), /division's own GitHub account/);
  assert.match(text('docs/automation-setup.md'), /Set the client ID variable and the private key secret together/);
  assert.match(text('docs/Add-Repo-With-Internal-Ontology-To-Division-Ontology-Three-Step-Instructions.md'), /same GitHub account or organisation/);
  assert.match(text('README.md'), /DIVISION_SOURCE_APP=enabled/);
});
