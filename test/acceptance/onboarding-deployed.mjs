import { execFileSync } from 'node:child_process';
import { writeFileSync } from 'node:fs';
import { hostname } from 'node:os';
import { createInterface } from 'node:readline/promises';
import { pathToFileURL } from 'node:url';

// Read-only check of a deployed division's published catalog against the live source repositories.
// usage: node test/acceptance/onboarding-deployed.mjs <owner/division> [--ref main] [--sync] <owner/source>=<status> ...
//    or: node test/acceptance/onboarding-deployed.mjs run <owner/division> --alpha <owner/repo> --beta <owner/repo> --gamma <owner/repo> [--installation <id>] [--manual-app] [--dry-run] [--name N] [--out file]
// --manual-app: commits and the sync are automatic, but you add or remove repositories on the source-read App yourself
// (changing an installation through the API needs a classic personal access token). --sync dispatches the division's sync first and waits for it and its refresh PR. run performs every deployed step through gh,
// compares the published catalog with the expected result after each, and stops at the first exception.
// Status is current, invalid, "not configured", "access removed", registered (a steward-registered source with no row)
// or absent (no row, not in the catalog).
// Quote a status with spaces, for example 'owner/source=not configured'.

export function checkDeployed({ catalog, heads, expectations }) {
  const problems = [];
  for (const { source, status } of expectations) {
    const row = catalog.importStatus?.find((item) => item.source === source);
    const entry = catalog.sources?.find((item) => item.id === source);
    if (status === 'absent') {
      if (row || entry) problems.push(`${source}: expected absent, found ${row ? `row ${row.status}` : 'a catalog source'}`);
      continue;
    }
    if (status === 'registered') {
      if (row || entry?.origin !== 'registered') problems.push(`${source}: expected one registered catalog source with no status row, found ${row ? `row ${row.status}` : entry ? `origin ${entry.origin}` : 'nothing'}`);
      continue;
    }
    if (!row) { problems.push(`${source}: no import status row (expected ${status})`); continue; }
    if (row.status !== status) { problems.push(`${source}: expected ${status}, found ${row.status}${row.reason ? ` (${row.reason.code})` : ''}`); continue; }
    if (status === 'current') {
      if (row.publishedCommit !== heads[source] || row.attemptedCommit !== heads[source]) problems.push(`${source}: expected commit ${heads[source]?.slice(0, 12)}, found attempted ${row.attemptedCommit?.slice(0, 12)} published ${row.publishedCommit?.slice(0, 12)}`);
      if (!entry || entry.origin !== 'granted' || entry.stale || entry.commit !== heads[source]) problems.push(`${source}: expected a fresh granted catalog source at the head commit`);
    } else if (row.publishedCommit && (!entry || !entry.stale || entry.commit !== row.publishedCommit)) {
      problems.push(`${source}: expected a stale catalog source at the last published commit`);
    } else if (!row.publishedCommit && entry) problems.push(`${source}: expected no catalog source for a never-published repository`);
  }
  return problems;
}

const gh = (...args) => execFileSync('gh', args, { encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] });
const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));
const parse = (pair) => { const at = pair.lastIndexOf('='); return { source: pair.slice(0, at), status: pair.slice(at + 1) }; };

export const readCatalog = (division, ref = 'main') => JSON.parse(gh('api', '-H', 'Accept: application/vnd.github.raw', `repos/${division}/contents/dist/catalog.json?ref=${encodeURIComponent(ref)}`));

export function liveHeads(expectations) {
  const heads = {};
  for (const { source, status } of expectations) {
    if (status !== 'current') continue;
    const branch = gh('api', `repos/${source}`, '--jq', '.default_branch').trim();
    heads[source] = gh('api', `repos/${source}/commits/${encodeURIComponent(branch)}`, '--jq', '.sha').trim();
  }
  return heads;
}

