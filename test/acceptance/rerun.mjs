import { spawnSync } from 'node:child_process';
import { readdirSync } from 'node:fs';
import { hostname } from 'node:os';
import { fileURLToPath } from 'node:url';

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
  '--test', '--test-reporter=dot', ...repositoryTests, 'test/acceptance/c4.test.mjs'
], { cwd, encoding: 'utf8' });

if (result.status === 0) {
  console.log('PASS — repository tests and B1–B15');
} else {
  // Drop the progress dots and preserve the failed test names and assertions.
  const discrepancies = result.stdout?.split(/\r?\n/).filter(line => !/^[.X]+$/.test(line)).join('\n').trim();
  if (discrepancies) console.error(discrepancies);
  if (result.stderr?.trim()) console.error(result.stderr.trim());
  if (result.error) console.error(result.error.message);
  if (result.signal) console.error('Test process stopped by ' + result.signal);
  process.exitCode = result.status ?? 1;
}
