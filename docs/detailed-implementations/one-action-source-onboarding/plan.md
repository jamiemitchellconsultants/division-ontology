# One-action source onboarding — detailed implementation plan

**Inputs:** [accepted vision](../../features/one-action-source-onboarding/01-vision.md), [behaviours B1–B17](../../features/one-action-source-onboarding/02-behaviours.md), [architecture](../../features/one-action-source-onboarding/03-architecture.md), ADRs 0009–0012, and the [task plan](../../superpowers/plans/one-action-source-onboarding/plan.md).

**Build rule:** Work on `feature/one-action-source-onboarding/dev-build` only after this plan PR merges. Execute Tasks 1–13 in order. For each task: add the named test file, run it and see it fail, implement the interface, run the focused test and `npm test`, review `git diff --cached` and `git diff --cached --check`, then commit and push. The superpowers plan has the commit messages. Do not stage `dist/`. Existing tests must keep passing with their current messages.

## Decisions this plan settles

These were passed to the developer by the architect.

- **`repository_selection` is available.** GitHub's REST description (version 2022-11-28) lists `repository_selection`, `total_count` and `repositories` in the response of `GET /installation/repositories`. `githubGrantProvider` reads the selection from the first page, so no fallback is needed.
- **Contents API volume.** A sync makes `ceil(N/100)` listing requests plus one `GET /repos/{id}/contents/.division-ontology.json?ref={branch}` per installed repository, at most 8 at a time. A rate-limit or server response fails the run (`InfrastructureError`); the next schedule retries. `docs/automation-setup.md` states the cost is one request per installed repository per sync.
- **Consumers of `catalog.sources`.** Only `src/c4/snapshot.mjs` compares `catalog.sources` with `sources.json`; the workspace bundle reads sources through `loadSnapshot`. Task 11 fixes the loader once. A granted source gets the derived URL `https://github.com/<id>`, so GitHub Enterprise hosts are not supported by this feature.
- **A source that is both stale and later registered.** When a published granted source appears in `sources.json`, registration governs: its carried-forward graph is dropped and it is built strictly (U7).
- **Prefix namespaces.** Two namespaces overlap when either starts with the other, not only when equal. Otherwise two sources could define the same concept IRI and fail the whole build (B15, B16).
- **Status when a source is installed but unconfigured.** Only a repository with a published version or a configuration is cloned; for the rest the status row carries no attempted commit. A published source whose configuration disappears is `invalid` with `config-missing` and its older version is carried forward (B11).

## Contracts

```js
// Reason = {code, detail}                       // detail is safeDetail()-sanitised, max 200 chars
// Discovery = {selection:'all'|'selected', repositories:[{id,defaultBranch,hasConfig}]}   // sorted by id
// Outcome = {id, attemptedCommit, result:{ok:true,namespace,quads}|{ok:false,reason}}
// Published = Map<id,{commit,namespace,quads}>  // granted sources in the committed dist/
// Row = {source,status,attemptedCommit,publishedCommit,reason?}   // key order as written; null for none
// GrantedSource = {id,namespace,commit,quads,origin:'granted',stale}
// provider = {listInstallation():Promise<{selection,repositories:[{id,defaultBranch}]}>,
//             hasImportConfig(id,defaultBranch):Promise<boolean>}
// .division-ontology.json = {"schemaVersion":1,"ontologyPath":"…","namespace":"https://…"}
```

Catalog additions: every `sources[]` entry gets `origin` (`registered`|`granted`) and `stale` (boolean), written after `conceptCount`; the catalog gets `importStatus` (array, possibly empty), written after `rules` and before `links`. Reason codes: `config-missing`, `config-invalid`, `ontology-unreadable`, `ontology-invalid-turtle`, `concept-outside-namespace`, `no-concepts`, `namespace-conflict`, `access-removed`.

## Task 1 — reasons and sanitising

**Files:** `src/import-status.mjs`, `test/import-status-reasons.test.mjs`.

**Interfaces:** `STATUSES`, `REASON_CODES`, `safeDetail(value)->string`, `reason(code,detail='')->Reason`.

`safeDetail`: coerce to string; replace control characters with a space; replace `ghp_`/`gho_`/`ghu_`/`ghs_`/`ghr_` tokens (16 or more word characters), `github_pat_` tokens, the text `x-access-token` and `//user:password@` URL credentials with `[redacted]` (keep the `//` and `@` around the last); trim; if longer than 200, keep 199 characters and append `…`.

```js
import test from 'node:test';
import assert from 'node:assert/strict';
import { REASON_CODES, STATUSES, reason, safeDetail } from '../src/import-status.mjs';

test('status values and reason codes are the closed sets from ADR 0011', () => {
  assert.deepEqual(STATUSES, ['current', 'invalid', 'not configured', 'access removed']);
  assert.deepEqual(REASON_CODES, ['config-missing', 'config-invalid', 'ontology-unreadable', 'ontology-invalid-turtle',
    'concept-outside-namespace', 'no-concepts', 'namespace-conflict', 'access-removed']);
});
test('reason rejects an unknown code and keeps an ordinary detail', () => {
  assert.throws(() => reason('boom', 'x'), /unknown reason code boom/);
  assert.deepEqual(reason('config-missing', '.division-ontology.json'), { code: 'config-missing', detail: '.division-ontology.json' });
  assert.deepEqual(reason('no-concepts'), { code: 'no-concepts', detail: '' });
});
test('safeDetail redacts token shapes and URL credentials', () => {
  const token = `ghp_${'a1B2'.repeat(8)}`;
  for (const text of [`token ${token} leaked`, 'github_pat_' + 'Z9'.repeat(20), 'header x-access-token here', 'https://user:hunter2@github.com/o/r']) {
    const out = safeDetail(text);
    assert.match(out, /\[redacted\]/);
    assert.doesNotMatch(out, /ghp_|github_pat_|x-access-token|hunter2/);
  }
});
test('safeDetail strips control characters and bounds the length', () => {
  assert.equal(safeDetail('a\u0000b\nc'), 'a b c');
  const out = safeDetail('x'.repeat(500));
  assert.equal(out.length, 200);
  assert.ok(out.endsWith('…'));
});
```

## Task 2 — import configuration

**Files:** `src/import-config.mjs`, `test/import-config.test.mjs`.

**Also modify:** `src/sources.mjs` to export `safeRelativePath` (no behaviour change).

**Interfaces:** `IMPORT_CONFIG_PATH = '.division-ontology.json'`, `IMPORT_CONFIG_MAX_BYTES = 16384`, `parseImportConfiguration(text)`. Check order: byte length, JSON parse, plain object, unknown keys, `schemaVersion === 1`, `ontologyPath` (non-empty string, not absolute, no `..` segment, using the same rule as `safeRelativePath` in `src/sources.mjs`; export that helper rather than copy it), `namespace` (string matching `^https?://`). Details are exactly the strings the test matches. Every failure is `reason('config-invalid', detail)`.

