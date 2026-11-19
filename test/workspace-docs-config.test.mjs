import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync, statSync } from 'node:fs';
import YAML from 'yaml';

const text = (path) => readFileSync(new URL(`../${path}`, import.meta.url), 'utf8');
const docs = '/api/docs/oauth2-redirect.html';
const spa = (realm) => JSON.parse(text(realm)).clients.find((c) => c.publicClient);

test('both Keycloak realms allow the docs callback on the web UI client', () => {
  assert.deepEqual(spa('deploy/workspace/local/realm.json').redirectUris,
    ['${WORKSPACE_ORIGIN}/authentication/login-callback', `\${WORKSPACE_ORIGIN}${docs}`]);
  assert.deepEqual(spa('deploy/workspace/home-lab/realm-template.json').redirectUris,
    ['https://${WORKSPACE_PUBLIC_HOST}/authentication/login-callback', `https://\${WORKSPACE_PUBLIC_HOST}${docs}`]);
  for (const realm of ['deploy/workspace/local/realm.json', 'deploy/workspace/home-lab/realm-template.json'])
    assert.ok(spa(realm).protocolMappers.some((m) => m.protocolMapper === 'oidc-audience-mapper'), 'audience mapper kept (B21)');
});

test('the API reuses the web UI client in every deployment', () => {
  assert.equal(YAML.parse(text('deploy/workspace/local/compose.yml')).services.api.environment.Workspace__Docs__ClientId, 'workspace-spa');
  assert.match(YAML.parse(text('deploy/workspace/home-lab/compose.yml')).services.api.environment.Workspace__Docs__ClientId, /^\$\{WORKSPACE_SPA_CLIENT:\?/);
  const identity = text('deploy/workspace/aws/identity.tf');
  assert.match(identity, /Workspace__Docs__ClientId\s*=\s*var\.entra_client_id/);
  assert.match(identity, /Workspace__Docs__Scopes\s*=\s*var\.entra_api_scope/);
  assert.match(text('deploy/workspace/aws/outputs.tf'), /entra_docs_redirect_uri[\s\S]*\/api\/docs\/oauth2-redirect\.html/);
});

test('Entra and externally managed Keycloak setups document the docs callback', () => {
  assert.match(text('deploy/workspace/aws/simulation.md'), /entra_docs_redirect_uri[\s\S]*Single-page application[\s\S]*PKCE/);
  assert.match(text('deploy/workspace/home-lab/deployment-contract.md'), /\/api\/docs\/oauth2-redirect\.html[\s\S]*managed outside the template/);
});

test('the docs check runs in the local smoke and is executable', () => {
  assert.match(text('deploy/workspace/local/smoke.sh'), /verify-docs\.sh" "http:\/\/workspace\.localhost:\$WORKSPACE_PORT" --expect-sign-in/);
  assert.ok(statSync(new URL('../deploy/workspace/verify-docs.sh', import.meta.url)).mode & 0o111);
});

test('the docs check refuses a served server address and expects the 401 code', () => {
  const script = text('deploy/workspace/verify-docs.sh');
  assert.match(script, /has\("servers"\) \| not/);
  assert.match(script, /authenticationRequired/);
});
