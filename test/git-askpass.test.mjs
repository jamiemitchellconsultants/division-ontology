import test from 'node:test';
import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { join } from 'node:path';

const script = join(import.meta.dirname, '../scripts/git-askpass.sh');
test('askpass returns only the requested Git credential component', () => {
  const env = { ...process.env, GH_SOURCE_TOKEN: 'secret-fixture-token' };
  assert.equal(execFileSync(script, ['Username for https://github.com:'], { encoding: 'utf8', env }), 'x-access-token\n');
  assert.equal(execFileSync(script, ['Password for https://x-access-token@github.com:'], { encoding: 'utf8', env }), 'secret-fixture-token\n');
});