```js
import test from 'node:test';
import assert from 'node:assert/strict';
import { IMPORT_CONFIG_MAX_BYTES, IMPORT_CONFIG_PATH, parseImportConfiguration } from '../src/import-config.mjs';

const valid = { schemaVersion: 1, ontologyPath: 'docs/ontology.ttl', namespace: 'https://example.test/a#' };
const parse = (value) => parseImportConfiguration(typeof value === 'string' ? value : JSON.stringify(value));

test('the file lives at a fixed repository-root path with a 16 KiB limit', () => {
  assert.equal(IMPORT_CONFIG_PATH, '.division-ontology.json');
  assert.equal(IMPORT_CONFIG_MAX_BYTES, 16384);
});
test('a valid file yields its ontology path and namespace', () => {
  assert.deepEqual(parse(valid), { ok: true, config: { ontologyPath: 'docs/ontology.ttl', namespace: 'https://example.test/a#' } });
});
for (const [name, input, detail] of [
  ['text that is not JSON', '{nope', /not valid JSON/],
  ['a JSON array', '[]', /must be a JSON object/],
  ['an unknown key', { ...valid, extra: 1 }, /unknown key extra/],
  ['another schema version', { ...valid, schemaVersion: 2 }, /schemaVersion must be 1/],
  ['a missing namespace', { schemaVersion: 1, ontologyPath: 'docs/ontology.ttl' }, /namespace must be an HTTP IRI/],
  ['a non-HTTP namespace', { ...valid, namespace: 'ftp://example.test/a#' }, /namespace must be an HTTP IRI/],
  ['an absolute path', { ...valid, ontologyPath: '/etc/o.ttl' }, /ontologyPath must stay inside the repository/],
  ['a parent-directory path', { ...valid, ontologyPath: 'docs/../../o.ttl' }, /ontologyPath must stay inside the repository/],
  ['an empty path', { ...valid, ontologyPath: '' }, /ontologyPath must stay inside the repository/],
  ['an oversized file', JSON.stringify({ ...valid, pad: 'x'.repeat(17000) }), /larger than 16 KiB/],
]) {
  test(`rejects ${name} as config-invalid`, () => {
    const result = parse(input);
    assert.equal(result.ok, false);
    assert.equal(result.reason.code, 'config-invalid');
    assert.match(result.reason.detail, detail);
  });
}
```

## Task 3 — fixture division

**Files:** `test/fixtures/onboarding/fixture-division.mjs`, `test/fixture-division.test.mjs`.

**Interfaces:** exports `coreNamespace = 'https://example.test/division#'`, `ontologyText(namespace, terms=['Thing'])` (Turtle declaring each term as an `owl:Class` with an `rdfs:label`), `importConfig(namespace, ontologyPath='docs/ontology.ttl')` (JSON text), and `createFixtureDivision()` returning:

`root`; `addSource(id,{namespace?,terms?,config?,branch?})->{dir,commit}` (`config` is the exact text of `.division-ontology.json`, `null` for no file, and defaults to `importConfig(namespace)`; a local Git repository named `<root>/sources/<owner>__<name>` with `origin` set to itself, `docs/ontology.ttl`, and `.division-ontology.json` unless `config === null`; default namespace `https://example.test/<owner>-<name>#`); `commitSource(id, {path: text|null})->commit` (null deletes a file; commits everything); `head(id)`; `register(id)` (writes it into `sources.json` with `ref` the branch, `ontologyPath: 'docs/ontology.ttl'` and its namespace); `grant(id)`; `revoke(id)`; `setSelection('all'|'selected')`; `failListing(message)`; `provider`; `urlFor(id)->dir`; `overrides()->Map<id,dir>` of every added source; `checkoutArgs()->string[]` of `--checkout id=dir` for registered and granted ids; `run(command, {root?}={})->runCli([command,...checkoutArgs()], {root, grantProvider: provider, urlFor})`; `cloneRoot()->string` (a copy of the division root, removed by `cleanup`); `catalog(root?)`; `dist(name, root?)`; `cleanup()`. The core ontology is `ontologyText(coreNamespace, ['StaffId'])`; `division.json` has one steward; `sources.json` starts empty. Git commits use `-c user.name=Test -c user.email=test@example.invalid`. `provider.listInstallation()` rejects with `new Error(message)` after `failListing`, otherwise returns the granted ids sorted with their branch; `provider.hasImportConfig(id)` is true when `HEAD:.division-ontology.json` exists in the repository.

```js
import test from 'node:test';
import assert from 'node:assert/strict';
import { createFixtureDivision, importConfig, ontologyText } from './fixtures/onboarding/fixture-division.mjs';

test('a registered fixture source builds into the catalog', async () => {
  const d = createFixtureDivision();
  try {
    d.addSource('example/alpha', { terms: ['Alpha'] });
    d.register('example/alpha');
    await d.run('build');
    assert.deepEqual(d.catalog().sources.map((s) => s.id), ['example/alpha']);
    assert.match(d.head('example/alpha'), /^[0-9a-f]{40}$/);
  } finally { d.cleanup(); }
});
test('the provider lists granted repositories and whether they carry configuration', async () => {
  const d = createFixtureDivision();
  try {
    d.addSource('example/with', {});
    d.addSource('example/without', { config: null });
    d.addSource('example/unlisted', {});
    d.grant('example/with'); d.grant('example/without');
    assert.deepEqual(await d.provider.listInstallation(), { selection: 'selected', repositories: [
      { id: 'example/with', defaultBranch: 'main' }, { id: 'example/without', defaultBranch: 'main' }] });
    assert.equal(await d.provider.hasImportConfig('example/with', 'main'), true);
    assert.equal(await d.provider.hasImportConfig('example/without', 'main'), false);
    d.setSelection('all');
    assert.equal((await d.provider.listInstallation()).selection, 'all');
    d.failListing('HTTP 503');
    await assert.rejects(d.provider.listInstallation(), /HTTP 503/);
    d.revoke('example/with');
  } finally { d.cleanup(); }
});
test('helpers write the documented text', () => {
  assert.match(ontologyText('https://x.test/a#', ['A']), /:A a owl:Class; rdfs:label "A"/);
  assert.deepEqual(JSON.parse(importConfig('https://x.test/a#')), { schemaVersion: 1, ontologyPath: 'docs/ontology.ttl', namespace: 'https://x.test/a#' });
});
```

## Task 4 — typed ontology errors and the granted source importer

**Files:** `src/errors.mjs`, `src/granted-sources.mjs`, `src/rdf.mjs` (modify), `test/granted-sources.test.mjs`.

**Interfaces:** `class InfrastructureError extends Error` in `src/errors.mjs`. In `src/rdf.mjs`, the four existing failures keep their messages and gain `code` and `detail`: `parseTurtle` → `ontology-invalid-turtle`, detail `line <n>` from `error.context.line` or `line unknown`; the non-blob `ontologyPath` failure → `ontology-unreadable`, detail the path; a concept outside the namespace → `concept-outside-namespace`, detail the IRI; no concepts in namespace → `no-concepts`, detail the namespace. `importGrantedSources({repositories, published, urlFor = (id) => \`https://github.com/${id}.git\`, checkoutOverrides = new Map()})->Outcome[]`.

Behaviour: iterate `repositories` by id; skip a repository unless `hasConfig` or `published.has(id)`. `resolveSource({id,url:urlFor(id),ref:defaultBranch}, checkoutOverrides)`; any error from it becomes `InfrastructureError(\`${id}: ${message}\`)`. In the clone, `git ls-tree <commit> -- .division-ontology.json`: no entry → `config-missing`; a mode other than `100644`/`100755` → `config-invalid` with detail `not a regular file`; `git cat-file -s` above the limit → `config-invalid` with detail `file is larger than 16 KiB`; otherwise `git show` and `parseImportConfiguration`. Then `readSourceOntology({id,ontologyPath,namespace}, directory, commit)`; an error with a `code` becomes `reason(code, detail)`; any other error becomes `InfrastructureError`. Always `cleanup()`. `attemptedCommit` is the cloned HEAD. Content outcomes never throw.

