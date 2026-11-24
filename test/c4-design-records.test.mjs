import test from 'node:test';
import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { withPilotFixture } from './fixtures/c4/fixture-repos.mjs';
import { loadSnapshot } from '../src/c4/snapshot.mjs';

const source = 'fixture/Alpha';
const kit = 'https://ontology-kit.dev/schema#';
const ns = 'https://alpha.example/#';
const litePath = 'docs/features/payments/feature.md';
const fullPath = 'docs/features/payments/02-behaviours.md';
const declaration = '# Payments\n\n**B1 — Confirm payment.**\n';
const arc = (id, from, relationship, to, basis = 'direct') =>
  ':' + id + ' a kit:GraphAssertion ; kit:subject :' + from + ' ; kit:object :' + to +
  ' ; kit:relationship kit:' + relationship + ' ; kit:basis kit:' + basis + ' ; kit:evidence :document .\n';

function writeDesign(f, path, mutate = turtle => turtle) {
  const config = { sourceGraph: { enabled: true, root: 'graph',
    evidence: { implementation: ['src/**'], 'selected-document': [path] } } };
  f.writeSource(source, 'ontology.config.json', JSON.stringify(config));
  f.writeSource(source, path, declaration);
  const bytes = Buffer.from(declaration);
  const blobId = createHash('sha1').update('blob ' + bytes.length + '\0').update(bytes).digest('hex');
  const graph = f.readSource(source, 'graph/manifest.ttl');
  f.writeSource(source, 'graph/manifest.ttl', graph
    .replace('<' + kit + 'schemaVersion> "1"', '<' + kit + 'schemaVersion> 2')
    .replace('<' + kit + 'role> <' + kit + 'implementation>', '<' + kit + 'role> <' + kit + 'role-implementation>'));
  const design = [
    '@prefix : <' + ns + '> .',
    '@prefix kit: <' + kit + '> .',
    '@prefix rdfs: <http://www.w3.org/2000/01/rdf-schema#> .',
    ':documents a kit:GraphCoverage ; kit:pathSelector ' + JSON.stringify(path) + ' ; kit:priority 1 ; kit:status kit:covered .',
    ':document a kit:GraphEvidence ; kit:path ' + JSON.stringify(path) +
      ' ; kit:startLine 3 ; kit:endLine 3 ; kit:blobId "' + blobId + '" ; kit:role kit:role-selected-document .',
    ':behaviour a kit:GraphNode ; kit:kind kit:behaviour ; rdfs:label "payments#B1 Confirm payment" ; kit:designStatus kit:current ; kit:evidence :document .',
    ':retired a kit:GraphNode ; kit:kind kit:behaviour ; rdfs:label "payments#B2 Old flow" ; kit:designStatus kit:retired ; kit:evidence :document .',
    ':decision a kit:GraphNode ; kit:kind kit:decision ; rdfs:label "ADR 0001 Routing" ; kit:designStatus kit:accepted ; kit:evidence :document .',
    ':old-decision a kit:GraphNode ; kit:kind kit:decision ; rdfs:label "ADR 0002 Old routing" ; kit:designStatus kit:superseded ; kit:evidence :document .',
    ':proposal a kit:GraphNode ; kit:kind kit:decision ; rdfs:label "ADR 0003 Proposal" ; kit:designStatus kit:proposed ; kit:evidence :document .',
    ':deprecated a kit:GraphNode ; kit:kind kit:decision ; rdfs:label "ADR 0004 Deprecated" ; kit:designStatus kit:deprecated ; kit:evidence :document .',
    ':test a kit:GraphNode ; kit:kind kit:test ; rdfs:label "tests/payment.js" .',
    ':unlinked a kit:GraphNode ; kit:kind kit:behaviour ; rdfs:label "payments#B3 Unlinked" ; kit:designStatus kit:current ; kit:evidence :document .',
    ':empty-component a kit:GraphNode ; kit:kind kit:component ; rdfs:label "Empty handler" .',
    ':partial a kit:GraphNode ; kit:kind kit:behaviour ; rdfs:label "payments#B4 Partial" ; kit:designStatus kit:current ; kit:evidence :document .',
    arc('direct', 'code', 'implements', 'behaviour'),
    arc('via', 'component', 'implements', 'behaviour'),
    arc('partial-impl', 'empty-component', 'implements', 'partial'),
    arc('old-impl', 'code', 'implements', 'retired'),
    arc('test-link', 'behaviour', 'tested-by', 'test'),
    arc('mentions', 'component', 'documented-by', 'old-decision', 'inferred'),
    arc('replacement', 'retired', 'replaced-by', 'behaviour'),
    arc('succession', 'old-decision', 'superseded-by', 'decision'),
  ].join('\n');
  f.writeSource(source, 'graph/design.ttl', mutate(design));
  return { pin: f.commitSource(source, 'Design graph'), blobId };
}

function catalogAt(f, pin) {
  const catalog = JSON.parse(f.readDivision('dist/catalog.json'));
  catalog.sources.find(row => row.id === source).commit = pin;
  f.writeDivision('dist/catalog.json', JSON.stringify(catalog));
  return { ...f, catalogCommit: f.commitDivision('Pin C4 test source') };
}

function replace(text, needle, value) {
  assert.ok(text.includes(needle));
  return text.replace(needle, value);
}

