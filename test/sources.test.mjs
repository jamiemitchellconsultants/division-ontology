import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, mkdirSync, writeFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { execFileSync } from 'node:child_process';
import { parseTurtle, readSourceOntology } from '../src/rdf.mjs';
import { loadSources, resolveSource } from '../src/sources.mjs';

const source = { id: 'example/booking', url: 'https://example.invalid/booking.git', ref: 'main', ontologyPath: 'docs/ontology.ttl', namespace: 'https://example.test/booking#' };

function repository() {
  const root = mkdtempSync(join(tmpdir(), 'division-source-'));
  execFileSync('git', ['init', '-q', '-b', 'main', root]);
  mkdirSync(join(root, 'docs'));
  writeFileSync(join(root, 'docs/ontology.ttl'), '@prefix : <https://example.test/booking#> .\n:Booking <http://www.w3.org/2000/01/rdf-schema#label> "Booking" .\n');
  execFileSync('git', ['add', '.'], { cwd: root });
  execFileSync('git', ['-c', 'user.name=Test', '-c', 'user.email=test@example.invalid', 'commit', '-q', '-m', 'fixture'], { cwd: root });
  return root;
}

test('malformed Turtle is rejected rather than treated as an empty ontology', () => {
  assert.throws(() => parseTurtle(':Broken rdfs:label "unclosed', 'https://example.test/'), /unexpected|expected|literal|prefix|unterminated/i);
});

test('source manifest rejects duplicate repository IDs', () => {
  const root = mkdtempSync(join(tmpdir(), 'division-manifest-'));
  try {
    const file = join(root, 'sources.json');
    writeFileSync(file, JSON.stringify({ sources: [source, source] }));
    assert.throws(() => loadSources(file), /duplicate.*example\/booking/i);
  } finally { rmSync(root, { recursive: true, force: true }); }
});

test('source ontology must use its configured namespace', () => {
  const root = repository();
  try {
    const resolved = resolveSource(source, new Map([[source.id, root]]));
    writeFileSync(join(root, 'docs/ontology.ttl'), '@prefix : <https://wrong.test/#> .\n:Other <http://www.w3.org/2000/01/rdf-schema#label> "Other" .\n');
    assert.throws(() => readSourceOntology(source, resolved.directory), /namespace/i);
  } finally { rmSync(root, { recursive: true, force: true }); }
});

test('dirty source checkout is rejected so recorded commit describes its concepts', () => {
  const root = repository();
  try {
    writeFileSync(join(root, 'docs/ontology.ttl'), '@prefix : <https://example.test/booking#> .\n:Changed <http://www.w3.org/2000/01/rdf-schema#label> "Changed" .\n');
    assert.throws(() => resolveSource(source, new Map([[source.id, root]])), /uncommitted|dirty/i);
  } finally { rmSync(root, { recursive: true, force: true }); }
});
