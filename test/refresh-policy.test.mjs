import test from 'node:test';
import assert from 'node:assert/strict';
import { classifyRefresh } from '../src/refresh-policy.mjs';

const core = 'https://example.test/division#StaffId';
const linked = 'https://example.test/booking#StaffId';
const other = 'https://example.test/rooms#Room';
const classType = 'http://www.w3.org/2002/07/owl#Class';
const label = 'http://www.w3.org/2000/01/rdf-schema#label';
const comment = 'http://www.w3.org/2000/01/rdf-schema#comment';
const graph = (commit) => `<urn:division-ontology:source:example%2Fbooking:${commit}>`;

function baseline() {
  const previousCatalog = {
    core: { namespace: 'https://example.test/division#', version: '1', conceptCount: 1 },
    sources: [
      { id: 'example/booking', namespace: 'https://example.test/booking#', commit: 'a'.repeat(40), conceptCount: 1 },
      { id: 'example/rooms', namespace: 'https://example.test/rooms#', commit: 'c'.repeat(40), conceptCount: 1 },
    ],
    concepts: [
      { iri: core, source: 'division-core', label: 'StaffId', definition: 'Enterprise staff number', types: [classType] },
      { iri: linked, source: 'example/booking', label: 'StaffId', definition: 'Enterprise staff number', types: [classType] },
      { iri: other, source: 'example/rooms', label: 'Room', definition: 'Bookable room', types: [classType] },
    ],
    rules: [{ source: 'example/booking', subject: 'https://example.test/booking#_rule1', label: 'StaffId', text: 'Never reissued' }],
    links: [{ source: 'example/booking', local: linked, shared: core, relation: 'exact' }],
  };
  const previousNQuads = [
    `<${core}> <${label}> "StaffId" <urn:division-ontology:core> .`,
    `<${linked}> <${label}> "StaffId" ${graph('a'.repeat(40))} .`,
    `<${linked}> <${comment}> "Enterprise staff number" ${graph('a'.repeat(40))} .`,
    `<${other}> <${label}> "Room" <urn:division-ontology:source:example%2Frooms:${'c'.repeat(40)}> .`,
  ].join('\n') + '\n';
  return { previousCatalog, nextCatalog: structuredClone(previousCatalog), previousNQuads, nextNQuads: previousNQuads, changedPaths: ['dist/catalog.json', 'dist/combined.nq'] };
}

test('an unlinked concept can refresh automatically across two sources', () => {
  const data = baseline();
  data.nextCatalog.sources[1].commit = 'd'.repeat(40);
  data.nextCatalog.sources[1].conceptCount = 2;
  data.nextCatalog.concepts.push({ iri: 'https://example.test/rooms#Desk', source: 'example/rooms', label: 'Desk', definition: 'Bookable desk', types: [classType] });
  data.nextNQuads += `<https://example.test/rooms#Desk> <${label}> "Desk" <urn:division-ontology:source:example%2Frooms:${'d'.repeat(40)}> .\n`;
  assert.equal(classifyRefresh(data).mode, 'automatic');
});

test('source commit and graph names may change without a linked-term review', () => {
  const data = baseline();
  data.nextCatalog.sources[0].commit = 'b'.repeat(40);
  data.nextNQuads = data.previousNQuads.replaceAll('a'.repeat(40), 'b'.repeat(40));
  assert.equal(classifyRefresh(data).mode, 'automatic');
});

test('a changed linked definition requires steward review', () => {
  const data = baseline();
  data.nextCatalog.concepts[1].definition = 'A new identifier';
  assert.equal(classifyRefresh(data).mode, 'review');
});

test('a changed direct triple about a linked concept requires steward review', () => {
  const data = baseline();
  data.nextNQuads = data.previousNQuads.replace('"Enterprise staff number"', '"A new identifier"');
  assert.equal(classifyRefresh(data).mode, 'review');
});

test('a changed rule associated with a linked concept requires steward review', () => {
  const data = baseline();
  data.nextCatalog.rules[0].text = 'May be reissued';
  assert.equal(classifyRefresh(data).mode, 'review');
});

test('source registration and links cannot refresh automatically', () => {
  const registered = baseline();
  registered.nextCatalog.sources[1].namespace = 'https://changed.test/rooms#';
  assert.equal(classifyRefresh(registered).mode, 'review');
  const relinked = baseline();
  relinked.nextCatalog.links[0].relation = 'specializes';
  assert.equal(classifyRefresh(relinked).mode, 'review');
});

test('a change outside the generated catalog needs steward review', () => {
  const data = baseline();
  data.changedPaths.push('sources.json');
  assert.equal(classifyRefresh(data).mode, 'review');
});

test('no changed files require no pull request', () => {
  const data = baseline();
  data.changedPaths = [];
  assert.equal(classifyRefresh(data).mode, 'none');
});
