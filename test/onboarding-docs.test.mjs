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
test('the README says init-division removes the template-only tests', () => {
  assert.match(text('README.md'), /test\/template\.test\.mjs/);
  assert.match(text('README.md'), /test\/init-division\.test\.mjs/);
});
const startHere = () => text('docs/agents-start-here.md');
test('agents start here asks for the required choices before creation', () => {
  const doc = startHere();
  for (const pattern of [/target owner\/name/i, /division namespace/i, /steward logins/i, /public repository/i]) assert.match(doc, pattern);
  assert.match(doc, /defaulted — not answered/);
});
test('agents start here prompts for each optional setup with its declined default', () => {
  const doc = startHere();
  for (const pattern of [/runtime knowledge workspace/i, /catalogue-only/, /embedding/i, /no semantic search/i, /C4/, /not set up/, /source-read App/, /steward-registered sources only/, /auto-merge/, /steward review/]) assert.match(doc, pattern);
});
test('agents start here records states, stops, and pending hand-offs', () => {
  const doc = startHere();
  for (const pattern of [/accepted/, /declined/, /pending/i, /who must/i]) assert.match(doc, pattern);
  assert.match(doc, /never.*(grant itself|credentials)/i);
});
test('agents start here points to the canonical homes and keeps similarity as discovery', () => {
  const doc = startHere();
  for (const pattern of [/AGENTS\.md/, /GOVERNANCE\.md/, /automation-setup\.md/, /05-runbook\.md/]) assert.match(doc, pattern);
  assert.match(doc, /discovery/i);
  assert.doesNotMatch(doc, /create a link from a (similarity )?match/i);
});
test('AGENTS and README point at agents start here', () => {
  assert.match(text('AGENTS.md'), /agents-start-here\.md/);
  assert.match(text('AGENTS.md'), /Create a division from this template/);
  assert.match(text('README.md'), /agents-start-here\.md/);
});
