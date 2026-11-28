import test from 'node:test';
import assert from 'node:assert/strict';
import {existsSync,readdirSync} from 'node:fs';
import {join,dirname,basename} from 'node:path';
import {cliKey,impactCli} from '../src/c4/engine.mjs';

test('the launcher uses a build keyed to the current engine sources', () => {
  const saved = process.env.DIVISION_IMPACT_CLI;
  delete process.env.DIVISION_IMPACT_CLI;
  try {
    const dll = impactCli();
    assert.equal(basename(dirname(dll)), cliKey());
    assert.ok(existsSync(dll));
    assert.deepEqual(readdirSync(dirname(dirname(dll))).filter(n => n.includes('.partial-')), []);
  } finally {
    if (saved !== undefined) process.env.DIVISION_IMPACT_CLI = saved;
  }
});

test('the build key covers the engine project and ignores build output', () => {
  const key = cliKey();
  assert.match(key, /^[0-9a-f]{16}$/);
  assert.equal(cliKey(), key);
  assert.ok(!existsSync(join('.cache', 'impact-cli', 'RuntimeKnowledge.Impact.Cli.dll')), 'no unkeyed build at the cache root');
});
