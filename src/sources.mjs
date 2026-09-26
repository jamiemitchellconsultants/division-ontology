import { execFileSync } from 'node:child_process';
import { mkdtempSync, readFileSync, realpathSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { isAbsolute, join, resolve } from 'node:path';

function git(args, cwd) {
  return execFileSync('git', args, { cwd, encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] }).trim();
}

function safeRelativePath(path) {
  return typeof path === 'string' && path.length > 0 && !isAbsolute(path) && !path.split(/[\\/]/).includes('..');
}

function sourceIdentity(url) {
  const ssh = /^git@([^:]+):(.+)$/.exec(url);
  const remote = ssh ? `https://${ssh[1]}/${ssh[2]}` : url;
  if (!/^https?:\/\//.test(remote) && !/^ssh:\/\//.test(remote)) return `file:${realpathSync(remote)}`;
  const parsed = new URL(remote);
  return `${parsed.hostname.toLowerCase()}/${parsed.pathname.replace(/^\/+|\/+$/g, '').replace(/\.git$/i, '')}`;
}

export function loadSources(manifestPath) {
  const manifest = JSON.parse(readFileSync(manifestPath, 'utf8'));
  if (!manifest || !Array.isArray(manifest.sources) || manifest.sources.length === 0) {
    throw new Error('sources.json requires a non-empty sources array');
  }
  const seen = new Set();
  for (const source of manifest.sources) {
    if (!source || typeof source.id !== 'string' || !/^[\w.-]+\/[\w.-]+$/.test(source.id)) throw new Error('source id must be owner/name');
    if (seen.has(source.id)) throw new Error(`duplicate source ${source.id}`);
    seen.add(source.id);
    if (typeof source.url !== 'string' || !source.url) throw new Error(`${source.id}: url is required`);
    const identity = sourceIdentity(source.url);
    if (identity.startsWith('github.com/') && identity.slice('github.com/'.length).toLowerCase() !== source.id.toLowerCase()) throw new Error(`${source.id}: url identifies a different GitHub repository`);
    if (typeof source.ref !== 'string' || !source.ref) throw new Error(`${source.id}: ref is required`);
    if (!safeRelativePath(source.ontologyPath)) throw new Error(`${source.id}: ontologyPath must stay inside the repository`);
    if (typeof source.namespace !== 'string' || !/^https?:\/\//.test(source.namespace)) throw new Error(`${source.id}: namespace must be an HTTP IRI`);
    if (source.linksPath !== undefined && !safeRelativePath(source.linksPath)) throw new Error(`${source.id}: linksPath must be relative`);
  }
  return manifest.sources;
}

export function resolveSource(source, checkoutOverrides = new Map()) {
  const override = checkoutOverrides.get(source.id);
  const temporary = !override;
  const directory = override ? resolve(override) : mkdtempSync(join(tmpdir(), 'division-ontology-'));
  try {
    if (temporary) git(['clone', '--quiet', '--depth', '1', '--branch', source.ref, source.url, directory], undefined);
    const root = git(['rev-parse', '--show-toplevel'], directory);
    if (realpathSync(root) !== realpathSync(directory)) throw new Error(`${source.id}: checkout path is not a repository root`);
    let origin;
    try { origin = git(['remote', 'get-url', 'origin'], directory); }
    catch { throw new Error(`${source.id}: checkout has no origin remote to verify source identity`); }
    if (sourceIdentity(origin) !== sourceIdentity(source.url)) throw new Error(`${source.id}: checkout origin does not match configured source url`);
    if (git(['status', '--porcelain', '--untracked-files=all'], directory)) throw new Error(`${source.id}: checkout has uncommitted changes`);
    const commit = git(['rev-parse', 'HEAD'], directory);
    return { directory, commit, cleanup: () => { if (temporary) rmSync(directory, { recursive: true, force: true }); } };
  } catch (error) {
    if (temporary) rmSync(directory, { recursive: true, force: true });
    throw error;
  }
}
