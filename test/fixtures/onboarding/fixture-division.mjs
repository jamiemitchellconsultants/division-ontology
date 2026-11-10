import { execFileSync } from 'node:child_process';
import { cpSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { dirname, join } from 'node:path';
import { runCli } from '../../../src/cli.mjs';

export const coreNamespace = 'https://example.test/division#';

export const ontologyText = (namespace, terms = ['Thing']) => [
  `@prefix : <${namespace}> . @prefix owl: <http://www.w3.org/2002/07/owl#> . @prefix rdfs: <http://www.w3.org/2000/01/rdf-schema#> .`,
  ...terms.map((term) => `:${term} a owl:Class; rdfs:label "${term}" .`),
  '',
].join('\n');

export const importConfig = (namespace, ontologyPath = 'docs/ontology.ttl') => JSON.stringify({ schemaVersion: 1, ontologyPath, namespace });

const git = (cwd, ...args) => execFileSync('git', args, { cwd, encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] }).trim();
const put = (path, text) => { mkdirSync(dirname(path), { recursive: true }); writeFileSync(path, text); };

function commitAll(dir, message = 'fixture') {
  git(dir, 'add', '-A');
  git(dir, '-c', 'user.name=Test', '-c', 'user.email=test@example.invalid', 'commit', '-q', '--allow-empty', '-m', message);
  return git(dir, 'rev-parse', 'HEAD');
}

export function createFixtureDivision() {
  const root = mkdtempSync(join(tmpdir(), 'division-onboarding-'));
  const clones = [];
  const repos = new Map();
  const granted = new Set();
  const registered = new Set();
  let selection = 'selected';
  let listingFailure = null;

  put(join(root, 'core/ontology.ttl'), ontologyText(coreNamespace, ['StaffId']));
  put(join(root, 'links/.gitkeep'), '');
  put(join(root, 'division.json'), JSON.stringify({ namespace: coreNamespace, stewards: ['steward-one'] }));
  const writeManifest = () => put(join(root, 'sources.json'), JSON.stringify({
    sources: [...registered].sort().map((id) => {
      const repo = repos.get(id);
      return { id, url: repo.dir, ref: repo.branch, ontologyPath: 'docs/ontology.ttl', namespace: repo.namespace };
    }),
  }));
  writeManifest();

  const provider = {
    async listInstallation() {
      if (listingFailure) throw new Error(listingFailure);
      return { selection, repositories: [...granted].sort().map((id) => ({ id, defaultBranch: repos.get(id).branch })) };
    },
    async hasImportConfig(id) {
      try { git(repos.get(id).dir, 'cat-file', '-e', 'HEAD:.division-ontology.json'); return true; } catch { return false; }
    },
  };

  const division = {
    root,
    provider,
    addSource(id, { namespace = `https://example.test/${id.replace('/', '-')}#`, terms = ['Thing'], config, branch = 'main' } = {}) {
      const dir = join(root, 'sources', id.replace('/', '__'));
      mkdirSync(dir, { recursive: true });
      git(dir, 'init', '-q', '-b', branch);
      git(dir, 'remote', 'add', 'origin', dir);
      put(join(dir, 'docs/ontology.ttl'), ontologyText(namespace, terms));
      const text = config === undefined ? importConfig(namespace) : config;
      if (text !== null) put(join(dir, '.division-ontology.json'), text);
      repos.set(id, { dir, branch, namespace });
      return { dir, commit: commitAll(dir) };
    },
    commitSource(id, files) {
      const { dir } = repos.get(id);
      for (const [path, text] of Object.entries(files)) {
        if (text === null) rmSync(join(dir, path), { force: true });
        else put(join(dir, path), text);
      }
      return commitAll(dir);
    },
    head: (id) => git(repos.get(id).dir, 'rev-parse', 'HEAD'),
    register(id) { registered.add(id); writeManifest(); },
    grant(id) { granted.add(id); },
    revoke(id) { granted.delete(id); },
    setSelection(value) { selection = value; },
    failListing(message) { listingFailure = message; },
    urlFor: (id) => repos.get(id).dir,
    overrides: () => new Map([...repos].map(([id, { dir }]) => [id, dir])),
    checkoutArgs: () => [...new Set([...registered, ...granted])].sort().flatMap((id) => ['--checkout', `${id}=${repos.get(id).dir}`]),
    run: (command, { root: target = root } = {}) => runCli([command, ...division.checkoutArgs()], { root: target, grantProvider: provider, urlFor: division.urlFor }),
    cloneRoot() {
      const copy = mkdtempSync(join(tmpdir(), 'division-onboarding-clone-'));
      cpSync(root, copy, { recursive: true });
      clones.push(copy);
      return copy;
    },
    catalog: (target = root) => JSON.parse(readFileSync(join(target, 'dist/catalog.json'), 'utf8')),
    dist: (name, target = root) => readFileSync(join(target, 'dist', name), 'utf8'),
    cleanup() { for (const path of [root, ...clones]) rmSync(path, { recursive: true, force: true }); },
  };
  return division;
}
