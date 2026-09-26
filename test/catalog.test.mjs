import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, writeFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { parseTurtle } from '../src/rdf.mjs';
import { buildCatalog, loadLinks } from '../src/catalog.mjs';

const shared = 'https://example.test/division#';
const booking = 'https://example.test/booking#';
const people = 'https://example.test/people#';
const coreQuads = parseTurtle(`@prefix : <${shared}> . @prefix owl: <http://www.w3.org/2002/07/owl#> . @prefix rdfs: <http://www.w3.org/2000/01/rdf-schema#> . :StaffId a owl:Class; rdfs:label "StaffId"; rdfs:comment "Enterprise staff number" .`, shared);
const bookingQuads = parseTurtle(`@prefix : <${booking}> . @prefix owl: <http://www.w3.org/2002/07/owl#> . @prefix rdfs: <http://www.w3.org/2000/01/rdf-schema#> . :StaffId a owl:Class; rdfs:label "StaffId" . :Record a owl:Class; rdfs:label "Record" . :_invariant_1 <https://example.test/kit#invariantLabel> "StaffId"; <https://example.test/kit#invariantText> "Never reissued" .`, booking);
const peopleQuads = parseTurtle(`@prefix : <${people}> . @prefix owl: <http://www.w3.org/2002/07/owl#> . @prefix rdfs: <http://www.w3.org/2000/01/rdf-schema#> . :Record a owl:Class; rdfs:label "Record" .`, people);
const sources = [
  { id: 'example/booking', namespace: booking, commit: 'a'.repeat(40), quads: bookingQuads },
  { id: 'example/people', namespace: people, commit: 'b'.repeat(40), quads: peopleQuads },
];
const exactLink = { source: 'example/booking', local: `${booking}StaffId`, shared: `${shared}StaffId`, relation: 'exact' };

test('all source concepts remain distinct even when labels collide', () => {
  const { catalog } = buildCatalog({ coreQuads, sources, links: [exactLink], coreNamespace: shared });
  assert.equal(catalog.concepts.length, 4);
  assert.deepEqual(catalog.concepts.filter((concept) => concept.label === 'Record').map((concept) => concept.iri), [`${booking}Record`, `${people}Record`]);
  assert.equal(catalog.sources[0].commit, 'a'.repeat(40));
  assert.deepEqual(catalog.links, [exactLink]);
  assert.equal(catalog.rules[0].text, 'Never reissued');
});

test('every source and core triple carries a named provenance graph', () => {
  const { quads } = buildCatalog({ coreQuads, sources, links: [exactLink], coreNamespace: shared });
  assert.equal(quads.length, coreQuads.length + bookingQuads.length + peopleQuads.length + 1);
  assert(quads.every((quad) => quad.graph.termType === 'NamedNode'));
  assert.equal(new Set(quads.map((quad) => quad.graph.value)).size, 4);
  assert(quads.some((quad) => quad.predicate.value === 'http://www.w3.org/2002/07/owl#equivalentClass' && quad.subject.value === exactLink.local && quad.object.value === exactLink.shared));
});

test('exact alignment rejects a different local label', () => {
  const wrong = { ...exactLink, local: `${booking}Record` };
  assert.throws(() => buildCatalog({ coreQuads, sources, links: [wrong], coreNamespace: shared }), /exact.*label/i);
});

test('a property cannot be aligned as a class even when its label matches', () => {
  const property = parseTurtle(`@prefix : <${booking}> . @prefix owl: <http://www.w3.org/2002/07/owl#> . @prefix rdfs: <http://www.w3.org/2000/01/rdf-schema#> . :StaffId a owl:ObjectProperty; rdfs:label "StaffId" .`, booking);
  const modified = [{ ...sources[0], quads: property }];
  assert.throws(() => buildCatalog({ coreQuads, sources: modified, links: [exactLink], coreNamespace: shared }), /class.*type|not.*class/i);
});

test('alignment rejects unresolved shared or local IRIs', () => {
  assert.throws(() => buildCatalog({ coreQuads, sources, links: [{ ...exactLink, shared: `${shared}Missing` }], coreNamespace: shared }), /shared.*not found/i);
  assert.throws(() => buildCatalog({ coreQuads, sources, links: [{ ...exactLink, local: `${booking}Missing` }], coreNamespace: shared }), /local.*not found/i);
});

test('link loader rejects unsupported relations', () => {
  const root = mkdtempSync(join(tmpdir(), 'division-links-'));
  try {
    const file = join(root, 'links.json');
    writeFileSync(file, JSON.stringify({ source: 'example/booking', links: [{ local: `${booking}StaffId`, shared: `${shared}StaffId`, relation: 'related' }] }));
    assert.throws(() => loadLinks(file), /relation.*exact.*specializes/i);
  } finally { rmSync(root, { recursive: true, force: true }); }
});
