import { execFileSync } from 'node:child_process';
import { writeFileSync } from 'node:fs';
import { hostname } from 'node:os';
import { createInterface } from 'node:readline/promises';
import { pathToFileURL } from 'node:url';

// Read-only check of a deployed division's published catalog against the live source repositories.
// usage: node test/acceptance/onboarding-deployed.mjs <owner/division> [--ref main] [--sync] <owner/source>=<status> ...
//    or: node test/acceptance/onboarding-deployed.mjs guided <owner/division> --alpha <owner/repo> --beta <owner/repo> --gamma <owner/repo> [--second <owner/division>] [--name N] [--out file]
// --sync dispatches the division's sync first and waits for it and its refresh PR. guided walks every deployed step.
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

export function deployedSteps({ alpha, beta, gamma, second }) {
  const a = (status) => ({ source: alpha, status });
  return [
    { id: 'D1', behaviours: 'B7', instruction: 'Before any grant, the new division has no sources and no import status.', expect: ['dist/catalog.json has empty sources and importStatus arrays'], empty: true },
    { id: 'D2', behaviours: 'B1, B6, B2', instruction: `As the owner of ${alpha} (a user with no write access to the division repository): commit .division-ontology.json and a valid ontology to its default branch, then add ${alpha} to the division's source-read App.`, expect: [`${alpha} is current at its default-branch head`, 'a generated-only PR opened and merged with no steward review', 'nothing was committed to the division repository by the alpha owner'], expectations: [a('current')], sync: true },
    { id: 'D3', behaviours: 'B13', instruction: `Add ${gamma} (no .division-ontology.json) to the source-read App, using a selected-repositories installation.`, expect: [`${gamma} has status not configured and no concepts`, `${alpha} is still current`], expectations: [a('current'), { source: gamma, status: 'not configured' }], sync: true },
    { id: 'D4', behaviours: 'B8', instruction: `Commit a new term to ${alpha}'s ontology on its default branch.`, expect: [`${alpha} is current at the new head, with no renewed grant`], expectations: [a('current')], sync: true },
    { id: 'D5', behaviours: 'B9', instruction: `Move ${alpha}'s ontology file and update its .division-ontology.json to the new path in the same commit.`, expect: [`${alpha} is current at the new head and its concepts are still published`], expectations: [a('current')], sync: true },
    { id: 'D6', behaviours: 'B11, B10', instruction: `Commit invalid Turtle to ${alpha}'s ontology.`, expect: [`${alpha} is invalid; its last valid version is still published and stale`, 'the reason is a code with a short detail and no credential'], expectations: [a('invalid')], sync: true },
    { id: 'D7', behaviours: 'B11', instruction: `Commit a fix to ${alpha}'s ontology.`, expect: [`${alpha} is current at the fixing commit`], expectations: [a('current')], sync: true },
    { id: 'D8', behaviours: 'B16, B12, B15', instruction: `Commit a .division-ontology.json and ontology for ${beta} that use ${alpha}'s namespace, then add ${beta} to the source-read App.`, expect: [`${beta} is invalid with reason namespace-conflict and is not published`, `${alpha} is still current in the same sync`], expectations: [a('current'), { source: beta, status: 'invalid' }], sync: true },
    { id: 'D9', behaviours: 'B14', instruction: `Remove ${alpha} from the source-read App.`, expect: [`${alpha} is access removed and its last version stays in the catalog, stale`], expectations: [a('access removed')], sync: true },
    { id: 'D10', behaviours: 'B14', instruction: `Add ${alpha} back to the source-read App.`, expect: [`${alpha} is current again at its head`], expectations: [a('current')], sync: true },
    { id: 'D11', behaviours: 'B17', instruction: 'Make no changes. The check runs the sync twice.', expect: ['the second run is green, opens no PR, and leaves main unchanged'], expectations: [a('current')], repeat: true },
    { id: 'D12', behaviours: 'B3', optional: true, instruction: `Open a PR that registers ${alpha} in sources.json while it is also in the App, get a steward to approve it, and let it merge.`, expect: [`${alpha} appears once, as registered, with no import status row`], expectations: [a('registered')], sync: true },
    ...(second ? [{ id: 'D13', behaviours: 'B4', optional: true, division: second, instruction: `In the second division (${second}), which has its own App and was never granted ${alpha}, run its sync.`, expect: [`${alpha} is not in the second division's catalog`], expectations: [a('absent')], sync: true }] : []),
    { id: 'D14', behaviours: 'B5', optional: true, instruction: `Only if acceptable in the test organisation: install the source-read App on all repositories instead of selected ones.`, expect: [`only repositories that carry .division-ontology.json appear; ${gamma} has no row`], expectations: [{ source: gamma, status: 'absent' }], sync: true },
  ];
}