function routes(graph, target) {
  const edges = [...graph.assertions.values()];
  const found = [];
  for (const edge of edges.filter(item => item.object === target && item.relationship === kit + 'implements')) {
    const kind = graph.nodes.get(edge.subject).kind;
    if (kind === kit + 'source-symbol') found.push([edge.iri, edge.subject]);
    if (kind === kit + 'component') {
      for (const containment of edges.filter(item => item.subject === edge.subject && item.relationship === kit + 'contains')) {
        if ([kit + 'source-file', kit + 'source-symbol'].includes(graph.nodes.get(containment.object).kind)) {
          found.push([edge.iri, edge.subject, containment.iri, containment.object]);
        }
      }
    }
  }
  return found.sort((a, b) => JSON.stringify(a).localeCompare(JSON.stringify(b)));
}

test('preserves design provenance and both routes from the pinned config and graph', () => withPilotFixture(f => {
  const { pin, blobId } = writeDesign(f, litePath);
  const input = catalogAt(f, pin);
  const later = writeDesign(f, fullPath, text => replace(text, 'payments#B1 Confirm payment', 'payments#B1 Reworded payment'));
  assert.notEqual(later.pin, pin);
  const imported = loadSnapshot(input).sources.get(source);
  assert.equal(imported.commit, pin);
  const graph = imported.graphState;
  assert.equal(graph.schemaVersion, 2);
  for (const [id, kind, label, status] of [
    ['behaviour', 'behaviour', 'payments#B1 Confirm payment', 'current'],
    ['retired', 'behaviour', 'payments#B2 Old flow', 'retired'],
    ['decision', 'decision', 'ADR 0001 Routing', 'accepted'],
    ['old-decision', 'decision', 'ADR 0002 Old routing', 'superseded'],
    ['proposal', 'decision', 'ADR 0003 Proposal', 'proposed'],
    ['deprecated', 'decision', 'ADR 0004 Deprecated', 'deprecated'],
  ]) {
    const node = graph.nodes.get(ns + id);
    assert.deepEqual([node.iri, node.kind, node.label, node.designStatus, node.evidenceIris],
      [ns + id, kit + kind, label, kit + status, [ns + 'document']]);
  }
  assert.equal(Object.hasOwn(graph.nodes.get(ns + 'code'), 'designStatus'), false);
  assert.equal(Object.hasOwn(graph.nodes.get(ns + 'code'), 'evidenceIris'), false);
  for (const [id, from, relationship, to, basis = 'direct'] of [
    ['direct', 'code', 'implements', 'behaviour'],
    ['via', 'component', 'implements', 'behaviour'],
    ['old-impl', 'code', 'implements', 'retired'],
    ['test-link', 'behaviour', 'tested-by', 'test'],
    ['mentions', 'component', 'documented-by', 'old-decision', 'inferred'],
    ['replacement', 'retired', 'replaced-by', 'behaviour'],
    ['succession', 'old-decision', 'superseded-by', 'decision'],
  ]) assert.deepEqual(graph.assertions.get(ns + id), { iri: ns + id, subject: ns + from, object: ns + to,
    relationship: kit + relationship, basis, evidenceIris: [ns + 'document'] });
  assert.deepEqual(graph.evidence.get(ns + 'document'), { iri: ns + 'document',
    path: litePath, startLine: 3, endLine: 3, blobId, role: 'selected-document' });
  assert.ok(graph.coverage.some(row => row.selector === litePath && row.status === 'covered'));
  assert.deepEqual(routes(graph, ns + 'behaviour'), [[ns + 'direct', ns + 'code'],
    [ns + 'via', ns + 'component', ns + 'contains3', ns + 'code']]);
  assert.deepEqual(routes(graph, ns + 'unlinked'), []);
  assert.deepEqual(routes(graph, ns + 'partial'), []);
}));

for (const [name, needle, value, error] of [
  ['missing status', ' ; kit:designStatus kit:current', '', /designStatus/i],
  ['unknown status', 'kit:designStatus kit:current', 'kit:designStatus kit:unknown', /designStatus|status/i],
  ['conflicting status', 'kit:designStatus kit:current', 'kit:designStatus kit:current, kit:retired', /designStatus|status/i],
  ['wrong-kind status', 'kit:designStatus kit:current', 'kit:designStatus kit:accepted', /designStatus|status/i],
  ['missing evidence', 'kit:designStatus kit:current ; kit:evidence :document', 'kit:designStatus kit:current', /evidence/i],
  ['multiple evidence', 'kit:designStatus kit:current ; kit:evidence :document', 'kit:designStatus kit:current ; kit:evidence :document, :evidence', /evidence/i],
  ['literal evidence', 'kit:designStatus kit:current ; kit:evidence :document', 'kit:designStatus kit:current ; kit:evidence "document"', /evidence/i],
  ['unknown evidence', 'kit:designStatus kit:current ; kit:evidence :document', 'kit:designStatus kit:current ; kit:evidence :unknown', /evidence/i],
  ['wrong evidence role', 'kit:designStatus kit:current ; kit:evidence :document', 'kit:designStatus kit:current ; kit:evidence :evidence', /evidence|selected-document/i],
  ['design metadata on an ordinary node', 'rdfs:label "tests/payment.js"', 'rdfs:label "tests/payment.js" ; kit:designStatus kit:current ; kit:evidence :document', /design|status|evidence/i],
]) {
  test('rejects ' + name, () => withPilotFixture(f => {
    const { pin } = writeDesign(f, litePath, text => replace(text, needle, value));
    assert.throws(() => loadSnapshot(catalogAt(f, pin)), error);
  }));
}
