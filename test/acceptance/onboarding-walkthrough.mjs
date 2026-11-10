import { execFileSync } from 'node:child_process';
import { cpSync, existsSync, mkdirSync, mkdtempSync, readFileSync, rmSync, writeFileSync } from 'node:fs';
import { hostname, tmpdir } from 'node:os';
import { createInterface } from 'node:readline/promises';
import { createHash } from 'node:crypto';
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

// ---- Guided run: every walkthrough step in 05-acceptance.md, run for you, with a pass/fail prompt per step ----
const A = 'A', B = 'B';
const act_ = (division, ...args) => ['act', division, ...args];
const sha = (dir, division, files) => createHash('sha256').update(files.map((file) => readFileSync(join(divisionDir(dir, division), file))).join('\n')).digest('hex');
const divisionFiles = ['sources.json', 'division.json'];
const sortedDist = ['dist/catalog.json', 'dist/catalog.md', 'dist/combined.nq'];
const credentialPattern = /ghp_|ghs_|github_pat_|x-access-token/i;

export const steps = [
  { behaviours: 'B7', title: 'A new division starts empty', ops: [['build', A], ['show', A], ['fn', 'sources.json', (c) => readFileSync(join(divisionDir(c.dir, A), 'sources.json'), 'utf8').trim()]],
    expect: ['Sources and Import status both say (none)', 'sources.json holds an empty sources array'] },
  { behaviours: 'B1', title: 'Granting joins a source without touching the division', ops: [['fn', 'division files before', (c) => { c.before = sha(c.dir, A, divisionFiles); return c.before.slice(0, 12); }], act_(A, 'grant', 'acceptance/alpha'), ['build', A], ['show', A],
    ['fn', 'division files after', (c) => `${sha(c.dir, A, divisionFiles).slice(0, 12)} (${sha(c.dir, A, divisionFiles) === c.before ? 'unchanged' : 'CHANGED'})`]],
    expect: ['acceptance/alpha is listed as granted and current', 'the division files hash is unchanged'] },
  { behaviours: 'B6', title: 'Exact head commit and every concept', ops: [['show', A], ['fn', 'alpha repository head', (c) => head(c.dir, 'acceptance/alpha').slice(0, 12)]],
    expect: ['alpha is listed at exactly the head commit above', 'status current with the same attempted and published commit', 'concepts for acceptance/alpha: Alpha'] },
  { behaviours: 'B4', title: 'A grant reaches exactly one division', ops: [['build', B], ['show', B]], expect: ['division B has no sources and no import status'] },
  { behaviours: 'B5', title: 'An all-repositories installation admits only configured repositories', ops: [act_(A, 'selection', 'all'), act_(A, 'grant', 'acceptance/gamma'), ['build', A], ['show', A]],
    expect: ['acceptance/gamma (no .division-ontology.json) appears nowhere', 'alpha is still current'] },
  { behaviours: 'B13', title: 'A selected repository without configuration is reported', ops: [act_(A, 'selection', 'selected'), ['build', A], ['show', A]],
    expect: ['acceptance/gamma has status not configured with reason config-missing', 'gamma is not under Sources and has no concepts'] },
  { behaviours: 'B8, B9', title: 'A new version needs no renewed grant, and a configuration change is followed', ops: [act_(A, 'commit', 'acceptance/alpha', 'new-version'), ['build', A], ['show', A], act_(A, 'commit', 'acceptance/alpha', 'move-ontology'), ['build', A], ['show', A]],
    expect: ['after the first build alpha is current at the new head with concepts Alpha, NewTerm', 'after the second build alpha is current with concepts Alpha, Moved'] },
  { behaviours: 'B11', title: 'An invalid version keeps the last valid one, then recovers', ops: [['fn', 'last valid head', (c) => head(c.dir, 'acceptance/alpha').slice(0, 12)], act_(A, 'commit', 'acceptance/alpha', 'break'), ['build', A], ['show', A], act_(A, 'commit', 'acceptance/alpha', 'fix'), ['build', A], ['show', A]],
    expect: ['after the break alpha is invalid, attempted is the new commit, published is the last valid head above, stale=true, reason ontology-invalid-turtle with a line, concepts still Alpha, Moved', 'after the fix alpha is current, stale=false, concepts Alpha, Fixed'] },
  { behaviours: 'B12, B15, B10', title: 'An invalid first version is not published, holds nothing back, and no reason holds a credential', ops: [act_(A, 'grant', 'acceptance/beta'), act_(A, 'grant', 'acceptance/delta'), ['build', A], ['show', A],
    ['fn', 'credential-shaped text in the published files', (c) => ['catalog.json', 'catalog.md'].map((name) => `${name}: ${credentialPattern.test(readFileSync(join(divisionDir(c.dir, A), 'dist', name), 'utf8')) ? 'FOUND' : 'none'}`).join(', ')]],
    expect: ['beta is current and listed under Sources', 'delta is invalid with published - and is not under Sources or Concepts', 'alpha is unchanged', 'every granted source has a status row; credential-shaped text: none in both files'] },
  { behaviours: 'B16', title: 'A conflicting namespace is rejected', ops: [act_(A, 'commit', 'acceptance/beta', 'claim-namespace', 'acceptance/alpha'), ['build', A], ['show', A]],
    expect: ['beta is invalid with reason namespace-conflict: overlaps acceptance/alpha, stale at its previous commit', 'alpha stays current'] },
  { behaviours: 'B14', title: 'Removed access leaves the last version stale; granting again resumes', ops: [act_(A, 'revoke', 'acceptance/alpha'), act_(A, 'revoke', 'acceptance/delta'), ['build', A], ['show', A], act_(A, 'grant', 'acceptance/alpha'), act_(A, 'commit', 'acceptance/alpha', 'new-version'), ['build', A], ['show', A]],
    expect: ['after revoking, alpha is access removed with its last head published, stale=true, concepts Alpha, Fixed still present', 'delta has no row (never published)', 'after granting again alpha is current at the new head'] },
  { behaviours: 'B2, B3', title: 'Granted-only refreshes are automatic; registration still needs review', ops: [['classify', A], act_(A, 'register', 'acceptance/epsilon'), act_(A, 'grant', 'acceptance/epsilon'), ['build', A], ['show', A], ['classify', A]],
    expect: ['the first classification is automatic', 'epsilon is listed once, as registered, with no import status row', 'the second classification is review (source registration changed)'] },
  { behaviours: 'B17', title: 'Unchanged inputs produce no refresh', ops: [['fn', 'dist hash before', (c) => { c.dist = sha(c.dir, A, sortedDist); return c.dist.slice(0, 12); }], ['build', A], ['fn', 'dist hash after', (c) => `${sha(c.dir, A, sortedDist).slice(0, 12)} (${sha(c.dir, A, sortedDist) === c.dist ? 'identical' : 'DIFFERENT'})`], ['check', A]],
    expect: ['the two hashes are identical', 'check ok'] },
];

