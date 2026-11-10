import { spawnSync } from 'node:child_process';
import { readdirSync } from 'node:fs';
import { hostname } from 'node:os';
import { fileURLToPath } from 'node:url';

const suites = {
  c4: { file: 'test/acceptance/c4.test.mjs', pass: 'PASS — repository tests and B1–B15' },
  onboarding: { file: 'test/acceptance/onboarding.test.mjs', pass: 'PASS — repository tests and one-action source onboarding B1–B17' },
};
const name = process.argv[2] ?? 'c4';
const suite = suites[name];
if (!suite) { console.error('usage: rerun.mjs [' + Object.keys(suites).join('|') + ']'); process.exit(2); }
const cwd = fileURLToPath(new URL('../../', import.meta.url));
const revision = spawnSync('git', ['rev-parse', '--short', 'HEAD'], { cwd, encoding: 'utf8' });
console.log('Machine: ' + hostname());
console.log('Node: ' + process.version);
console.log('Revision: ' + (revision.stdout?.trim() || 'unavailable'));

const repositoryTests = readdirSync(new URL('../', import.meta.url))
  .filter(name => name.endsWith('.test.mjs'))
  .sort()
  .map(name => 'test/' + name);
const result = spawnSync(process.execPath, [
  '--test', '--test-reporter=dot', ...repositoryTests, suite.file
], { cwd, encoding: 'utf8' });

if (result.status === 0) {
  console.log(suite.pass);
} else {
  // Drop the progress dots and preserve the failed test names and assertions.
  const discrepancies = result.stdout?.split(/\r?\n/).filter(line => !/^[.X]+$/.test(line)).join('\n').trim();
  if (discrepancies) console.error(discrepancies);
  if (result.stderr?.trim()) console.error(result.stderr.trim());
  if (result.error) console.error(result.error.message);
  if (result.signal) console.error('Test process stopped by ' + result.signal);
  process.exitCode = result.status ?? 1;
}
