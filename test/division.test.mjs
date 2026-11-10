import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { loadDivision, validateDivision } from '../src/division.mjs';

const valid = { namespace: 'https://example.test/division#', stewards: ['steward-one', 'Steward2'] };

test('a valid division configuration is accepted', () => assert.deepEqual(validateDivision(valid), valid));

test('the namespace must be an https IRI ending in # or /', () => {
  for (const namespace of ['http://example.test/division#', 'https://example.test/division', 'not an iri', 42]) {
    assert.throws(() => validateDivision({ ...valid, namespace }), /namespace/);
  }
  assert.doesNotThrow(() => validateDivision({ ...valid, namespace: 'https://example.test/division/' }));
});

test('stewards must be a non-empty list of distinct GitHub logins', () => {
  for (const stewards of [[], 'steward', ['bad login'], ['-leading'], ['a', 'A'], [7]]) {
    assert.throws(() => validateDivision({ ...valid, stewards }), /steward/);
  }
});

test('unknown keys are rejected', () => assert.throws(() => validateDivision({ ...valid, owner: 'x' }), /unknown/));

test('division.json is loaded from the repository root', () => {
  const root = mkdtempSync(join(tmpdir(), 'division-config-'));
  try {
    writeFileSync(join(root, 'division.json'), JSON.stringify(valid));
    assert.deepEqual(loadDivision(root), valid);
  } finally { rmSync(root, { recursive: true, force: true }); }
});

test('the repository division.json is valid', () => assert.doesNotThrow(() => loadDivision('.')));