async function execute(op, context) {
  const [kind, ...rest] = op;
  if (kind === 'act') { const [division, ...args] = rest; const result = await act(context.dir, division, ...args); return `${args.join(' ')}${typeof result === 'string' ? ` -> ${result.slice(0, 12)}` : ''}`; }
  if (kind === 'build' || kind === 'check') { const result = await run(context.dir, rest[0], kind); return `${kind} division ${rest[0]}: ${result.sources} source(s), ${result.granted} granted`; }
  if (kind === 'show') return show(context.dir, rest[0]);
  if (kind === 'classify') { const result = classify(context.dir, rest[0]); return `Refresh classification: ${result.mode}${result.reasons.length ? ` (${result.reasons.join('; ')})` : ''}`; }
  if (kind === 'fn') return `${rest[0]}: ${await rest[1](context)}`;
  throw new Error(`unknown operation ${kind}`);
}

export async function guided({ name = 'unnamed', yes = false, out } = {}) {
  const parent = mkdtempSync(join(tmpdir(), 'division-onboarding-guided-'));
  const dir = join(parent, 'w');
  const context = { dir };
  const results = [];
  const rl = yes ? null : createInterface({ input: process.stdin, output: process.stdout });
  try {
    if (!yes && !process.stdin.isTTY) throw new Error('run this in a terminal so you can answer pass/fail, or pass --yes to rehearse without prompts');
    console.log('Preparing divisions A and B and five source repositories (this takes a few seconds)...');
    await prepare(dir);
    for (const [index, step] of steps.entries()) {
      console.log(`\n=== Step ${index + 1} of ${steps.length} — ${step.behaviours}: ${step.title} ===`);
      let failure = null;
      try { for (const op of step.ops) console.log(`\n${(await execute(op, context)).split('\n').map((line) => `  ${line}`).join('\n').trimStart()}`); }
      catch (error) { failure = error.message; console.log(`\n  ERROR: ${failure}`); }
      console.log('\nExpect:'); for (const line of step.expect) console.log(`  - ${line}`);
      let answer = 'y';
      if (failure) answer = 'n';
      else if (!yes) { do { answer = (await rl.question('\nPass? [y]es / [n]o / [q]uit: ')).trim().toLowerCase(); } while (!['y', 'n', 'q'].includes(answer)); }
      if (answer === 'q') { console.log('Stopped; steps not reached are not recorded.'); break; }
      results.push({ behaviours: step.behaviours, title: step.title, pass: answer === 'y', note: failure ? `error: ${failure}` : '' });
      if (failure) { console.log('A step failed with an error, so the story cannot continue. Raise it as pushback to the build.'); break; }
    }
  } finally { rl?.close(); teardown(parent); }
  const revision = (() => { try { return git(repoRoot, 'rev-parse', '--short', 'HEAD'); } catch { return 'unavailable'; } })();
  const dirty = (() => { try { return git(repoRoot, 'status', '--porcelain') ? ' (working tree not clean)' : ''; } catch { return ''; } })();
  const report = [`Local walkthrough — ${name}, ${new Date().toISOString().slice(0, 10)}, ${hostname()}, Node ${process.version}, revision ${revision}${dirty}${yes ? ' — REHEARSAL, prompts skipped, not an acceptance result' : ''}`, '',
    '| Step | Behaviours | Result |', '| --- | --- | --- |', ...results.map((item, index) => `| ${index + 1} ${item.title} | ${item.behaviours} | ${item.pass ? 'pass' : 'FAIL'}${item.note ? ` (${item.note})` : ''} |`), '',
    results.length === steps.length && results.every((item) => item.pass) ? `All ${steps.length} steps passed.` : `Not complete: ${results.filter((item) => item.pass).length} of ${steps.length} steps passed.`].join('\n');
  console.log(`\n${report}`);
  if (out) writeFileSync(out, `${report}\n`);
  return { results, complete: results.length === steps.length && results.every((item) => item.pass) };
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  const argv = process.argv.slice(2);
  if (argv[0] === 'guided') {
    const option = (flag) => (argv.includes(flag) ? argv[argv.indexOf(flag) + 1] : undefined);
    const result = await guided({ name: option('--name'), yes: argv.includes('--yes'), out: option('--out') }).catch((error) => { console.error(error.message); return { complete: false }; });
    process.exit(result.complete ? 0 : 1);
  }
  const [command, dir, ...rest] = argv;
  const usage = 'usage: onboarding-walkthrough.mjs guided [--name N] [--yes] [--out file] | prepare|build|check|show|classify|grant|revoke|selection|register|commit|teardown <dir> [A|B] [args]';
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
