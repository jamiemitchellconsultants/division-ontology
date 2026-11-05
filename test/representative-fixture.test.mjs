import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, existsSync, readFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { setup, teardown } from '../test/fixtures/representative/setup.mjs';
import { loadSnapshot } from '../src/c4/snapshot.mjs';
import { loadRegistries } from '../src/c4/registry.mjs';
import { runC4Cli } from '../src/c4/cli.mjs';
import { runCli } from '../src/cli.mjs';

test('representative fixture builds a catalog and supported cross-source graph, then removes its own files', async () => {
  const parent = mkdtempSync(join(tmpdir(), 'division-representative-test-'));
  const root = join(parent, 'fixture');
  try {
    const f = await setup(root);
    const overrides = [...f.checkouts].flatMap(([id, path]) => ['--checkout', `${id}=${path}`]);
    await runCli(['check', ...overrides], { root: f.divisionRoot });
    const catalog = JSON.parse(readFileSync(join(f.divisionRoot, 'dist/catalog.json')));
    assert.equal(catalog.sources.length, 3);
    assert.ok(catalog.concepts.length >= 20);
    const snapshot = loadSnapshot({ divisionRoot: f.divisionRoot, catalogCommit: f.commit, checkouts: f.checkouts });
    const registry = loadRegistries({ divisionRoot: f.divisionRoot, registryCommit: f.commit, snapshot });
    assert.equal(registry.bindings.length, 2);
    assert.deepEqual(registry.gaps, []);
    assert.ok([...snapshot.sources.values()].every(s => s.graphState.status === 'available'));
    const args = ['--catalog-commit', f.commit, '--registry-commit', f.commit, ...overrides, '--output', join(root, 'output')];
    assert.equal(await runC4Cli(['paths', ...args, '--start', 'fixture/ecommerce=https://example.test/ecommerce/graph#system'], { cwd: f.divisionRoot }), 0);
    const paths = JSON.parse(readFileSync(join(root, 'output/paths.json')));
    assert.ok(paths.paths.some(path => path.steps.filter(step => step.type === 'binding').length === 2));
    assert.ok(paths.gaps.some(gap => gap.code === 'coverage-unknown'));
    assert.throws(() => teardown(parent));
    await assert.rejects(setup(root), /EEXIST/);
    teardown(root);
    assert.equal(existsSync(root), false);
    assert.equal(existsSync(parent), true);
  } finally { rmSync(parent, { recursive: true, force: true }); }
});