```js
import test from 'node:test';
import assert from 'node:assert/strict';
import { symlinkSync } from 'node:fs';
import { join } from 'node:path';
import { createFixtureDivision, importConfig, ontologyText } from './fixtures/onboarding/fixture-division.mjs';
import { importGrantedSources } from '../src/granted-sources.mjs';
import { InfrastructureError } from '../src/errors.mjs';
import { readSourceOntology } from '../src/rdf.mjs';

const ns = (n) => `https://example.test/${n}#`;
const repo = (id, hasConfig = true) => ({ id, defaultBranch: 'main', hasConfig });
const withDivision = (fn) => async () => { const d = createFixtureDivision(); try { await fn(d); } finally { d.cleanup(); } };
const run = (d, repos, published = new Map()) => importGrantedSources({ repositories: repos, published, urlFor: d.urlFor, checkoutOverrides: d.overrides() });

test('a valid repository yields its namespace, quads and the commit it read', withDivision((d) => {
  d.addSource('o/a', { namespace: ns('a'), terms: ['A', 'B'] });
  const [outcome] = run(d, [repo('o/a')]);
  assert.equal(outcome.id, 'o/a');
  assert.equal(outcome.attemptedCommit, d.head('o/a'));
  assert.equal(outcome.result.ok, true);
  assert.equal(outcome.result.namespace, ns('a'));
  assert.ok(outcome.result.quads.length >= 4);
}));
test('an unconfigured, unpublished repository is not cloned', withDivision((d) => {
  d.addSource('o/a', { config: null });
  assert.deepEqual(run(d, [repo('o/a', false)]), []);
}));
test('a published repository is attempted even when its configuration is gone', withDivision((d) => {
  d.addSource('o/a', { config: null });
  const [outcome] = run(d, [repo('o/a', false)], new Map([['o/a', {}]]));
  assert.equal(outcome.attemptedCommit, d.head('o/a'));
  assert.equal(outcome.result.reason.code, 'config-missing');
}));
test('configuration problems are config-invalid', withDivision((d) => {
  d.addSource('o/json', { config: '{nope' });
  d.addSource('o/key', { config: JSON.stringify({ schemaVersion: 1, ontologyPath: 'docs/ontology.ttl', namespace: ns('key'), extra: 1 }) });
  d.addSource('o/big', { config: JSON.stringify({ schemaVersion: 1, ontologyPath: 'docs/ontology.ttl', namespace: ns('big'), pad: 'x'.repeat(17000) }) });
  d.addSource('o/link', { config: null });
  symlinkSync('docs/ontology.ttl', join(d.overrides().get('o/link'), '.division-ontology.json'));
  d.commitSource('o/link', {});
  const byId = Object.fromEntries(run(d, ['o/big', 'o/json', 'o/key', 'o/link'].map((id) => repo(id))).map((o) => [o.id, o.result]));
  for (const id of ['o/big', 'o/json', 'o/key', 'o/link']) { assert.equal(byId[id].ok, false, id); assert.equal(byId[id].reason.code, 'config-invalid', id); }
  assert.match(byId['o/link'].reason.detail, /not a regular file/);
  assert.match(byId['o/big'].reason.detail, /larger than 16 KiB/);
}));
test('ontology problems carry a code and detail and do not stop other repositories', withDivision((d) => {
  d.addSource('o/bad', { namespace: ns('bad') });
  d.addSource('o/good', { namespace: ns('good') });
  d.commitSource('o/bad', { 'docs/ontology.ttl': 'this is not turtle {' });
  const outcomes = run(d, [repo('o/bad'), repo('o/good')]);
  assert.deepEqual(outcomes.map((o) => [o.id, o.result.ok]), [['o/bad', false], ['o/good', true]]);
  assert.equal(outcomes[0].result.reason.code, 'ontology-invalid-turtle');
  assert.match(outcomes[0].result.reason.detail, /^line /);
}));
test('readSourceOntology failures carry codes and details', withDivision((d) => {
  const cases = [
    ['docs/missing.ttl', ns('m'), 'ontology-unreadable', 'docs/missing.ttl', {}],
    ['docs/ontology.ttl', ns('o'), 'concept-outside-namespace', ns('other') + 'Thing', { 'docs/ontology.ttl': ontologyText(ns('other')) }],
    ['docs/ontology.ttl', ns('e'), 'no-concepts', ns('e'), { 'docs/ontology.ttl': `@prefix : <${ns('e')}> .\n` }],
  ];
  for (const [index, [ontologyPath, namespace, code, detail, files]] of cases.entries()) {
    const id = `o/case${index}`;
    d.addSource(id, { namespace });
    const commit = d.commitSource(id, files);
    assert.throws(() => readSourceOntology({ id, ontologyPath, namespace }, d.overrides().get(id), commit), (e) => e.code === code && e.detail === detail, code);
  }
}));
test('a configuration change is followed on the next import', withDivision((d) => {
  d.addSource('o/a', { namespace: ns('a'), terms: ['Old'] });
  d.commitSource('o/a', { 'docs/new.ttl': ontologyText(ns('a'), ['New']), '.division-ontology.json': importConfig(ns('a'), 'docs/new.ttl'), 'docs/ontology.ttl': null });
  const [outcome] = run(d, [repo('o/a')]);
  assert.equal(outcome.result.ok, true);
  assert.ok(outcome.result.quads.some((q) => q.subject.value === ns('a') + 'New'));
}));
test('a clone failure is an infrastructure error, not a status', () => {
  assert.throws(() => importGrantedSources({ repositories: [repo('o/gone')], published: new Map(), urlFor: () => '/nonexistent/path/for/test', checkoutOverrides: new Map() }), InfrastructureError);
});
```

## Task 5 — grant discovery

**Files:** `src/grants.mjs`, `test/grants.test.mjs`.

**Interfaces:** `discoverGrants(provider, {registeredIds, excludeRepository})->Promise<Discovery>`: call `listInstallation()`; throw `InfrastructureError` if `selection` is not `all` or `selected`, an id does not match `^[\w.-]+\/[\w.-]+$`, a `defaultBranch` is empty, or ids repeat ignoring case (message contains `duplicate repository`); drop `excludeRepository` and every id in `registeredIds` (case-insensitive); call `hasImportConfig(id, defaultBranch)` for the rest, at most 8 at once; return them sorted by code point. Provider rejections propagate. `githubGrantProvider({token, fetchImpl = globalThis.fetch, apiBase = 'https://api.github.com'})`: requests send `Authorization: Bearer <token>`, `Accept: application/vnd.github+json`, `X-GitHub-Api-Version: 2022-11-28`. `listInstallation` reads `GET /installation/repositories?per_page=100&page=N` from page 1 until it holds `total_count` repositories; an empty page earlier, or a count above `total_count`, throws `InfrastructureError` containing `incomplete`; `selection` is `repository_selection` of page 1. `hasImportConfig` returns true for HTTP 200 and false for 404. Every other status, and every thrown fetch error, becomes `InfrastructureError('GitHub API <path>: HTTP <status>')` or `…: request failed`, never containing the token.

```js
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
```

## Task 6 — status classification

**Files:** `src/import-status.mjs` (extend), `test/import-status-classify.test.mjs`.

**Interfaces:** `classifyGrantedSources({discovery, outcomes, published, registeredIds, reservedNamespaces})->{rows, sources}`; `reservedNamespaces` is `Map<namespace, holderLabel>` holding the core (`'division core'`) and every registered source (its id).

Algorithm, all in code-point id order:

1. A published id absent from `discovery.repositories` and in `registeredIds` is dropped silently. Absent and not registered: row `access removed` (`attemptedCommit: null`, `publishedCommit` the published commit, reason `access-removed` with detail `repository is no longer in the installation`), carried forward stale, and its namespace reserved to it. Do these first.
2. Then discovered repositories that have a published version, then the rest.
3. For a repository: no outcome and no published version → if `discovery.selection === 'selected'`, row `not configured` (both commits null, reason `config-missing` detail `.division-ontology.json`); with `all`, nothing.
4. An `ok` outcome whose namespace overlaps (either is a prefix of the other) a reserved namespace held by another label becomes failure `namespace-conflict`, detail `overlaps <holder>`.
5. Success: row `current` (attempted = published = outcome commit), a `GrantedSource` with `stale:false`, and the namespace reserved to the id.
6. Failure with a published version: row `invalid` (attempted from the outcome, published from the old version, the reason), the old version carried forward as `stale:true`, its namespace reserved. Failure without one: row `invalid`, `publishedCommit: null`, nothing carried.
7. `rows` sorted by `source`; `sources` sorted by `id`. Row keys in order `source, status, attemptedCommit, publishedCommit, reason`; omit `reason` for `current`.

```js
import test from 'node:test';
import assert from 'node:assert/strict';
import { classifyGrantedSources, reason } from '../src/import-status.mjs';
import { parseTurtle } from '../src/rdf.mjs';
import { ontologyText } from './fixtures/onboarding/fixture-division.mjs';

