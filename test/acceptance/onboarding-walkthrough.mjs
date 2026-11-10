import { execFileSync } from 'node:child_process';
import { cpSync, existsSync, mkdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { initDivision } from '../../scripts/init-division.mjs';
import { runCli } from '../../src/cli.mjs';
import { classifyRefresh } from '../../src/refresh-policy.mjs';
import { importConfig, ontologyText } from '../fixtures/onboarding/fixture-division.mjs';

// A scripted stand-in for GitHub and for source owners, so the one-action onboarding walkthrough can be followed
// on one machine. Two divisions (A and B) are created from this template with the real init-division command;
// five source repositories are local Git repositories; the App installation is the file <division>/installation.json.
const repoRoot = fileURLToPath(new URL('../../', import.meta.url));
const templateFiles = ['core', 'links', 'sources.json', 'division.json', '.github/CODEOWNERS', 'narrative', 'Narrative.md', 'docs', 'dist'];
const distFiles = ['catalog.json', 'catalog.md', 'combined.nq'];
export const sourceIds = ['acceptance/alpha', 'acceptance/beta', 'acceptance/gamma', 'acceptance/delta', 'acceptance/epsilon'];
export const namespaceOf = (id) => `https://acceptance.test/${id.split('/')[1]}#`;
const nameOf = (id) => id.split('/')[1][0].toUpperCase() + id.split('/')[1].slice(1);
const git = (cwd, ...args) => execFileSync('git', args, { cwd, encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] }).trim();
const put = (path, text) => { mkdirSync(join(path, '..'), { recursive: true }); writeFileSync(path, text); };
export const sourceDir = (dir, id) => join(dir, 'sources', id.replace('/', '__'));
const divisionDir = (dir, division) => join(dir, division);
const installationPath = (dir, division) => join(divisionDir(dir, division), 'installation.json');
const readJson = (path) => JSON.parse(readFileSync(path, 'utf8'));

function commitAll(directory) {
  git(directory, 'add', '-A');
  git(directory, '-c', 'user.name=Acceptance', '-c', 'user.email=acceptance@example.invalid', 'commit', '-q', '--allow-empty', '-m', 'acceptance');
  return git(directory, 'rev-parse', 'HEAD');
}

export const head = (dir, id) => git(sourceDir(dir, id), 'rev-parse', 'HEAD');

export async function prepare(dir) {
  mkdirSync(dir, { recursive: true });
  for (const id of sourceIds) {
    const directory = sourceDir(dir, id);
    mkdirSync(directory, { recursive: true });
    git(directory, 'init', '-q', '-b', 'main');
    git(directory, 'remote', 'add', 'origin', directory);
    put(join(directory, 'docs/ontology.ttl'), id.endsWith('delta') ? 'this is not turtle {' : ontologyText(namespaceOf(id), [nameOf(id)]));
    if (!id.endsWith('gamma')) put(join(directory, '.division-ontology.json'), importConfig(namespaceOf(id)));
    commitAll(directory);
  }
  for (const division of ['A', 'B']) {
    const root = divisionDir(dir, division);
    for (const path of templateFiles) cpSync(join(repoRoot, path), join(root, path), { recursive: true });
    await initDivision(['--namespace', `https://acceptance.test/division-${division.toLowerCase()}#`, '--steward', 'acceptance-steward'], { root });
    writeFileSync(installationPath(dir, division), JSON.stringify({ selection: 'selected', granted: [] }, null, 2));
  }
}

function provider(dir, division) {
  const state = () => readJson(installationPath(dir, division));
  return {
    async listInstallation() { return { selection: state().selection, repositories: state().granted.slice().sort().map((id) => ({ id, defaultBranch: 'main' })) }; },
    async hasImportConfig(id) { try { git(sourceDir(dir, id), 'cat-file', '-e', 'HEAD:.division-ontology.json'); return true; } catch { return false; } },
  };
}

export const checkoutArgs = (dir, division) => {
  const registered = readJson(join(divisionDir(dir, division), 'sources.json')).sources.map((source) => source.id);
  return [...new Set([...registered, ...readJson(installationPath(dir, division)).granted])].sort().flatMap((id) => ['--checkout', `${id}=${sourceDir(dir, id)}`]);
};

export async function run(dir, division, command = 'build') {
  const root = divisionDir(dir, division);
  const previous = join(root, '.prev-dist');
  if (command === 'build' && existsSync(join(root, 'dist'))) { rmSync(previous, { recursive: true, force: true }); cpSync(join(root, 'dist'), previous, { recursive: true }); }
  return runCli([command, ...checkoutArgs(dir, division)], { root, grantProvider: provider(dir, division), urlFor: (id) => sourceDir(dir, id) });
}

export const catalog = (dir, division) => readJson(join(divisionDir(dir, division), 'dist/catalog.json'));
export const distBytes = (dir, division) => distFiles.map((name) => readFileSync(join(divisionDir(dir, division), 'dist', name), 'utf8'));

