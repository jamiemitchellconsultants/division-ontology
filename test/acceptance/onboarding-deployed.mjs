import { execFileSync } from 'node:child_process';
import { pathToFileURL } from 'node:url';

// Read-only check of a deployed division's published catalog against the live source repositories.
// usage: node test/acceptance/onboarding-deployed.mjs <owner/division> [--ref main] <owner/source>=<status> ...
// Status is current, invalid, "not configured", "access removed" or absent (no row, not in the catalog).
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

function main(argv) {
  const refIndex = argv.indexOf('--ref');
  const ref = refIndex >= 0 ? argv.splice(refIndex, 2)[1] : 'main';
  const [division, ...pairs] = argv;
  if (!division || !pairs.length) throw new Error('usage: onboarding-deployed.mjs <owner/division> [--ref main] <owner/source>=<status> ...');
  const expectations = pairs.map((pair) => { const at = pair.lastIndexOf('='); return { source: pair.slice(0, at), status: pair.slice(at + 1) }; });
  const catalog = JSON.parse(gh('api', '-H', 'Accept: application/vnd.github.raw', `repos/${division}/contents/dist/catalog.json?ref=${encodeURIComponent(ref)}`));
  const heads = {};
  for (const { source, status } of expectations) {
    if (status !== 'current') continue;
    const branch = gh('api', `repos/${source}`, '--jq', '.default_branch').trim();
    heads[source] = gh('api', `repos/${source}/commits/${encodeURIComponent(branch)}`, '--jq', '.sha').trim();
  }
  const problems = checkDeployed({ catalog, heads, expectations });
  if (problems.length) { console.error(problems.join('\n')); process.exitCode = 1; }
  else console.log(`PASS — ${expectations.length} expectation(s) match ${division}@${ref}`);
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  try { main(process.argv.slice(2)); } catch (error) { console.error(error.message); process.exitCode = 1; }
}