const core = 'https://example.test/division#';
const ns = (n) => `https://example.test/${n}#`;
const sha = (c) => c.repeat(40);
const quads = (namespace) => parseTurtle(ontologyText(namespace), namespace);
const published = (...entries) => new Map(entries.map(([id, commit, namespace]) => [id, { commit, namespace, quads: quads(namespace) }]));
const repo = (id, hasConfig = true) => ({ id, defaultBranch: 'main', hasConfig });
const ok = (id, commit, namespace) => ({ id, attemptedCommit: commit, result: { ok: true, namespace, quads: quads(namespace) } });
const bad = (id, commit, code = 'ontology-invalid-turtle') => ({ id, attemptedCommit: commit, result: { ok: false, reason: reason(code, 'x') } });
const classify = (input) => classifyGrantedSources({ discovery: { selection: 'selected', repositories: [] }, outcomes: [], published: new Map(),
  registeredIds: new Set(), reservedNamespaces: new Map([[core, 'division core']]), ...input });
const discovery = (repositories, selection = 'selected') => ({ selection, repositories });

test('a valid new source is current', () => {
  const { rows, sources } = classify({ discovery: discovery([repo('o/a')]), outcomes: [ok('o/a', sha('a'), ns('a'))] });
  assert.deepEqual(rows, [{ source: 'o/a', status: 'current', attemptedCommit: sha('a'), publishedCommit: sha('a') }]);
  assert.deepEqual(sources.map(({ id, commit, namespace, origin, stale }) => ({ id, commit, namespace, origin, stale })),
    [{ id: 'o/a', commit: sha('a'), namespace: ns('a'), origin: 'granted', stale: false }]);
});
test('an invalid later version keeps the published one, marked stale', () => {
  const { rows, sources } = classify({ discovery: discovery([repo('o/a')]), outcomes: [bad('o/a', sha('b'))], published: published(['o/a', sha('a'), ns('a')]) });
  assert.deepEqual(rows[0], { source: 'o/a', status: 'invalid', attemptedCommit: sha('b'), publishedCommit: sha('a'), reason: { code: 'ontology-invalid-turtle', detail: 'x' } });
  assert.deepEqual([sources[0].commit, sources[0].stale], [sha('a'), true]);
});
test('an invalid first version is reported but not published', () => {
  const { rows, sources } = classify({ discovery: discovery([repo('o/a')]), outcomes: [bad('o/a', sha('b'))] });
  assert.deepEqual([rows[0].status, rows[0].publishedCommit, sources], ['invalid', null, []]);
});
test('a removed configuration on a published source is invalid and stale', () => {
  const { rows, sources } = classify({ discovery: discovery([repo('o/a', false)]), outcomes: [bad('o/a', sha('b'), 'config-missing')], published: published(['o/a', sha('a'), ns('a')]) });
  assert.deepEqual([rows[0].status, rows[0].reason.code, sources[0].stale], ['invalid', 'config-missing', true]);
});
test('an unconfigured repository is reported under a selected installation and ignored under all', () => {
  const selected = classify({ discovery: discovery([repo('o/a', false)]) });
  assert.deepEqual(selected.rows, [{ source: 'o/a', status: 'not configured', attemptedCommit: null, publishedCommit: null, reason: reason('config-missing', '.division-ontology.json') }]);
  assert.deepEqual(classify({ discovery: discovery([repo('o/a', false)], 'all') }).rows, []);
});
test('removed access keeps the published version stale; a never-published source leaves no row', () => {
  const { rows, sources } = classify({ published: published(['o/a', sha('a'), ns('a')]) });
  assert.deepEqual(rows, [{ source: 'o/a', status: 'access removed', attemptedCommit: null, publishedCommit: sha('a'), reason: reason('access-removed', 'repository is no longer in the installation') }]);
  assert.equal(sources[0].stale, true);
  assert.deepEqual(classify({}).rows, []);
});
test('a published source that is now steward-registered is dropped', () => {
  const out = classify({ published: published(['o/a', sha('a'), ns('a')]), registeredIds: new Set(['o/a']) });
  assert.deepEqual([out.rows, out.sources], [[], []]);
});
test('a namespace equal to, or overlapping, the core or a registered source is a conflict', () => {
  const reservedNamespaces = new Map([[core, 'division core'], [ns('reg'), 'o/reg']]);
  const outcomes = [ok('o/c', sha('a'), core), ok('o/p', sha('a'), `${ns('reg')}sub`), ok('o/q', sha('a'), 'https://example.test/')];
  const { rows, sources } = classify({ discovery: discovery(['o/c', 'o/p', 'o/q'].map((id) => repo(id))), outcomes, reservedNamespaces });
  assert.deepEqual(rows.map((row) => [row.status, row.reason.code]), [['invalid', 'namespace-conflict'], ['invalid', 'namespace-conflict'], ['invalid', 'namespace-conflict']]);
  assert.match(rows[0].reason.detail, /overlaps division core/);
  assert.deepEqual(sources, []);
});
test('the existing holder of a namespace wins regardless of id order', () => {
  const { rows } = classify({ discovery: discovery([repo('a/new'), repo('z/old')]),
    outcomes: [ok('a/new', sha('b'), ns('x')), ok('z/old', sha('c'), ns('x'))], published: published(['z/old', sha('c'), ns('x')]) });
  assert.deepEqual(rows.map((row) => [row.source, row.status]), [['a/new', 'invalid'], ['z/old', 'current']]);
  assert.equal(rows[0].reason.code, 'namespace-conflict');
});
test('one failing source does not hold back the others, and rows are sorted', () => {
  const { rows, sources } = classify({ discovery: discovery([repo('o/z'), repo('o/a'), repo('o/m', false)]), outcomes: [ok('o/z', sha('a'), ns('z')), bad('o/a', sha('b'))] });
  assert.deepEqual(rows.map((row) => [row.source, row.status]), [['o/a', 'invalid'], ['o/m', 'not configured'], ['o/z', 'current']]);
  assert.deepEqual(sources.map((s) => s.id), ['o/z']);
});
```

## Task 7 — catalog builder, renderer and published state

**Files:** `src/catalog.mjs`, `src/output.mjs`, `src/import-status.mjs` (extend), `test/catalog-granted.test.mjs`.

**Interfaces:**

- `buildCatalog({coreQuads, sources, links, coreNamespace, importStatus = []})`: each source may have `origin` (default `'registered'`) and `stale` (default `false`); each summary is `{id, commit, namespace, conceptCount, origin, stale}`; the catalog is `{schemaVersion, core, sources, concepts, relations, rules, importStatus, links}`. A link whose source is not `registered` throws `link source <id> is granted; links need steward registration`; make this the first check in the link loop, before the concept lookups. Everything else, including graph names and ordering, is unchanged.
- `renderOutputs`: Markdown gains, after the Sources table and only when `importStatus` is non-empty, `## Import status` and a table `| Repository | Status | Attempted commit | Published commit | Reason |`, one row per entry, commits in backticks, a missing commit as `—`, the reason as `code: detail`. `catalog.json` is the catalog as built.
- `readPublishedState(root)->Published`: read `dist/catalog.json` and `dist/combined.nq` under `root`. Return an empty `Map` when `dist/catalog.json` does not exist. For every source with `origin === 'granted'`, return its commit, namespace and the quads of graph `urn:division-ontology:source:<encodeURIComponent(id)>:<commit>` (default graph in the returned quads is not required; the builder re-assigns graphs). Registered sources are not returned.

