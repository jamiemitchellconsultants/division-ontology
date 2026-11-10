import { spawnSync } from 'node:child_process';
import { readdirSync } from 'node:fs';
import { hostname } from 'node:os';
import { fileURLToPath } from 'node:url';

const suites = {
  c4: { file: 'test/acceptance/c4.test.mjs', pass: 'PASS — repository tests and B1–B15' },
  onboarding: { file: 'test/acceptance/onboarding.test.mjs', pass: 'PASS — repository tests and one-action source onboarding B1–B17', table: true },
};
const name = process.argv[2] ?? 'c4';
const suite = suites[name];
if (!suite) { console.error('usage: rerun.mjs [' + Object.keys(suites).join('|') + ']'); process.exit(2); }
const cwd = fileURLToPath(new URL('../../', import.meta.url));
const revision = spawnSync('git', ['rev-parse', '--short', 'HEAD'], { cwd, encoding: 'utf8' });
console.log('Machine: ' + hostname());
console.log('Node: ' + process.version);
console.log('Revision: ' + (revision.stdout?.trim() || 'unavailable'));
const dirty = spawnSync('git', ['status', '--porcelain'], { cwd, encoding: 'utf8' });
console.log('Working tree: ' + (dirty.stdout?.trim() ? 'NOT clean (not valid as an acceptance record)' : 'clean'));

const repositoryTests = readdirSync(new URL('../', import.meta.url))
  .filter(name => name.endsWith('.test.mjs'))
  .sort()
  .map(name => 'test/' + name);
const result = spawnSync(process.execPath, [
  '--test', '--test-reporter=dot', ...repositoryTests, ...(suite.table ? [] : [suite.file])
], { cwd, encoding: 'utf8' });

// With a results table, the acceptance file runs on its own with the TAP reporter so every check is listed by name.
let table = null;
if (suite.table) {
  const acceptance = spawnSync(process.execPath, ['--test', '--test-reporter=tap', suite.file], { cwd, encoding: 'utf8' });
  const lines = (acceptance.stdout ?? '').split(/\r?\n/);
  table = { status: acceptance.status ?? 1, rows: lines.flatMap(line => { const m = /^(not )?ok \d+ - (.+?)(?: #.*)?$/.exec(line); return m ? [[m[1] ? 'FAIL' : 'pass', m[2]]] : []; }), failures: [], stderr: acceptance.stderr?.trim() };
  let inFailure = false;
  for (const line of lines) {
    if (/^not ok /.test(line)) inFailure = true;
    else if (/^(ok |1\.\.|# )/.test(line)) inFailure = false;
    if (inFailure) table.failures.push(line);
  }
  console.log('\nAcceptance checks');
  for (const [state, title] of table.rows) console.log('  ' + state.padEnd(4) + ' ' + title);
  console.log('');
}

if (result.status === 0 && (!table || table.status === 0)) {
  console.log(suite.pass);
} else {
  // Drop the progress dots and preserve the failed test names and assertions.
  const discrepancies = result.stdout?.split(/\r?\n/).filter(line => !/^[.X]+$/.test(line)).join('\n').trim();
  if (discrepancies) console.error(discrepancies);
  if (result.stderr?.trim()) console.error(result.stderr.trim());
  if (result.error) console.error(result.error.message);
  if (result.signal) console.error('Test process stopped by ' + result.signal);
  if (table?.failures.length) console.error(table.failures.join('\n'));
  if (table?.stderr) console.error(table.stderr);
  process.exitCode = result.status || table?.status || 1;
}
