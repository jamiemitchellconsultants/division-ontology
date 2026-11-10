import { after, before, test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtempSync, readFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { teardown } from '../fixtures/representative/setup.mjs';
import { accounting, address, api, bank, git, key, order, shop, store, unresolved, warehouse } from './c4-fixture.mjs';
import { description, generateWalkthrough, impactReason, question, rejectionReason, requestId } from './c4-walkthrough.mjs';

const parent = mkdtempSync(join(tmpdir(), 'division-c4-acceptance-'));
const root = join(parent, 'fixture');
let fixture;
const bytes = path => readFileSync(join(root, 'reports', path));
const read = path => JSON.parse(bytes(path));
const view = (folder, level) => read(folder + '/view-' + level + '.json');
const find = (view, key) => view.items.find(item => item.key.nodeIri === key.nodeIri);
const gap = (result, code) => assert.ok(result.gaps.some(gap => gap.code === code), 'Missing ' + code);
const sourceCommit = (result, sourceId) => result.snapshotId.sources.find(source => source.id === sourceId).commit;
const labels = result => result.items.map(item => item.label).sort();
const assessmentText = () => bytes('B03-assessment/assessment.md').toString('utf8');

function assertPinnedEvidence(report, sourceId, claim) {
  assert.equal(claim.commit, sourceCommit(report, sourceId));
  assert.ok(claim.assertionIri);
  assert.ok(claim.evidence.length);
  const repo = fixture.checkouts.get(sourceId);
  for (const evidence of claim.evidence) {
    assert.equal(evidence.blobId, git(repo, 'rev-parse', claim.commit + ':' + evidence.path));
  }
}

before(async () => { fixture = await generateWalkthrough(root); });
after(() => {
  if (fixture) teardown(root);
  rmSync(parent, { recursive: true, force: true });
});

test('B1 finds a description lead and exact IRI and exposes an unmatched query', () => {
  const description = read('B01-description/candidates.json');
  const matched = description.leads.find(lead => lead.matchedText === 'Order processing');
  assert.ok(matched.supported);
  assert.equal(matched.matchKind, 'token');
  assert.deepEqual(matched.key, order);
  assert.equal(matched.sourceCommit, fixture.oldSource);
  const unmatched = read('B01-unmatched/candidates.json');
  assert.deepEqual(unmatched.leads, []);
  gap(unmatched, 'unknown-search');
  const exact = read('B01-exact/candidates.json');
  assert.deepEqual(exact.leads.map(lead => [lead.key, lead.matchKind]), [[order, 'exact-iri']]);
  assert.equal(exact.leads[0].sourceCommit, fixture.oldSource);
});

test('B2 traces the three systems through separate directed assertions and bindings', () => {
  const report = read('B02-paths/paths.json');
  const path = report.paths.find(path => path.steps.at(-1)?.to?.nodeIri === accounting.nodeIri);
  assert.deepEqual(path.steps.map(step => step.type), ['assertion', 'binding', 'assertion', 'binding']);
  const [first, firstBinding, second, secondBinding] = path.steps;
  assert.deepEqual([first.subject, firstBinding.to, second.subject, secondBinding.to], [shop, warehouse, warehouse, accounting]);
  for (const [assertion, binding] of [[first, firstBinding], [second, secondBinding]]) {
    assert.deepEqual(assertion.object, binding.from);
    assert.equal(assertion.direction, 'outgoing');
    assert.equal(assertion.commit, sourceCommit(report, assertion.subject.sourceId));
    assert.equal(binding.registryCommit, fixture.baseline);
    assert.ok(assertion.evidence.length);
    for (const e of [...assertion.evidence, binding.identityEvidence]) {
      const repo = fixture.checkouts.get(assertion.subject.sourceId);
      assert.equal(e.blobId, git(repo, 'rev-parse', assertion.commit + ':' + e.path));
    }
  }
  assert.ok(report.paths.every(path => path.steps.every(step =>
    step.type !== 'assertion' || step.subject.sourceId === step.object.sourceId)));
});

test('B3 renders a reviewable assessment with the request, evidence, rejection and question', () => {
  const text = assessmentText();
  assert.ok(text.includes(requestId));
  assert.ok(text.includes(description));
  assert.ok(text.includes(fixture.baseline));
  for (const value of [shop.nodeIri, bank.nodeIri, accounting.nodeIri, impactReason, rejectionReason, question]) {
    assert.ok(text.includes(value), value);
  }
  const path = read('B02-paths/paths.json').paths.find(path => path.steps.at(-1)?.to?.nodeIri === accounting.nodeIri);
  assert.ok(text.includes(path.id));
  for (const evidence of path.steps.flatMap(step => step.evidenceIris ?? [])) assert.ok(text.includes(evidence));
  assert.doesNotMatch(text, /approval status|^approved:/im);
});

test('B4 renders qualitative feedback linked to the assessment digest', () => {
  const text = bytes('B04-feedback/feedback.md').toString('utf8');
  const digestLine = assessmentText().match(/^Assessment digest: [a-f0-9]{64}$/m)[0];
  for (const line of [digestLine, 'Sample architect', 'Sample reviewer', 'perceived Accuracy',
    'perceived Completeness', 'confidence', 'useful Discoveries', 'suspected Misses',
    'false Leads', 'examples', 'open Questions', question]) {
    assert.ok(text.includes(line), line);
  }
  const input = JSON.parse(readFileSync(join(root, 'feedback.json'), 'utf8'));
  for (const [field, value] of Object.entries(input)) {
    if (field === 'schemaVersion') continue;
    for (const entry of Array.isArray(value) ? value : [value]) {
      assert.ok(text.includes(entry), field + ' was omitted from the report');
    }
  }
});

test('B5 context shows the system, shopper and Warehouse within supported relationships', () => {
  const context = view('B05-context', 'context');
  assert.deepEqual(context.boundary, shop);
  assert.deepEqual(labels(context), ['Ecommerce', 'Shopper', 'Warehouse fulfilment']);
  assert.ok(find(context, shop).insideBoundary);
  assert.equal(find(context, key('ecommerce', 'shopper')).role, 'person');
  assert.equal(find(context, key('ecommerce', 'shopper')).insideBoundary, false);
  assert.equal(find(context, key('ecommerce', 'external')).insideBoundary, false);
  assert.deepEqual(context.edges.map(edge => [edge.from.nodeIri, edge.to.nodeIri]).sort(), [
    [key('ecommerce', 'shopper').nodeIri, shop.nodeIri],
    [shop.nodeIri, key('ecommerce', 'external').nodeIri]
  ]);
  for (const edge of context.edges) assertPinnedEvidence(context, edge.from.sourceId, edge);
});

test('B6 container view includes Storefront API, Order store and their evidenced communication', () => {
  const containers = view('B06-container', 'container');
  assert.deepEqual(containers.boundary, shop);
  for (const [key, label] of [[api, 'Storefront API'], [store, 'Order store']]) {
    const item = find(containers, key);
    assert.equal(item.label, label);
    assert.equal(item.role, 'container');
    assert.ok(item.insideBoundary);
    assertPinnedEvidence(containers, key.sourceId, item);
  }
  const writes = containers.edges.find(edge => edge.from.nodeIri === api.nodeIri && edge.to.nodeIri === store.nodeIri);
  assert.ok(writes);
  assertPinnedEvidence(containers, api.sourceId, writes);
});

test('B7 component view keeps the connected Order store outside the API boundary', () => {
  const components = view('B07-component', 'component');
  assert.deepEqual(components.boundary, api);
  assert.deepEqual(components.items.filter(item => item.insideBoundary).map(item => item.label).sort(),
    ['Address validation', 'Order processing']);
  assert.equal(find(components, store).insideBoundary, false);
  const reads = components.edges.find(edge => edge.from.nodeIri === address.nodeIri && edge.to.nodeIri === store.nodeIri);
  assert.ok(reads);
  assertPinnedEvidence(components, address.sourceId, reads);
});

test('B8 links graph-backed and inspected code to the pinned source and shows unavailable code', () => {
  const graph = read('B08-code/graph-code.json');
  const source = read('B08-code/pinned-code.json');
  const graphItem = graph.items.find(item => item.label === 'handle');
  assert.equal(graphItem.commit, source.commit);
  assert.equal(graphItem.evidence[0].path, source.path);
  assert.equal(graphItem.evidence[0].blobId, source.blobId);
  assert.deepEqual([graphItem.evidence[0].startLine, graphItem.evidence[0].endLine], [1, 1]);
  assert.equal(source.content, 'export const api = 1;');
  const inspected = view('B08-code', 'code').items.find(item => item.basis === 'architect-inspected');
  assert.equal(inspected.commit, source.commit);
  assert.equal(inspected.evidence[0].blobId, source.blobId);
  assert.deepEqual([inspected.evidence[0].startLine, inspected.evidence[0].endLine], [1, 1]);
  const missing = view('B08-code-gap', 'code');
  assert.deepEqual(missing.items, []);
  assert.deepEqual(missing.edges, []);
  gap(missing, 'empty-level');
});

test('B9 moves down supported levels with the same snapshot and reports the missing next level', () => {
  const context = view('B05-context', 'context');
  const containers = view('B06-container', 'container');
  const components = view('B07-component', 'component');
  const missing = view('B08-code-gap', 'code');
  assert.ok(context.nextFocus.some(next => next.level === 'container' && next.focus.nodeIri === shop.nodeIri));
  assert.ok(containers.nextFocus.some(next => next.level === 'component' && next.focus.nodeIri === api.nodeIri));
  for (const result of [containers, components, missing]) {
    assert.deepEqual(result.snapshotId, context.snapshotId);
    assert.equal(result.registryCommit, context.registryCommit);
  }
  assert.deepEqual(missing.focus, order);
  gap(missing, 'empty-level');
});

test('B10 exposes pinned evidence, tentative inference and unsupported mapping', () => {
  const components = view('B07-component', 'component');
  const item = find(components, order);
  assert.deepEqual(item.key, order);
  assert.equal(item.commit, fixture.oldSource);
  assert.equal(item.assertionIri, key('ecommerce', 'contains-container').nodeIri);
  assert.equal(item.evidence[0].path, 'docs/architecture.md');
  assertPinnedEvidence(components, order.sourceId, item);
  const inferred = components.edges.find(edge => edge.assertionIri === key('ecommerce', 'validates').nodeIri);
  assert.equal(inferred.basis, 'inferred');
  assert.equal(inferred.commit, fixture.oldSource);
  assert.equal(inferred.evidence[0].path, 'docs/acceptance-architecture.md');
  assertPinnedEvidence(components, order.sourceId, inferred);
  assert.match(bytes('B07-component/view-component.md').toString(), /dashed relationships are inferred and tentative/);
  assert.ok(components.gaps.some(gap => gap.code === 'stale-mapping' && gap.nodeIri === unresolved.nodeIri));
  assert.equal(find(components, unresolved), undefined);
});

test('B11 equal Order labels keep three separate source identifiers and revisions', () => {
  const result = read('B11-order-labels/candidates.json');
  const orders = result.leads.filter(lead => lead.kind === 'concept' && lead.matchedText.startsWith('Order — '));
  assert.deepEqual(orders.map(lead => lead.key.sourceId).sort(), [accounting.sourceId, shop.sourceId, warehouse.sourceId]);
  assert.equal(new Set(orders.map(lead => lead.key.nodeIri)).size, 3);
  assert.equal(new Set(orders.map(lead => lead.sourceCommit)).size, 3);
  for (const lead of orders) assert.equal(lead.sourceCommit, sourceCommit(result, lead.key.sourceId));
});

test('B12 reproduces an older catalog byte for byte and reads the revised label at the new revision', () => {
  for (const extension of ['md', 'json']) {
    assert.ok(bytes('B06-container/view-container.' + extension).equals(bytes('B12-repeat/view-container.' + extension)));
  }
  const baseline = view('B06-container', 'container');
  const newer = view('B12-newer', 'container');
  assert.equal(baseline.snapshotId.catalogCommit, fixture.baseline);
  assert.equal(newer.snapshotId.catalogCommit, fixture.newer);
  assert.equal(find(baseline, store).label, 'Order store');
  assert.equal(find(baseline, store).commit, fixture.oldSource);
  assert.equal(find(newer, store).label, 'Order store v2');
  assert.equal(find(newer, store).commit, fixture.newSource);
});

test('B13 retains available facts and reports an empty path and unknown and excluded scopes', () => {
  const components = view('B07-component', 'component');
  assert.ok(components.items.length);
  for (const code of ['coverage-unknown', 'coverage-excluded']) gap(components, code);
  const result = read('B13-empty-path/paths.json');
  assert.deepEqual(result.paths, []);
  for (const code of ['unknown-paths', 'coverage-unknown', 'coverage-excluded']) gap(result, code);
  assert.match(result.gaps.find(gap => gap.code === 'unknown-paths').reason, /do not prove no impact/);
});

test('B14 shows External bank as context and excludes it from inventory and coverage', () => {
  const context = view('B14-bank', 'context');
  assert.equal(find(context, bank).label, 'External bank');
  assert.equal(find(context, bank).insideBoundary, false);
  const payment = context.edges.find(edge => edge.from.nodeIri === accounting.nodeIri && edge.to.nodeIri === bank.nodeIri);
  assert.ok(payment);
  assertPinnedEvidence(context, accounting.sourceId, payment);
  const participants = [accounting.sourceId, shop.sourceId, warehouse.sourceId];
  assert.deepEqual(read('catalog.json').sources.map(source => source.id).sort(), participants);
  assert.deepEqual([...new Set(context.coverage.map(coverage => coverage.sourceId))].sort(), participants);
});

test('B15 catalog retains all source concepts while Ecommerce has no code-level coverage', () => {
  const catalog = read('catalog.json');
  assert.deepEqual(catalog.sources.map(source => [source.id, source.conceptCount]).sort(),
    [[accounting.sourceId, 9], [shop.sourceId, 8], [warehouse.sourceId, 8]]);
  assert.ok(find(view('B05-context', 'context'), shop));
  const missing = view('B08-code-gap', 'code');
  gap(missing, 'empty-level');
  assert.ok(missing.coverage.some(entry => entry.sourceId === shop.sourceId &&
    entry.selector === 'src/**' && entry.status === 'unknown'));
});