const openRefreshPr = (division) => JSON.parse(gh('pr', 'list', '--repo', division, '--head', 'automation/catalog-refresh', '--state', 'open', '--json', 'number'))[0]?.number ?? null;
// The latest merged refresh PR must have merged with no review at all, which is what an automatic classification means.
const lastRefreshMergedWithoutReview = (division) => {
  const [pr] = JSON.parse(gh('pr', 'list', '--repo', division, '--head', 'automation/catalog-refresh', '--state', 'merged', '--limit', '1', '--json', 'number,reviews'));
  if (!pr) return 'no merged refresh PR found';
  return pr.reviews.length ? `refresh PR #${pr.number} has ${pr.reviews.length} review(s), so it was not merged automatically` : null;
};
const mainHead = (division) => gh('api', `repos/${division}/commits/main`, '--jq', '.sha').trim();

// Dispatch the division's sync, wait for the run, then wait for its generated refresh PR to merge.
export async function dispatchAndWait(division, { log = console.log, prTimeoutMs = 12 * 60 * 1000 } = {}) {
  const latest = () => JSON.parse(gh('run', 'list', '--repo', division, '--workflow', 'Sync division catalog', '--limit', '1', '--json', 'databaseId'))[0]?.databaseId ?? 0;
  const before = latest();
  gh('workflow', 'run', 'Sync division catalog', '--repo', division);
  let runId = before;
  for (let waited = 0; runId === before && waited < 90000; waited += 3000) { await sleep(3000); runId = latest(); }
  if (runId === before) throw new Error('the dispatched sync run did not appear within 90 seconds');
  log(`  sync run ${runId} started; waiting for it to finish...`);
  try { gh('run', 'watch', String(runId), '--repo', division, '--exit-status'); } catch { throw new Error(`sync run ${runId} failed; see https://github.com/${division}/actions/runs/${runId}`); }
  log('  sync run finished; waiting for the refresh PR to merge (if one was opened)...');
  for (let waited = 0; waited < prTimeoutMs; waited += 15000) {
    const pr = openRefreshPr(division);
    if (!pr) return { runId, pr: null };
    await sleep(15000);
  }
  return { runId, pr: openRefreshPr(division), timedOut: true };
}

// ---- GitHub actions the run performs itself when given --installation (all through gh, as the signed-in user) ----
const enc = encodeURIComponent;
const defaultBranch = (repo) => gh('api', `repos/${repo}`, '--jq', '.default_branch').trim();
const repoId = (repo) => gh('api', `repos/${repo}`, '--jq', '.id').trim();
const fileSha = (repo, branch, path) => { try { return JSON.parse(gh('api', `repos/${repo}/contents/${path}?ref=${enc(branch)}`)).sha; } catch { return null; } };
function putFile(repo, path, text, message) {
  const branch = defaultBranch(repo);
  const sha = fileSha(repo, branch, path);
  gh('api', '-X', 'PUT', `repos/${repo}/contents/${path}`, '-f', `message=${message}`, '-f', `content=${Buffer.from(text).toString('base64')}`, '-f', `branch=${branch}`, ...(sha ? ['-f', `sha=${sha}`] : []));
}
function deleteFile(repo, path, message) {
  const branch = defaultBranch(repo);
  const sha = fileSha(repo, branch, path);
  if (sha) gh('api', '-X', 'DELETE', `repos/${repo}/contents/${path}`, '-f', `message=${message}`, '-f', `sha=${sha}`, '-f', `branch=${branch}`);
}
const grant = (installation, repo) => gh('api', '-X', 'PUT', `user/installations/${installation}/repositories/${repoId(repo)}`);
const revoke = (installation, repo) => gh('api', '-X', 'DELETE', `user/installations/${installation}/repositories/${repoId(repo)}`);

const namespaceOf = (repo) => `https://acceptance.test/${repo.split('/')[1]}#`;
const ontology = (repo, terms) => `@prefix : <${namespaceOf(repo)}> . @prefix owl: <http://www.w3.org/2002/07/owl#> . @prefix rdfs: <http://www.w3.org/2000/01/rdf-schema#> .\n${terms.map((term) => `:${term} a owl:Class; rdfs:label "${term}" .`).join('\n')}\n`;
const config = (namespace, ontologyPath = 'docs/ontology.ttl') => JSON.stringify({ schemaVersion: 1, ontologyPath, namespace });
const invalidTurtle = 'this is not turtle {\n';

