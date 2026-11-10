import test from 'node:test';
import assert from 'node:assert/strict';
import { discoverGrants, githubGrantProvider } from '../src/grants.mjs';
import { InfrastructureError } from '../src/errors.mjs';

const stub = (repositories, { selection = 'selected', configured = [] } = {}) => ({
  listInstallation: async () => ({ selection, repositories }),
  hasImportConfig: async (id) => configured.includes(id),
});
const r = (id, defaultBranch = 'main') => ({ id, defaultBranch });

test('registered sources and the division repository are not discovered', async () => {
  const found = await discoverGrants(stub([r('o/B'), r('o/a'), r('o/reg'), r('o/division')], { configured: ['o/a'] }),
    { registeredIds: new Set(['O/REG']), excludeRepository: 'o/Division' });
  assert.deepEqual(found, { selection: 'selected', repositories: [
    { id: 'o/B', defaultBranch: 'main', hasConfig: false }, { id: 'o/a', defaultBranch: 'main', hasConfig: true }] });
});
test('malformed provider output is rejected', async () => {
  const opts = { registeredIds: new Set() };
  await assert.rejects(discoverGrants(stub([], { selection: 'some' }), opts), InfrastructureError);
  await assert.rejects(discoverGrants(stub([r('bad id')]), opts), InfrastructureError);
  await assert.rejects(discoverGrants(stub([r('o/a', '')]), opts), InfrastructureError);
  await assert.rejects(discoverGrants(stub([r('o/a'), r('O/A')]), opts), /duplicate/i);
});
test('provider failures propagate so the run fails', async () => {
  const failing = { listInstallation: async () => { throw new InfrastructureError('boom'); }, hasImportConfig: async () => true };
  await assert.rejects(discoverGrants(failing, { registeredIds: new Set() }), /boom/);
});

const response = (status, body) => ({ ok: status >= 200 && status < 300, status, json: async () => body });
const fakeFetch = (handler) => { const calls = []; const impl = async (url, init) => { calls.push({ url, init }); return handler(url, init); }; impl.calls = calls; return impl; };
const page = (from, count, total, selection = 'all') => response(200, { total_count: total, repository_selection: selection,
  repositories: Array.from({ length: count }, (_, i) => ({ full_name: `o/r${from + i}`, default_branch: 'main' })) });

test('the GitHub provider reads every page and the selection mode', async () => {
  const fetchImpl = fakeFetch((url) => url.endsWith('page=1') ? page(0, 100, 101) : page(100, 1, 101));
  const listing = await githubGrantProvider({ token: 'secret-token', fetchImpl }).listInstallation();
  assert.equal(listing.repositories.length, 101);
  assert.equal(listing.selection, 'all');
  assert.equal(fetchImpl.calls.length, 2);
  assert.match(fetchImpl.calls[1].url, /\/installation\/repositories\?per_page=100&page=2$/);
  assert.equal(fetchImpl.calls[0].init.headers.Authorization, 'Bearer secret-token');
});
test('a short or inconsistent listing is incomplete', async () => {
  const fetchImpl = fakeFetch((url) => url.endsWith('page=1') ? page(0, 100, 150) : page(100, 0, 150));
  await assert.rejects(githubGrantProvider({ token: 't', fetchImpl }).listInstallation(), /incomplete/);
});
test('HTTP and network failures never carry the token', async () => {
  for (const handler of [() => response(503, {}), () => { throw new Error('socket hang up with secret-token'); }]) {
    await assert.rejects(githubGrantProvider({ token: 'secret-token', fetchImpl: fakeFetch(handler) }).listInstallation(),
      (e) => e instanceof InfrastructureError && !e.message.includes('secret-token'));
  }
});
test('the configuration pre-filter maps 200, 404 and other statuses', async () => {
  const provider = (status) => githubGrantProvider({ token: 't', fetchImpl: fakeFetch(() => response(status, {})) });
  assert.equal(await provider(200).hasImportConfig('o/r1', 'main'), true);
  assert.equal(await provider(404).hasImportConfig('o/r1', 'main'), false);
  await assert.rejects(provider(403).hasImportConfig('o/r1', 'main'), /HTTP 403/);
  const fetchImpl = fakeFetch(() => response(200, {}));
  await githubGrantProvider({ token: 't', fetchImpl }).hasImportConfig('o/r1', 'feature/x');
  assert.match(fetchImpl.calls[0].url, /\/repos\/o\/r1\/contents\/\.division-ontology\.json\?ref=feature%2Fx$/);
});
