import assert from 'node:assert/strict';
import test from 'node:test';
import { verifyReplay } from '../deploy/workspace/verify-replay.mjs';

const live = {
  generation: 'eb3d64e6-b758-488c-80e7-ef988ec510a8',
  snapshotId: 'a'.repeat(64), throughPosition: 5, stateDigest: 'b'.repeat(64)
};
const rebuilt = { ...live, generation: '47bbb08a-a679-45e0-84d5-d4086af5fc6c' };

function fixture(candidate = rebuilt, active = live) {
  const commands = [];
  async function run(...args) {
    commands.push(args.join(' '));
    if (args[0] === 'describe-current') return JSON.stringify(active);
    if (args[0] === 'replay') return JSON.stringify(candidate);
    if (args[0] === 'activate-replay') active = candidate;
    return '';
  }
  return { commands, run };
}

test('replay checks the fixture, switches an equivalent generation and resumes writes', async () => {
  const { commands, run } = fixture();
  assert.deepEqual(await verifyReplay(run, live.snapshotId), { live, rebuilt, active: rebuilt });
  assert.deepEqual(commands, ['describe-current', 'writes pause', 'describe-current', 'replay --through 5',
    `activate-replay --generation ${rebuilt.generation} --expect-position 5`, 'describe-current', 'writes resume']);
});

test('replay stops before pausing if the active snapshot is different', async () => {
  const { commands, run } = fixture();
  await assert.rejects(verifyReplay(run, 'c'.repeat(64)), /active fixture snapshot/);
  assert.deepEqual(commands, ['describe-current']);
});

for (const candidate of [{ ...rebuilt, stateDigest: 'c'.repeat(64) }, { ...rebuilt, throughPosition: 6 }]) {
  test(`replay resumes without cutting over after a mismatch (${candidate.throughPosition})`, async () => {
    const { commands, run } = fixture(candidate);
    await assert.rejects(verifyReplay(run, live.snapshotId), /replay comparison failed/i);
    assert.deepEqual(commands, ['describe-current', 'writes pause', 'describe-current', 'replay --through 5', 'writes resume']);
  });
}
