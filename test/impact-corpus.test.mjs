import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { recordScenario } from './impact-corpus/capture.mjs';
import { scenarios } from './impact-corpus/scenarios.mjs';

const corpus = fileURLToPath(new URL('./impact-corpus/', import.meta.url));
// Scenarios added in I2 and I3 are listed here until their behaviour lands.
const pending = new Set(['confluence']);

for (const id of Object.keys(scenarios)) {
  test(`B37 baseline: ${id} matches its expected bytes`, { skip: pending.has(id) }, async () => {
    const expected = JSON.parse(readFileSync(join(corpus, id, 'steps.json'), 'utf8'));
    const steps = await recordScenario(id, { copyInputs: false });
    assert.equal(steps.length, expected.length, 'step count');
    steps.forEach((step, index) => {
      const want = expected[index], dir = join(corpus, id, 'expected', String(index).padStart(3, '0'));
      assert.deepEqual(step.argv, want.argv, `step ${index} argv`);
      assert.equal(step.exit, want.exit, `step ${index} exit`);
      assert.equal(step.stderr, want.stderr, `step ${index} stderr`);
      assert.deepEqual(step.files, want.files, `step ${index} files`);
      for (const name of want.files)
        assert.ok(step.outputs[name].equals(readFileSync(join(dir, name))), `step ${index} ${name} differs from the baseline`);
    });
  });
}