export function classify(dir, division) {
  const root = divisionDir(dir, division);
  const read = (base, name) => readFileSync(join(base, name), 'utf8');
  return classifyRefresh({
    previousCatalog: JSON.parse(read(join(root, '.prev-dist'), 'catalog.json')), nextCatalog: catalog(dir, division),
    previousNQuads: read(join(root, '.prev-dist'), 'combined.nq'), nextNQuads: read(join(root, 'dist'), 'combined.nq'),
    changedPaths: distFiles.map((name) => `dist/${name}`),
  });
}

export function act(dir, division, action, ...args) {
  const file = installationPath(dir, division);
  const state = readJson(file);
  const save = () => writeFileSync(file, JSON.stringify(state, null, 2));
  if (action === 'grant') { if (!state.granted.includes(args[0])) state.granted.push(args[0]); return save(); }
  if (action === 'revoke') { state.granted = state.granted.filter((id) => id !== args[0]); return save(); }
  if (action === 'selection') { state.selection = args[0]; return save(); }
  if (action === 'register') {
    const path = join(divisionDir(dir, division), 'sources.json');
    const manifest = readJson(path);
    manifest.sources.push({ id: args[0], url: sourceDir(dir, args[0]), ref: 'main', ontologyPath: 'docs/ontology.ttl', namespace: namespaceOf(args[0]) });
    return writeFileSync(path, `${JSON.stringify(manifest, null, 2)}\n`);
  }
  if (action === 'commit') return commitScenario(dir, args[0], args[1], args[2]);
  throw new Error(`unknown action ${action}`);
}

function commitScenario(dir, id, scenario, other) {
  const directory = sourceDir(dir, id);
  // Edits go to the ontology file the repository's configuration currently names, as an owner's edits would.
  const configured = () => { try { return readJson(join(directory, '.division-ontology.json')).ontologyPath; } catch { return 'docs/ontology.ttl'; } };
  const ontology = (terms, namespace = namespaceOf(id)) => put(join(directory, configured()), ontologyText(namespace, terms));
  if (scenario === 'new-version') ontology([nameOf(id), 'NewTerm']);
  else if (scenario === 'break') put(join(directory, configured()), 'this is not turtle {');
  else if (scenario === 'fix') ontology([nameOf(id), 'Fixed']);
  else if (scenario === 'remove-config') rmSync(join(directory, '.division-ontology.json'));
  else if (scenario === 'move-ontology') {
    rmSync(join(directory, 'docs/ontology.ttl'));
    put(join(directory, 'docs/moved.ttl'), ontologyText(namespaceOf(id), [nameOf(id), 'Moved']));
    put(join(directory, '.division-ontology.json'), importConfig(namespaceOf(id), 'docs/moved.ttl'));
  } else if (scenario === 'claim-namespace') {
    put(join(directory, '.division-ontology.json'), importConfig(namespaceOf(other)));
    ontology([nameOf(id)], namespaceOf(other));
  } else throw new Error(`unknown scenario ${scenario}`);
  return commitAll(directory);
}

export function show(dir, division) {
  const current = catalog(dir, division);
  const lines = [`Division ${division}`, '', 'Sources (repository, origin, stale, commit)'];
  for (const source of current.sources) lines.push(`  ${source.id}  ${source.origin}  stale=${source.stale}  ${source.commit.slice(0, 12)}`);
  if (!current.sources.length) lines.push('  (none)');
  lines.push('', 'Import status (repository, status, attempted, published, reason)');
  for (const row of current.importStatus) lines.push(`  ${row.source}  ${row.status}  ${row.attemptedCommit?.slice(0, 12) ?? '-'}  ${row.publishedCommit?.slice(0, 12) ?? '-'}  ${row.reason ? `${row.reason.code}: ${row.reason.detail}` : ''}`);
  if (!current.importStatus.length) lines.push('  (none)');
  lines.push('', 'Concepts');
  for (const source of ['division-core', ...current.sources.map((item) => item.id)]) lines.push(`  ${source}: ${current.concepts.filter((concept) => concept.source === source).map((concept) => concept.label).sort().join(', ') || '(none)'}`);
  return lines.join('\n');
}

export function teardown(dir) { rmSync(dir, { recursive: true, force: true }); }

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  const argv = process.argv.slice(2);
  const [command, dir, ...rest] = argv;
  const usage = 'usage: onboarding-walkthrough.mjs prepare|build|check|show|classify|grant|revoke|selection|register|commit|teardown <dir> [A|B] [args]';
  try {
    if (!command || !dir) throw new Error(usage);
    if (command === 'prepare') { await prepare(dir); console.log(`Prepared divisions A and B and ${sourceIds.length} sources in ${dir}`); }
    else if (command === 'teardown') { teardown(dir); console.log('Removed'); }
    else {
      const [division, ...args] = rest;
      if (!['A', 'B'].includes(division)) throw new Error(usage);
      if (command === 'build' || command === 'check') { const result = await run(dir, division, command); console.log(`${command} ok: ${result.sources} source(s), ${result.granted} granted`); }
      else if (command === 'show') console.log(show(dir, division));
      else if (command === 'classify') { const result = classify(dir, division); console.log(`Refresh classification: ${result.mode}${result.reasons.length ? ` (${result.reasons.join('; ')})` : ''}`); }
      else { await act(dir, division, command, ...args); console.log('done'); }
    }
  } catch (error) { console.error(error.message); process.exitCode = 1; }
}