export async function guidedDeployed({ division, alpha, beta, gamma, second, name = 'unnamed', out, log = console.log }) {
  const rl = createInterface({ input: process.stdin, output: process.stdout });
  const results = [];
  const steps = deployedSteps({ alpha, beta, gamma, second });
  try {
    if (!process.stdin.isTTY) throw new Error('run this in a terminal so you can answer prompts');
    for (const step of steps) {
      const target = step.division ?? division;
      log(`\n=== ${step.id} — ${step.behaviours}${step.optional ? ' (optional)' : ''} ===\n${step.instruction}`);
      const ready = (await rl.question(`\nPress Enter when done${step.optional ? ', s to skip' : ''}, q to quit: `)).trim().toLowerCase();
      if (ready === 'q') { log('Stopped.'); break; }
      if (ready === 's' && step.optional) { results.push({ id: step.id, behaviours: step.behaviours, optional: true, automated: 'skipped', walkthrough: 'skipped' }); continue; }
      let automated = 'n/a'; let problems = [];
      try {
        if (step.repeat) {
          const before = mainHead(target);
          await dispatchAndWait(target, { log }); await dispatchAndWait(target, { log });
          if (mainHead(target) !== before) problems.push('main moved after two syncs with no changes');
          if (openRefreshPr(target)) problems.push('a refresh PR is open after two syncs with no changes');
        } else if (step.sync) {
          const sync = await dispatchAndWait(target, { log });
          if (sync.pr) problems.push(`refresh PR #${sync.pr} is still open (classified for review, or checks pending)`);
        }
        if (step.empty) { const catalog = readCatalog(target); if (catalog.sources.length || catalog.importStatus?.length) problems.push('the catalog already holds sources or status'); }
        if (step.expectations) problems.push(...checkDeployed({ catalog: readCatalog(target), heads: liveHeads(step.expectations), expectations: step.expectations }));
        automated = problems.length ? 'FAIL' : 'pass';
      } catch (error) { problems.push(error.message); automated = 'FAIL'; }
      log(problems.length ? `\nAutomated check: FAIL\n${problems.map((line) => `  - ${line}`).join('\n')}` : '\nAutomated check: pass');
      log('Expect:'); for (const line of step.expect) log(`  - ${line}`);
      let answer; do { answer = (await rl.question('\nWalkthrough pass? [y]es / [n]o / [q]uit: ')).trim().toLowerCase(); } while (!['y', 'n', 'q'].includes(answer));
      if (answer === 'q') { log('Stopped.'); break; }
      results.push({ id: step.id, behaviours: step.behaviours, optional: Boolean(step.optional), automated, walkthrough: answer === 'y' ? 'pass' : 'FAIL' });
    }
  } finally { rl.close(); }
  const complete = steps.filter((step) => !step.optional).every((step) => results.some((item) => item.id === step.id && item.automated === 'pass' && item.walkthrough === 'pass'));
  const report = [`Deployed walkthrough — ${name}, ${new Date().toISOString().slice(0, 10)}, ${hostname()}, division ${division}, alpha ${alpha}, beta ${beta}, gamma ${gamma}${second ? `, second ${second}` : ''}`, '',
    '| Step | Behaviours | Automated check | Walkthrough |', '| --- | --- | --- | --- |', ...results.map((item) => `| ${item.id} | ${item.behaviours} | ${item.automated} | ${item.walkthrough} |`), '',
    complete ? 'All required steps passed.' : 'Not complete: a required step failed or was not reached.'].join('\n');
  log(`\n${report}`);
  if (out) writeFileSync(out, `${report}\n`);
  return { results };
}

async function main(argv) {
  const option = (flag) => (argv.includes(flag) ? argv.splice(argv.indexOf(flag), 2)[1] : undefined);
  if (argv[0] === 'guided') {
    argv.shift();
    const [alpha, beta, gamma, second, name, out] = [option('--alpha'), option('--beta'), option('--gamma'), option('--second'), option('--name'), option('--out')];
    const [division] = argv;
    if (!division || !alpha || !beta || !gamma) throw new Error('usage: onboarding-deployed.mjs guided <owner/division> --alpha <owner/repo> --beta <owner/repo> --gamma <owner/repo> [--second <owner/division>] [--name N] [--out file]');
    return guidedDeployed({ division, alpha, beta, gamma, second, name, out });
  }
  const ref = option('--ref') ?? 'main';
  const sync = argv.includes('--sync') && argv.splice(argv.indexOf('--sync'), 1);
  const [division, ...pairs] = argv;
  if (!division || !pairs.length) throw new Error('usage: onboarding-deployed.mjs <owner/division> [--ref main] [--sync] <owner/source>=<status> ... | guided <owner/division> --alpha ... --beta ... --gamma ...');
  const expectations = pairs.map(parse);
  if (sync) { const result = await dispatchAndWait(division); if (result.pr) console.error(`refresh PR #${result.pr} is still open`); }
  const problems = checkDeployed({ catalog: readCatalog(division, ref), heads: liveHeads(expectations), expectations });
  if (problems.length) { console.error(problems.join('\n')); process.exitCode = 1; }
  else console.log(`PASS — ${expectations.length} expectation(s) match ${division}@${ref}`);
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  try { await main(process.argv.slice(2)); } catch (error) { console.error(error.message); process.exitCode = 1; }
}