// Steps run in order. `actions` are what the run does for you; `manual` is what you do instead when --installation is not given.
export function deployedSteps({ alpha, beta, gamma, installation }) {
  const a = (status) => ({ source: alpha, status });
  const act = (describe, run, options = {}) => ({ describe, app: Boolean(options.app), run: () => run(installation) });
  const commit = (repo, path, text) => act(`commit ${path} to ${repo}`, () => putFile(repo, path, text, 'acceptance: onboarding'));
  return [
    { id: 'D1', behaviours: 'B7', manual: 'Confirm the new division has not been given any source yet.', expect: ['dist/catalog.json has empty sources and importStatus arrays'], empty: true, actions: [] },
    { id: 'D2', behaviours: 'B1, B6, B2', manual: `As the owner of ${alpha}: commit .division-ontology.json and a valid ontology, then add ${alpha} to the source-read App.`, expect: [`${alpha} is current at its default-branch head`, 'the generated-only refresh PR merged with no review at all'], autoMerged: true,
      actions: [commit(alpha, '.division-ontology.json', config(namespaceOf(alpha))), commit(alpha, 'docs/ontology.ttl', ontology(alpha, ['Alpha'])), act(`add ${alpha} to the source-read App`, (i) => grant(i, alpha), { app: true })], expectations: [a('current')], sync: true },
    { id: 'D3', behaviours: 'B13', manual: `Add ${gamma} (no .division-ontology.json) to the source-read App.`, expect: [`${gamma} is not configured`, `${alpha} is still current`],
      actions: [act(`remove .division-ontology.json from ${gamma} if present`, () => deleteFile(gamma, '.division-ontology.json', 'acceptance: onboarding')), act(`add ${gamma} to the source-read App`, (i) => grant(i, gamma), { app: true })], expectations: [a('current'), { source: gamma, status: 'not configured' }], sync: true },
    { id: 'D4', behaviours: 'B8', manual: `Commit a new term to ${alpha}.`, expect: [`${alpha} is current at the new head`], actions: [commit(alpha, 'docs/ontology.ttl', ontology(alpha, ['Alpha', 'NewTerm']))], expectations: [a('current')], sync: true },
    { id: 'D5', behaviours: 'B9', manual: `Move ${alpha}'s ontology file and update its .division-ontology.json in the same commit.`, expect: [`${alpha} is current at the new head`],
      actions: [commit(alpha, 'docs/moved.ttl', ontology(alpha, ['Alpha', 'Moved'])), commit(alpha, '.division-ontology.json', config(namespaceOf(alpha), 'docs/moved.ttl')), act(`delete docs/ontology.ttl from ${alpha}`, () => deleteFile(alpha, 'docs/ontology.ttl', 'acceptance: onboarding'))], expectations: [a('current')], sync: true },
    { id: 'D6', behaviours: 'B11, B10', manual: `Commit invalid Turtle to ${alpha}'s ontology.`, expect: [`${alpha} is invalid with its last valid version still published and stale`], actions: [commit(alpha, 'docs/moved.ttl', invalidTurtle)], expectations: [a('invalid')], sync: true },
    { id: 'D7', behaviours: 'B11', manual: `Commit a fix to ${alpha}'s ontology.`, expect: [`${alpha} is current at the fixing commit`], actions: [commit(alpha, 'docs/moved.ttl', ontology(alpha, ['Alpha', 'Fixed']))], expectations: [a('current')], sync: true },
    { id: 'D8', behaviours: 'B16, B12, B15', manual: `Commit a configuration and ontology for ${beta} that use ${alpha}'s namespace, then add ${beta} to the App.`, expect: [`${beta} is invalid and not published`, `${alpha} is still current in the same sync`],
      actions: [commit(beta, '.division-ontology.json', config(namespaceOf(alpha))), commit(beta, 'docs/ontology.ttl', `@prefix : <${namespaceOf(alpha)}> . @prefix owl: <http://www.w3.org/2002/07/owl#> . @prefix rdfs: <http://www.w3.org/2000/01/rdf-schema#> .\n:BetaClaim a owl:Class; rdfs:label "BetaClaim" .\n`), act(`add ${beta} to the source-read App`, (i) => grant(i, beta), { app: true })], expectations: [a('current'), { source: beta, status: 'invalid' }], sync: true },
    { id: 'D9', behaviours: 'B14', manual: `Remove ${alpha} from the source-read App.`, expect: [`${alpha} is access removed and stays in the catalog, stale`], actions: [act(`remove ${alpha} from the source-read App`, (i) => revoke(i, alpha), { app: true })], expectations: [a('access removed')], sync: true },
    { id: 'D10', behaviours: 'B14', manual: `Add ${alpha} back to the source-read App.`, expect: [`${alpha} is current again`], actions: [act(`add ${alpha} to the source-read App`, (i) => grant(i, alpha), { app: true })], expectations: [a('current')], sync: true },
    { id: 'D11', behaviours: 'B17', manual: 'Make no changes.', expect: ['two syncs in a row leave main unchanged and open no refresh PR'], actions: [], expectations: [a('current')], repeat: true },
  ];
}