```js
import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { buildCatalog } from '../src/catalog.mjs';
import { renderOutputs, writeOutputs } from '../src/output.mjs';
import { readPublishedState, reason } from '../src/import-status.mjs';
import { parseTurtle } from '../src/rdf.mjs';
import { coreNamespace, ontologyText } from './fixtures/onboarding/fixture-division.mjs';

const ns = (n) => `https://example.test/${n}#`;
const coreQuads = parseTurtle(ontologyText(coreNamespace, ['StaffId']), coreNamespace);
const source = (id, n, extra = {}) => ({ id, commit: 'a'.repeat(40), namespace: ns(n), quads: parseTurtle(ontologyText(ns(n), ['Thing']), ns(n)), ...extra });
const build = (sources, importStatus, links = []) => buildCatalog({ coreQuads, sources, links, coreNamespace, importStatus });

test('sources record origin and stale, defaulting to registered and fresh', () => {
  const { catalog } = build([source('o/a', 'a'), source('o/b', 'b', { origin: 'granted', stale: true })], []);
  assert.deepEqual(catalog.sources.map(({ id, conceptCount, origin, stale }) => ({ id, conceptCount, origin, stale })),
    [{ id: 'o/a', conceptCount: 1, origin: 'registered', stale: false }, { id: 'o/b', conceptCount: 1, origin: 'granted', stale: true }]);
  assert.deepEqual(catalog.importStatus, []);
  assert.deepEqual(Object.keys(catalog), ['schemaVersion', 'core', 'sources', 'concepts', 'relations', 'rules', 'importStatus', 'links']);
});
test('a link naming a granted source fails the build', () => {
  const links = [{ source: 'o/b', local: ns('b') + 'Thing', shared: coreNamespace + 'StaffId', relation: 'specializes' }];
  assert.throws(() => build([source('o/b', 'b', { origin: 'granted' })], [], links), /granted; links need steward registration/);
});
test('the import status is published in JSON and as a table', async () => {
  const rows = [{ source: 'o/b', status: 'invalid', attemptedCommit: 'b'.repeat(40), publishedCommit: null, reason: reason('config-missing', '.division-ontology.json') }];
  const { catalog, quads } = build([source('o/a', 'a')], rows);
  const out = await renderOutputs(catalog, quads);
  assert.deepEqual(JSON.parse(out['catalog.json']).importStatus, rows);
  assert.match(out['catalog.md'], /## Import status/);
  assert.match(out['catalog.md'], /\| o\/b \| invalid \| `b{40}` \| — \| config-missing: \.division-ontology\.json \|/);
  const none = await renderOutputs(...Object.values(build([source('o/a', 'a')], [])));
  assert.doesNotMatch(none['catalog.md'], /Import status/);
});
test('readPublishedState returns granted sources only, with their recorded graph', async () => {
  const root = mkdtempSync(join(tmpdir(), 'published-state-'));
  try {
    assert.equal(readPublishedState(root).size, 0);
    const granted = source('o/b', 'b', { origin: 'granted' });
    const { catalog, quads } = build([source('o/a', 'a'), granted], []);
    writeOutputs(root, await renderOutputs(catalog, quads));
    const state = readPublishedState(root);
    assert.deepEqual([...state.keys()], ['o/b']);
    assert.equal(state.get('o/b').commit, granted.commit);
    assert.equal(state.get('o/b').namespace, ns('b'));
    assert.equal(state.get('o/b').quads.length, granted.quads.length);
  } finally { rmSync(root, { recursive: true, force: true }); }
});
```

## Task 8 — orchestration and the behaviour tests

**Files:** `src/cli.mjs`, `test/onboarding-behaviours.test.mjs`.

**Interfaces:** `runCli(argv, {root, grantProvider, urlFor, env = process.env})`. When `'grantProvider' in options` use it (`null` disables discovery); otherwise create `githubGrantProvider({token: env.GH_SOURCE_TOKEN})` if `env.DIVISION_SOURCE_APP === 'enabled'` (throw `GH_SOURCE_TOKEN is required` without a token), else `null`. The result gains `granted`, the number of granted sources in the catalog.

Flow after the registered sources resolve (unchanged, strict, with their links):

1. `published = readPublishedState(root)`, before any `--checkout` validation. With no provider, throw `the committed catalog holds granted sources but no source-read App is configured` if any published id is not in `sources.json`; otherwise the catalog is built from registered sources only.
2. With a provider: `discovery = await discoverGrants(provider, {registeredIds, excludeRepository: env.GITHUB_REPOSITORY})`. Validate `--checkout` overrides now: each id must be in `sources.json` or a discovered repository, else throw the existing message `checkout override <id> is not in sources.json`.
3. `outcomes = importGrantedSources({repositories: discovery.repositories, published, urlFor, checkoutOverrides})`; `{rows, sources} = classifyGrantedSources({…, reservedNamespaces: core plus each registered id's namespace})`.
4. `buildCatalog({coreQuads, sources: [...registered, ...sources], links, coreNamespace, importStatus: rows})`. Granted sources must not change the build for a division without a provider or published granted sources: its output differs from today only by the new fields.

Move the existing override validation so registered-only behaviour and message are unchanged when no provider exists.

```js
import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { createFixtureDivision, ontologyText } from './fixtures/onboarding/fixture-division.mjs';
import { runCli } from '../src/cli.mjs';

const ns = (n) => `https://example.test/${n}#`;
const withDivision = (fn) => async () => { const d = createFixtureDivision(); try { await fn(d); } finally { d.cleanup(); } };
const files = ['catalog.json', 'catalog.md', 'combined.nq'];
const bytes = (d) => files.map((name) => d.dist(name));
const source = (d, id) => d.catalog().sources.find((s) => s.id === id);
const row = (d, id) => d.catalog().importStatus.find((r) => r.source === id);
const concepts = (d, id) => d.catalog().concepts.filter((c) => c.source === id).map((c) => c.label).sort();
async function joinSource(d, id = 'o/alpha', terms = ['Alpha']) { d.addSource(id, { namespace: ns(id.split('/')[1]), terms }); d.grant(id); await d.run('build'); return id; }
const alphaOntology = (terms) => ({ 'docs/ontology.ttl': ontologyText(ns('alpha'), terms) });

test('B1 granting a repository joins it without touching the division files', withDivision(async (d) => {
  const before = ['sources.json', 'division.json'].map((f) => readFileSync(join(d.root, f), 'utf8'));
  await joinSource(d);
  assert.equal(source(d, 'o/alpha').origin, 'granted');
  assert.deepEqual(['sources.json', 'division.json'].map((f) => readFileSync(join(d.root, f), 'utf8')), before);
}));
test('B3 a repository both registered and granted appears once, registered, with no status', withDivision(async (d) => {
  d.addSource('o/alpha', { namespace: ns('alpha') });
  d.register('o/alpha'); d.grant('o/alpha');
  await d.run('build');
  assert.deepEqual(d.catalog().sources.map((s) => [s.id, s.origin]), [['o/alpha', 'registered']]);
  assert.deepEqual(d.catalog().importStatus, []);
}));
test('B3 a published granted source later registered is built once, strictly', withDivision(async (d) => {
  await joinSource(d);
  d.register('o/alpha');
  await d.run('build');
  assert.deepEqual(d.catalog().sources.map((s) => [s.id, s.origin, s.stale]), [['o/alpha', 'registered', false]]);
  assert.deepEqual(d.catalog().importStatus, []);
}));
test('B4 a grant reaches only its own division', async () => {
  const a = createFixtureDivision(); const b = createFixtureDivision();
  try {
    a.addSource('o/alpha', {}); b.addSource('o/alpha', {});
    a.grant('o/alpha');
    await a.run('build'); await b.run('build');
    assert.equal(a.catalog().sources.length, 1);
    assert.deepEqual([b.catalog().sources, b.catalog().importStatus], [[], []]);
  } finally { a.cleanup(); b.cleanup(); }
});
test('B5 an all-repositories installation admits only configured repositories', withDivision(async (d) => {
  d.setSelection('all');
  d.addSource('o/with', {}); d.addSource('o/without', { config: null });
  d.grant('o/with'); d.grant('o/without');
  await d.run('build');
  assert.deepEqual(d.catalog().sources.map((s) => s.id), ['o/with']);
  assert.deepEqual(d.catalog().importStatus.map((r) => r.source), ['o/with']);
}));
test('B6 a valid new source appears with its exact commit and every concept', withDivision(async (d) => {
  await joinSource(d, 'o/alpha', ['A', 'B', 'C']);
  assert.equal(source(d, 'o/alpha').commit, d.head('o/alpha'));
  assert.equal(source(d, 'o/alpha').conceptCount, 3);
  assert.deepEqual(concepts(d, 'o/alpha'), ['A', 'B', 'C']);
  assert.equal(row(d, 'o/alpha').status, 'current');
}));
test('B8 a new valid version is imported without a renewed grant', withDivision(async (d) => {
  await joinSource(d);
  const commit = d.commitSource('o/alpha', alphaOntology(['Alpha', 'Beta']));
  await d.run('build');
  assert.deepEqual([source(d, 'o/alpha').commit, concepts(d, 'o/alpha')], [commit, ['Alpha', 'Beta']]);
}));
test('B9 a configuration change is followed', withDivision(async (d) => {
  await joinSource(d);
  d.commitSource('o/alpha', { 'docs/next.ttl': ontologyText(ns('alpha'), ['Moved']), 'docs/ontology.ttl': null,
    '.division-ontology.json': JSON.stringify({ schemaVersion: 1, ontologyPath: 'docs/next.ttl', namespace: ns('alpha') }) });
  await d.run('build');
  assert.deepEqual(concepts(d, 'o/alpha'), ['Moved']);
}));
test('B10 every granted source has a status; reasons carry no credentials', withDivision(async (d) => {
  await joinSource(d);
  assert.deepEqual(Object.keys(row(d, 'o/alpha')), ['source', 'status', 'attemptedCommit', 'publishedCommit']);
  const token = 'ghp_' + 'a1B2'.repeat(8);
  d.commitSource('o/alpha', { 'docs/ontology.ttl': ontologyText(`https://example.test/${token}#`, ['Thing']) });
  await d.run('build');
  assert.deepEqual(Object.keys(row(d, 'o/alpha')), ['source', 'status', 'attemptedCommit', 'publishedCommit', 'reason']);
  assert.equal(row(d, 'o/alpha').reason.code, 'concept-outside-namespace');
  assert.match(row(d, 'o/alpha').reason.detail, /\[redacted\]/);
  assert.doesNotMatch(JSON.stringify(d.catalog()) + d.dist('catalog.md'), /ghp_|x-access-token/);
}));
test('B11 an invalid new version keeps the last valid one, then recovers', withDivision(async (d) => {
  await joinSource(d);
  const good = d.head('o/alpha');
  const broken = d.commitSource('o/alpha', { 'docs/ontology.ttl': 'this is not turtle {' });
  await d.run('build');
  assert.deepEqual([row(d, 'o/alpha').status, row(d, 'o/alpha').attemptedCommit, row(d, 'o/alpha').publishedCommit], ['invalid', broken, good]);
  assert.deepEqual([source(d, 'o/alpha').stale, source(d, 'o/alpha').commit, concepts(d, 'o/alpha')], [true, good, ['Alpha']]);
  const fixed = d.commitSource('o/alpha', alphaOntology(['Alpha', 'Again']));
  await d.run('build');
  assert.deepEqual([row(d, 'o/alpha').status, source(d, 'o/alpha').stale, source(d, 'o/alpha').commit], ['current', false, fixed]);
}));
test('B12 an invalid first version is reported but not published', withDivision(async (d) => {
  d.addSource('o/alpha', { namespace: ns('alpha') });
  d.commitSource('o/alpha', { 'docs/ontology.ttl': 'this is not turtle {' });
  d.grant('o/alpha');
  await d.run('build');
  assert.equal(row(d, 'o/alpha').status, 'invalid');
  assert.equal(row(d, 'o/alpha').publishedCommit, null);
  assert.deepEqual([d.catalog().sources, concepts(d, 'o/alpha')], [[], []]);
}));
test('B13 a selected repository without configuration is reported', withDivision(async (d) => {
  d.addSource('o/alpha', { config: null }); d.grant('o/alpha');
  await d.run('build');
  assert.deepEqual([row(d, 'o/alpha').status, d.catalog().sources], ['not configured', []]);
}));
test('B14 removed access leaves the last version stale; regranting resumes', withDivision(async (d) => {
  await joinSource(d);
  d.revoke('o/alpha');
  await d.run('build');
  assert.deepEqual([row(d, 'o/alpha').status, row(d, 'o/alpha').publishedCommit, source(d, 'o/alpha').stale, concepts(d, 'o/alpha')], ['access removed', d.head('o/alpha'), true, ['Alpha']]);
  d.grant('o/alpha');
  const next = d.commitSource('o/alpha', alphaOntology(['Alpha', 'Back']));
  await d.run('build');
  assert.deepEqual([row(d, 'o/alpha').status, source(d, 'o/alpha').commit], ['current', next]);
}));
test('B14 a never-published source that loses access leaves the table', withDivision(async (d) => {
  d.addSource('o/alpha', { config: null }); d.grant('o/alpha');
  await d.run('build'); d.revoke('o/alpha'); await d.run('build');
  assert.deepEqual(d.catalog().importStatus, []);
}));
test('B15 one failing source does not hold back the others', withDivision(async (d) => {
  d.addSource('o/bad', { namespace: ns('bad') }); d.addSource('o/good', { namespace: ns('good') });
  d.commitSource('o/bad', { 'docs/ontology.ttl': 'this is not turtle {' });
  d.grant('o/bad'); d.grant('o/good');
  await d.run('build');
  assert.deepEqual([row(d, 'o/bad').status, row(d, 'o/good').status, source(d, 'o/good').conceptCount], ['invalid', 'current', 1]);
}));
test('B16 a conflicting namespace is rejected and the holder is unaffected', withDivision(async (d) => {
  d.addSource('o/reg', { namespace: ns('shared') }); d.register('o/reg');
  d.addSource('o/late', { namespace: ns('shared') }); d.grant('o/late');
  await d.run('build');
  assert.equal(row(d, 'o/late').reason.code, 'namespace-conflict');
  assert.deepEqual([source(d, 'o/reg').origin, concepts(d, 'o/reg')], ['registered', ['Thing']]);
}));
test('B17 unchanged inputs write identical bytes, including stale states', withDivision(async (d) => {
  await joinSource(d);
  const once = bytes(d); await d.run('build'); assert.deepEqual(bytes(d), once);
  d.commitSource('o/alpha', { 'docs/ontology.ttl': 'this is not turtle {' });
  await d.run('build');
  const stale = bytes(d); await d.run('build'); assert.deepEqual(bytes(d), stale);
  await d.run('check');
}));
test('a failed listing fails the run and leaves dist untouched, never marking access removed', withDivision(async (d) => {
  await joinSource(d);
  const before = bytes(d);
  d.failListing('GitHub API /installation/repositories: HTTP 503');
  await assert.rejects(d.run('build'), /HTTP 503/);
  assert.deepEqual(bytes(d), before);
}));
test('without a provider, a catalog that holds granted sources refuses to build', withDivision(async (d) => {
  await joinSource(d);
  await assert.rejects(runCli(['build', ...d.checkoutArgs()], { root: d.root, grantProvider: null }), /no source-read App/);
}));
test('a checkout override for an unknown repository is still rejected', withDivision(async (d) => {
  d.addSource('o/stray', {});
  await assert.rejects(runCli(['build', '--checkout', `o/stray=${d.overrides().get('o/stray')}`], { root: d.root, grantProvider: d.provider, urlFor: d.urlFor }), /o\/stray is not in sources\.json/);
}));
```

## Task 9 — refresh classification and PR body

**Files:** `src/refresh-policy.mjs`, `src/refresh-body.mjs`, `test/refresh-granted.test.mjs`, `test/refresh-body.test.mjs` (add one test).

**Interfaces:** in `sourceView`, use only sources whose `(origin ?? 'registered') === 'registered'`. `renderRefreshBody` adds, after the Source table and only when `catalog.importStatus?.length`, `### Import status` and a table `| Source | Status | Published commit | Reason |` (`| o/a | invalid | \`aaaaaaa…\` | code: detail |`, commit shortened to 12 characters, none as `—`).

```js
import test from 'node:test';
import assert from 'node:assert/strict';
import { createFixtureDivision, ontologyText } from './fixtures/onboarding/fixture-division.mjs';
import { classifyRefresh } from '../src/refresh-policy.mjs';

const ns = (n) => `https://example.test/${n}#`;
const changedPaths = ['dist/catalog.json', 'dist/catalog.md', 'dist/combined.nq'];
const take = (d) => ({ catalog: d.catalog(), nquads: d.dist('combined.nq') });
const classify = (before, after) => classifyRefresh({ previousCatalog: before.catalog, nextCatalog: after.catalog, previousNQuads: before.nquads, nextNQuads: after.nquads, changedPaths });
const withDivision = (fn) => async () => { const d = createFixtureDivision(); try { await fn(d); } finally { d.cleanup(); } };

test('B2 adding, updating and failing a granted source is automatic', withDivision(async (d) => {
  const empty = (await d.run('build'), take(d));
  d.addSource('o/a', { namespace: ns('a') }); d.grant('o/a');
  await d.run('build'); const added = take(d);
  assert.deepEqual(classify(empty, added), { mode: 'automatic', reasons: [] });
  d.commitSource('o/a', { 'docs/ontology.ttl': ontologyText(ns('a'), ['Thing', 'More']) });
  await d.run('build'); const updated = take(d);
  assert.equal(classify(added, updated).mode, 'automatic');
  d.commitSource('o/a', { 'docs/ontology.ttl': 'this is not turtle {' });
  await d.run('build');
  assert.equal(classify(updated, take(d)).mode, 'automatic');
  d.revoke('o/a'); const stale = take(d); await d.run('build');
  assert.equal(classify(stale, take(d)).mode, 'automatic');
}));
test('B3 adding a steward-registered source still needs review', withDivision(async (d) => {
  const empty = (await d.run('build'), take(d));
  d.addSource('o/r', { namespace: ns('r') }); d.register('o/r');
  await d.run('build');
  const result = classify(empty, take(d));
  assert.equal(result.mode, 'review');
  assert.ok(result.reasons.includes('source registration changed'));
}));
test('catalogs written before origin existed read as registered', withDivision(async (d) => {
  d.addSource('o/r', { namespace: ns('r') }); d.register('o/r');
  await d.run('build');
  const after = take(d);
  const legacy = structuredClone(after);
  for (const source of legacy.catalog.sources) { delete source.origin; delete source.stale; }
  delete legacy.catalog.importStatus;
  assert.deepEqual(classify(legacy, after), { mode: 'automatic', reasons: [] });
}));
```

Add to `test/refresh-body.test.mjs`:

```js
test('the refresh body lists import status when granted sources have it', () => {
  const body = renderRefreshBody({ catalog: { sources: [], importStatus: [
    { source: 'o/a', status: 'invalid', attemptedCommit: 'b'.repeat(40), publishedCommit: null, reason: { code: 'config-missing', detail: '.division-ontology.json' } }] },
    mode: 'automatic', reasons: [], fingerprint: '0'.repeat(12) });
  assert.match(body, /### Import status/);
  assert.match(body, /\| o\/a \| invalid \| — \| config-missing: \.division-ontology\.json \|/);
});
```

## Task 10 — verifier carries the base catalog

**Files:** `src/verify-refresh.mjs`, `test/verify-granted.test.mjs`.

**Interfaces:** `verifyRefresh({baseRoot, candidateDist, changedPaths, checkoutOverrides = new Map(), grantProvider, urlFor})`. Copy `baseRoot/dist` into the temporary root before the build (only if it exists), and pass `{root: temporary, grantProvider, urlFor}` to `runCli` (omit the keys when undefined so the environment default applies). Nothing else changes; `checkPr` in `src/merge-policy.mjs` calls it without these options, so the environment (`DIVISION_SOURCE_APP`, `GH_SOURCE_TOKEN`) governs.

```js
import test from 'node:test';
import assert from 'node:assert/strict';
import { writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { createFixtureDivision } from './fixtures/onboarding/fixture-division.mjs';
import { verifyRefresh } from '../src/verify-refresh.mjs';

const changedPaths = ['dist/catalog.json', 'dist/catalog.md', 'dist/combined.nq'];
const withDivision = (fn) => async () => { const d = createFixtureDivision(); try { await fn(d); } finally { d.cleanup(); } };

test('B2 a candidate that carries a stale granted source forward verifies as automatic', withDivision(async (d) => {
  d.addSource('o/a', {}); d.grant('o/a');
  await d.run('build');
  d.revoke('o/a');
  const candidate = d.cloneRoot();
  await d.run('build', { root: candidate });
  const result = await verifyRefresh({ baseRoot: d.root, candidateDist: join(candidate, 'dist'), changedPaths, grantProvider: d.provider, urlFor: d.urlFor });
  assert.deepEqual(result, { mode: 'automatic', reasons: [] });
}));
test('a tampered candidate no longer matches a fresh build', withDivision(async (d) => {
  d.addSource('o/a', {}); d.grant('o/a');
  await d.run('build');
  d.revoke('o/a');
  const candidate = d.cloneRoot();
  await d.run('build', { root: candidate });
  writeFileSync(join(candidate, 'dist/catalog.md'), 'tampered\n');
  await assert.rejects(verifyRefresh({ baseRoot: d.root, candidateDist: join(candidate, 'dist'), changedPaths, grantProvider: d.provider, urlFor: d.urlFor }), /does not match a fresh build/);
}));
test('a new grant verifies when the live installation matches', withDivision(async (d) => {
  await d.run('build');
  d.addSource('o/a', {}); d.grant('o/a');
  const candidate = d.cloneRoot();
  await d.run('build', { root: candidate });
  const result = await verifyRefresh({ baseRoot: d.root, candidateDist: join(candidate, 'dist'), changedPaths,
    checkoutOverrides: d.overrides(), grantProvider: d.provider, urlFor: d.urlFor });
  assert.equal(result.mode, 'automatic');
}));
```

(`cloneRoot` copies the whole division root, including its current `dist/`; `d.run('build', {root})` builds there.)

## Task 11 — C4 and workspace readers

**Files:** `src/c4/snapshot.mjs`, `test/c4-snapshot-granted.test.mjs`.

**Interfaces:** export `sourceInventory(catalog, manifest)->Map<id,{id,url}>`, used by `loadSnapshot` in place of the inline `configMap`. Rules: catalog source ids are unique; catalog sources with `origin !== 'granted'` have exactly the ids in `manifest.sources`; a granted id must not be in the manifest; a granted source's entry is `{id, url: \`https://github.com/${id}\`}`; any breach throws `catalog and source inventory mismatch`. The result keeps manifest entries for registered sources. `loadSnapshot` otherwise behaves as before, so existing `test/c4-*.test.mjs` stay green.

```js
import test from 'node:test';
import assert from 'node:assert/strict';
import { sourceInventory } from '../src/c4/snapshot.mjs';

const manifest = { sources: [{ id: 'o/reg', url: 'https://github.com/o/reg.git', ref: 'main', ontologyPath: 'x.ttl', namespace: 'https://x.test/#' }] };
const entry = (id, origin) => ({ id, commit: 'a'.repeat(40), namespace: 'https://x.test/', conceptCount: 1, ...(origin ? { origin } : {}) });

test('registered sources keep their manifest entry and granted sources get a derived GitHub URL', () => {
  const inventory = sourceInventory({ sources: [entry('o/reg', 'registered'), entry('o/grant', 'granted')] }, manifest);
  assert.equal(inventory.get('o/reg').url, 'https://github.com/o/reg.git');
  assert.deepEqual(inventory.get('o/grant'), { id: 'o/grant', url: 'https://github.com/o/grant' });
});
test('catalogs without origin read as registered', () => {
  assert.ok(sourceInventory({ sources: [entry('o/reg')] }, manifest).has('o/reg'));
});
test('mismatched inventories still fail closed', () => {
  assert.throws(() => sourceInventory({ sources: [] }, manifest), /mismatch/);
  assert.throws(() => sourceInventory({ sources: [entry('o/reg'), entry('o/other')] }, manifest), /mismatch/);
  assert.throws(() => sourceInventory({ sources: [entry('o/reg', 'granted')] }, manifest), /mismatch/);
  assert.throws(() => sourceInventory({ sources: [entry('o/reg'), entry('o/reg')] }, manifest), /mismatch/);
});
```

## Task 12 — workflows

**Files:** `.github/workflows/sync-catalog.yml`, `.github/workflows/merge-policy.yml`, `test/workflow.test.mjs`.

**Edits.** In both workflows, in the step that creates the source-read token (the first `actions/create-github-app-token` step in the sync workflow; `id: source-token` in merge policy), set `client-id: ${{ vars.DIVISION_SOURCE_APP_CLIENT_ID || vars.DIVISION_SYNC_APP_CLIENT_ID }}` and `private-key: ${{ secrets.DIVISION_SOURCE_APP_PRIVATE_KEY || secrets.DIVISION_SYNC_APP_PRIVATE_KEY }}`; keep `permission-contents: read` and no `repositories` key. In the sync workflow's `build` step and the merge-policy `Evaluate trusted policy` step add `DIVISION_SOURCE_APP: ${{ vars.DIVISION_SOURCE_APP_CLIENT_ID && 'enabled' || 'disabled' }}`. The writer token step, the `Check App setup` step and everything else stay as they are.

Replace the two workflow assertions in `test/workflow.test.mjs` with these additions (keep the other assertions in those tests):

```js
const flag = /DIVISION_SOURCE_APP_CLIENT_ID && 'enabled' \|\| 'disabled'/;
const readToken = (step) => {
  assert.equal(step.with['permission-contents'], 'read');
  assert.equal(step.with.repositories, undefined);
  assert.match(step.with['client-id'], /vars\.DIVISION_SOURCE_APP_CLIENT_ID \|\| vars\.DIVISION_SYNC_APP_CLIENT_ID/);
  assert.match(step.with['private-key'], /secrets\.DIVISION_SOURCE_APP_PRIVATE_KEY \|\| secrets\.DIVISION_SYNC_APP_PRIVATE_KEY/);
};
// sync test: readToken(appTokens[0]); assert.match(steps.find((s) => s.id === 'build').env.DIVISION_SOURCE_APP, flag);
//            assert.match(appTokens[1].with['client-id'], /DIVISION_SYNC_APP_CLIENT_ID/); assert.equal(appTokens[1].with.repositories, 'division-ontology');
// merge-policy test: readToken(token); assert.match(steps.find((s) => s.name === 'Evaluate trusted policy').env.DIVISION_SOURCE_APP, flag);
```

## Task 13 — setup, onboarding guidance and governance

**Files:** `docs/automation-setup.md`, `docs/Add-Repo-With-Internal-Ontology-To-Division-Ontology-Three-Step-Instructions.md`, `README.md`, `GOVERNANCE.md`, `AGENTS.md`, `test/init-division.test.mjs`, `test/onboarding-docs.test.mjs`.

**Content.**

- `docs/automation-setup.md`: rewrite section 1 as two Apps. The source-read App has **Contents: read** and **Metadata: read** only, is never installed on this repository, and is what source owners add repositories to; its client ID goes in `DIVISION_SOURCE_APP_CLIENT_ID` and its key in `DIVISION_SOURCE_APP_PRIVATE_KEY`. The division-writer App keeps `DIVISION_SYNC_APP_*`, with Contents and Pull requests write, installed only here. A single-App setup keeps working for steward-registered sources but cannot take grants. Say that discovery polls the installation on the normal sync (up to two hours, or run `Sync division catalog`), costs one request per installed repository per sync, fails the whole run on any GitHub error and never treats a failed listing as removed access, and that "All repositories" admits only repositories with `.division-ontology.json`. Add a verification step: add a test repository to the source-read App and dispatch the sync. Registered private sources must also be installed on whichever App reads them.
- Three-step document: add a first section "Fast path: grant the division's read-only App" — commit `.division-ontology.json` (with the three fields) to the default branch, add the repository to the division's source-read App, wait for or dispatch the sync, read the repository's row in `dist/catalog.md`. Say the steward path below remains for links, shared terms and sources that cannot be granted.
- `README.md`, `AGENTS.md`: add the owner action to "Minimal instructions for an agent" / "Short agent requests": for a source owner, write `.division-ontology.json` and ask the owner to add the repository to the division's source-read App; no PR here is needed and `sources.json` is untouched.
- `GOVERNANCE.md`, "Sources and links": a granted source joins without steward approval; it can never carry links or change core terms; a namespace already in use is rejected; steward registration still governs a repository that is also granted.

```js
// test/onboarding-docs.test.mjs
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
```

In `test/init-division.test.mjs`, in the first test after the `catalog.core.namespace` assertion add `assert.deepEqual(catalog.sources, []); assert.deepEqual(catalog.importStatus, []);`. This proves B7's template half: a fresh division builds with no sources and no status. After the build PR merges the scheduled sync PR regenerates `dist/`; before then `npm run check` reports only the new `origin`, `stale` and `importStatus` fields as stale.

## Build handoff

- Run `npm test` (all tests pass), then `npm run build` and `npm run check` against clean checkouts of every configured source; with the template that is none. Expect `npm run check` to report stale `dist/` only because of the new fields; do not stage `dist/`.
- Acceptance in a real division copy (B1, B4, B6, B14, and the organisation-policy assumption U12) belongs to the user phase.
- Offering `.division-ontology.json` from InternalOntology's installer is a separate change in that repository and is not part of this build.
