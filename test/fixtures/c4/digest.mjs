import {createHash} from 'node:crypto';

// Test-only view digest: canonicalises a view object, serialises it, and returns the SHA-256 hex digest.
// This mirrors the production viewDigest from assessment.mjs without pulling in the engine.
const canonical = x =>
  Array.isArray(x)
    ? x.map(canonical)
    : x && typeof x === 'object'
      ? Object.fromEntries(Object.keys(x).sort().map(k => [k, canonical(x[k])]))
      : x;

export function viewDigest(view) {
  return createHash('sha256').update(JSON.stringify(canonical(view)) + '\n').digest('hex');
}