// Behaviours that need something this script cannot do for you, each with a single comparing command to run afterwards.
export const optionalChecks = ({ division, alpha, gamma }) => [
  { behaviours: 'B3', needs: `a steward-approved, merged PR that registers ${alpha} in sources.json while it is also in the App`, command: `node test/acceptance/onboarding-deployed.mjs --sync ${division} ${alpha}=registered` },
  { behaviours: 'B4', needs: `a second division with its own App that was never given ${alpha}`, command: `node test/acceptance/onboarding-deployed.mjs --sync <owner/second-division> ${alpha}=absent` },
  { behaviours: 'B5', needs: 'the source-read App installed on all repositories of the test organisation (only if acceptable there)', command: `node test/acceptance/onboarding-deployed.mjs --sync ${division} ${gamma}=absent` },
];

// Runs every step, compares the published catalog with the expected result, and stops at the first exception.
export async function runDeployed({ division, alpha, beta, gamma, installation, manualApp = false, dryRun = false, name = 'unnamed', out, log = console.log }) {
  const steps = deployedSteps({ alpha, beta, gamma, installation });
  const results = [];
  let failed = null;
  const doApp = Boolean(installation) && !manualApp;
  const rl = (!installation || manualApp) && !dryRun ? createInterface({ input: process.stdin, output: process.stdout }) : null;
  try {
    if (rl && !process.stdin.isTTY) throw new Error('some GitHub actions are manual in this mode, so run this in a terminal');
    for (const step of steps) {
      log(`\n=== ${step.id} — ${step.behaviours} ===`);
      if (dryRun) {
        for (const action of step.actions) log(`  would: ${action.app && manualApp ? 'ask you to do in your browser: ' : ''}${action.describe}`);
        if (!step.actions.length) log(`  would: nothing to change (${step.manual})`);
        log(`  would: ${step.repeat ? 'dispatch the sync twice' : step.sync ? 'dispatch the sync and wait for its refresh PR' : 'read the catalog'}, then require: ${step.expect.join('; ')}`);
        continue;
      }
      const problems = [];
      try {
        if (step.actions.length) {
          if (installation || manualApp) {
            for (const action of step.actions) {
              if (action.app && !doApp) { log(`  Do this now in your browser: ${action.describe}`); await rl.question('  Press Enter when done: '); }
              else { log(`  ${action.describe}`); action.run(); }
            }
          } else { log(`  Do this now: ${step.manual}`); await rl.question('  Press Enter when done: '); }
        }
        if (step.repeat) {
          const before = mainHead(division);
          await dispatchAndWait(division, { log }); await dispatchAndWait(division, { log });
          if (mainHead(division) !== before) problems.push('main moved after two syncs with no changes');
          if (openRefreshPr(division)) problems.push('a refresh PR is open after two syncs with no changes');
        } else if (step.sync) {
          const sync = await dispatchAndWait(division, { log });
          if (sync.pr) problems.push(`refresh PR #${sync.pr} is still open (classified for review, or checks pending)`);
        }
        if (step.autoMerged) { const problem = lastRefreshMergedWithoutReview(division); if (problem) problems.push(problem); }
        if (step.empty) { const catalog = readCatalog(division); if (catalog.sources.length || catalog.importStatus?.length) problems.push('expected an empty catalog, but it already holds sources or status; run against a fresh division'); }
        if (step.expectations) problems.push(...checkDeployed({ catalog: readCatalog(division), heads: liveHeads(step.expectations), expectations: step.expectations }));
      } catch (error) { problems.push(error.message); }
      results.push({ id: step.id, behaviours: step.behaviours, pass: !problems.length });
      if (problems.length) {
        failed = { step, problems };
        log(`\nEXCEPTION at ${step.id}. Expected: ${step.expect.join('; ')}\n${problems.map((line) => `  - ${line}`).join('\n')}`);
        log(`Not run: ${steps.slice(steps.indexOf(step) + 1).map((item) => item.id).join(', ') || 'none'}. Raise this as pushback to the build.`);
        break;
      }
      log(`  pass: ${step.expect.join('; ')}`);
    }
  } finally { rl?.close(); }
  const report = dryRun ? null : [`Deployed run — ${name}, ${new Date().toISOString().slice(0, 10)}, ${hostname()}, division ${division}, alpha ${alpha}, beta ${beta}, gamma ${gamma}`, '',
    '| Step | Behaviours | Result |', '| --- | --- | --- |', ...results.map((item) => `| ${item.id} | ${item.behaviours} | ${item.pass ? 'pass' : 'FAIL'} |`), '',
    failed ? 'Not complete: see the exception above.' : `PASS — ${results.length} steps covering ${[...new Set(steps.flatMap((step) => step.behaviours.split(',').map((item) => item.trim())))].join(', ')}.`,
    '', 'Still to check separately (one comparing command each):', ...optionalChecks({ division, alpha, gamma }).map((check) => `- ${check.behaviours}, once you have ${check.needs}: ${check.command}`)].join('\n');
  if (report) { log(`\n${report}`); if (out) writeFileSync(out, `${report}\n`); }
  return { results, failed };
}

