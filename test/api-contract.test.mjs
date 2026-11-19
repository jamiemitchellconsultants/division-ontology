import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import YAML from 'yaml';

const text = (path) => readFileSync(new URL(`../${path}`, import.meta.url), 'utf8');
const ci = YAML.parse(text('.github/workflows/ci.yml'));
const job = ci.jobs['api-contract'];
const run = (pattern) => job.steps.find((step) => pattern.test(step.run ?? ''));

test('api-contract runs on every pull request with the pinned SDK and locked restore', () => {
  assert.ok(job, 'ci.yml has an api-contract job');
  assert.equal(ci.on.pull_request?.paths, undefined);
  assert.equal(job.steps.find((s) => s.uses?.startsWith('actions/setup-dotnet@')).with['global-json-file'], 'runtime/global.json');
  assert.ok(run(/dotnet restore runtime\/RuntimeKnowledge\.slnx --locked-mode/));
});

test('api-contract isolates the pinned SDK and verifies its selection before restoring', () => {
  const setup = job.steps.find((step) => step.uses?.startsWith('actions/setup-dotnet@'));
  const verify = run(/dotnet --version/);
  const restore = run(/dotnet restore runtime\/RuntimeKnowledge\.slnx --locked-mode/);
  assert.match(setup.env?.DOTNET_INSTALL_DIR ?? '', /runner\.temp/);
  assert.ok(verify, 'checks the SDK selected from the repository root');
  assert.match(verify.run, /runtime\/global\.json/);
  assert.ok(job.steps.indexOf(setup) < job.steps.indexOf(verify));
  assert.ok(job.steps.indexOf(verify) < job.steps.indexOf(restore));
});

test('the drift check catches changed and untracked spec files and names the regeneration command', () => {
  const check = run(/git status --porcelain -- runtime\/openapi/);
  assert.ok(check, 'uses git status so an uncommitted new file fails');
  assert.match(check.run, /The committed API spec is out of date\. Run: dotnet build runtime\/RuntimeKnowledge\.Api, then commit runtime\/openapi\/\./);
  assert.ok(job.steps.indexOf(run(/^dotnet build runtime\/RuntimeKnowledge\.Api --no-restore$/m)) < job.steps.indexOf(check));
});

test('deployment-shaped settings cannot change the committed spec', () => {
  const step = run(/--no-incremental && git diff --exit-code -- runtime\/openapi/);
  assert.equal(step.env.Workspace__Docs__ClientId, 'workspace-spa');
  assert.equal(step.env.ASPNETCORE_ENVIRONMENT, 'Development');
});

test('the completeness tests run in api-contract', () =>
  assert.ok(run(/dotnet test runtime\/RuntimeKnowledge\.Tests .*--filter Category=OpenApi/)));

test('api-contract is a required check', () =>
  assert.match(text('docs/automation-setup.md'), /required status checks to include `test`, `api-contract`, `ai-fingerprint`, and `merge-policy`/));

test('packaged API builds do not generate the spec', () => {
  assert.match(text('deploy/workspace/app/Dockerfile'), /dotnet publish RuntimeKnowledge\.Api [^\n]*-p:OpenApiGenerateDocuments=false/);
  assert.match(text('deploy/workspace/aws/package.sh'), /dotnet publish "RuntimeKnowledge\.\$component" [^\n]*-p:OpenApiGenerateDocuments=false/);
});

test('the README documents the paths and the regeneration command', () => {
  const readme = text('README.md');
  for (const pattern of [/\/api\/openapi\/v1\.json/, /\/api\/docs/, /dotnet build runtime\/RuntimeKnowledge\.Api/, /runtime\/openapi\/workspace-api\.json/]) assert.match(readme, pattern);
});
