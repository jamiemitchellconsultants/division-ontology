import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { pathToFileURL } from 'node:url';
import test from 'node:test';

const fixtures = process.env.C4_DESIGN_FIXTURES;
const kitCheckout = process.env.C4_DESIGN_KIT;
const trial = process.env.C4_DESIGN_TRIAL === '1';
const kit = 'https://ontology-kit.dev/schema#';
const git = (checkout, ...args) => execFileSync('git', ['-C', checkout, ...args], { encoding: 'utf8' }).trim();

async function load(name) {
  assert.ok(fixtures && kitCheckout, 'Set C4_DESIGN_FIXTURES and C4_DESIGN_KIT to the prepared directories.');
  const path = join(fixtures, name + '.json');
  const input = JSON.parse(readFileSync(path, 'utf8'));
  assert.match(input.importerCommit, /^[a-f0-9]{40}$/);
  assert.equal(git(input.importerCheckout, 'rev-parse', 'HEAD'), input.importerCommit);
  assert.equal(git(input.importerCheckout, 'status', '--porcelain'), '');
  const { loadSnapshot } = await import(pathToFileURL(join(input.importerCheckout, 'src/c4/snapshot.mjs')).href);
  const snapshot = loadSnapshot({
    divisionRoot: input.divisionRoot, catalogCommit: input.catalogCommit,
    checkouts: new Map(Object.entries(input.checkouts)),
  });
  assert.equal(snapshot.id.catalogCommit, input.catalogCommit);
  const source = snapshot.sources.get(input.sourceId);
  assert.equal(source?.commit, input.sourceCommit);
  return { path, input, snapshot, source };
}

function compareSource(path) {
  const env = { ...process.env, B23_SNAPSHOT: path };
  delete env.NODE_TEST_CONTEXT;
  const result = execFileSync(process.execPath, ['--test', '--test-reporter=tap', 'tests/design-links-division-acceptance.test.mjs'], {
    cwd: kitCheckout, env, encoding: 'utf8',
  });
  assert.match(result, /^# pass 1$/m);
  assert.match(result, /^# skipped 0$/m);
}

function compareRoutes(graph, behaviour, symbol, component, file) {
  const node = [...graph.nodes.values()].find(node => node.kind === kit + 'behaviour' && node.label.startsWith(behaviour + ' '));
  assert.ok(node, behaviour);
  const assertions = [...graph.assertions.values()];
  const implementation = assertions.filter(edge => edge.object === node.iri && edge.relationship === kit + 'implements');
  const implementers = implementation.map(edge => graph.nodes.get(edge.subject));
  assert.ok(implementers.some(node => node.kind === kit + 'source-symbol' && node.label === symbol), symbol);
  const runner = implementers.find(node => node.kind === kit + 'component' && node.label === component);
  assert.ok(runner, component);
  const containment = assertions.filter(edge => edge.subject === runner.iri && edge.relationship === kit + 'contains');
  assert.ok(containment.some(edge => {
    const target = graph.nodes.get(edge.object);
    return target.kind === kit + 'source-file' && target.label === file;
  }), file);
}

test('B1 imports standard kit schema 1 with readable source records', async () => {
  const { source } = await load('kit-v1');
  const graph = source.graphState;
  assert.equal(graph.status, 'available');
  assert.equal(graph.schemaVersion, 1);
  assert.deepEqual([graph.nodes.size, graph.assertions.size, graph.evidence.size, graph.coverage.length], [7, 6, 6, 8]);
  assert.ok([...graph.evidence.values()].some(item => item.path === 'src/Invoice.cs' && item.role === 'implementation'));
});

test('B2 preserves the three legacy dummy graphs', async () => {
  const { snapshot } = await load('dummy-v1');
  assert.deepEqual([...snapshot.sources.keys()], ['fixture/accounting', 'fixture/ecommerce', 'fixture/warehouse']);
  for (const { graphState, id } of snapshot.sources.values()) {
    assert.equal(graphState.status, 'available', id);
    assert.equal(graphState.schemaVersion, 1, id);
    assert.deepEqual([graphState.nodes.size, graphState.assertions.size, graphState.evidence.size, graphState.coverage.length],
      [4, 3, 1, 3], id);
    const [evidence] = graphState.evidence.values();
    assert.equal(evidence.path, 'docs/architecture.md', id);
    assert.equal(evidence.role, 'implementation', id);
  }
});

test('B3 B8 B9 B11 B12 preserve and trace the pinned Widgets graph', async () => {
  const { path, input, source } = await load('local-b23');
  const graph = source.graphState;
  assert.equal(graph.schemaVersion, 2);
  assert.notEqual(git(input.checkouts[input.sourceId], 'rev-parse', 'HEAD'), input.sourceCommit);
  assert.ok([...graph.evidence.values()].some(item => item.path === 'docs/features/payments/feature.md'));
  compareSource(path);
  compareRoutes(graph, 'payments#B1', 'PayInvoice', 'Ledger', 'src/Invoice.cs');
});

if (trial) {
  test('B3 B9 B12 preserve the deployed JEPA-Speech design and both B18 routes', async () => {
    const { path, input, source } = await load('deployed-b23');
    assert.equal(input.sourceId, 'jamiemitchellconsultants/JEPA-Speech');
    assert.equal(input.sourceCommit, '3bcf7e7f5926e42231ae5dbfd9275f8166cc952c');
    assert.equal(source.graphState.schemaVersion, 2);
    compareSource(path);
    compareRoutes(source.graphState, 'zener-shape-tracing-motor-test#B18', 'Submit', 'MotorBlockRunner',
      'src/JepaSpeech.Study/Motor/MotorBlockRunner.cs');
  });
}