async function main(argv) {
  const option = (flag) => (argv.includes(flag) ? argv.splice(argv.indexOf(flag), 2)[1] : undefined);
  const flag = (name) => argv.includes(name) && argv.splice(argv.indexOf(name), 1).length > 0;
  if (argv[0] === 'run') {
    argv.shift();
    const dryRun = flag('--dry-run');
    const manualApp = flag('--manual-app');
    const [alpha, beta, gamma, installation, name, out] = [option('--alpha'), option('--beta'), option('--gamma'), option('--installation'), option('--name'), option('--out')];
    const [division] = argv;
    if (!division || !alpha || !beta || !gamma) throw new Error('usage: onboarding-deployed.mjs run <owner/division> --alpha <owner/repo> --beta <owner/repo> --gamma <owner/repo> [--installation <source-read App installation id>] [--manual-app] [--dry-run] [--name N] [--out file]');
    const { failed } = await runDeployed({ division, alpha, beta, gamma, installation, manualApp, dryRun, name, out });
    if (failed) process.exitCode = 1;
    return;
  }
  const ref = option('--ref') ?? 'main';
  const sync = flag('--sync');
  const [division, ...pairs] = argv;
  if (!division || !pairs.length) throw new Error('usage: onboarding-deployed.mjs [--ref main] [--sync] <owner/division> <owner/source>=<status> ... | run <owner/division> --alpha ... --beta ... --gamma ... [--installation <id>] [--manual-app] [--dry-run]');
  const expectations = pairs.map(parse);
  if (sync) { const result = await dispatchAndWait(division); if (result.pr) console.error(`refresh PR #${result.pr} is still open`); }
  const problems = checkDeployed({ catalog: readCatalog(division, ref), heads: liveHeads(expectations), expectations });
  if (problems.length) { console.error(problems.join('\n')); process.exitCode = 1; }
  else console.log(`PASS — ${expectations.length} expectation(s) match ${division}@${ref}`);
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  try { await main(process.argv.slice(2)); } catch (error) { console.error(error.message); process.exitCode = 1; }
}
