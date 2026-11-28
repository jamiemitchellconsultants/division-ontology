import test from 'node:test';
import assert from 'node:assert/strict';
import { recordScenario } from './impact-corpus/capture.mjs';

// Runs the meaning scenario through runC4Cli once, then checks each case of I3's Task 21.
const steps = await recordScenario('meaning', { copyInputs: false });
const json = (index, name) => JSON.parse(steps[index].outputs[name].toString('utf8'));
const gaps = (result, code) => result.gaps.filter(g => g.code === code);
const crossings = result => result.paths.flatMap(p => p.steps).filter(s => s.type === 'reviewedLinkCrossing');

test('search finds supported graph leads', () => {
  const leads = json(0, 'candidates.json').leads.filter(l => l.kind === 'graph');
  assert.ok(leads.length > 0);
  assert.ok(leads.every(l => l.supported), JSON.stringify(leads.map(l => [l.key.nodeIri, l.reason])));
});

test('paths cross into the ledger and follow the master list (B6–B8, B10, B11)', () => {
  const r = json(1, 'paths.json');
  assert.equal(r.masterList.status, 'listed');
  assert.ok(crossings(r).some(s => s.to.sourceId === 'fixture/ledger'));
  const unlisted = gaps(r, 'unlisted-relationship');
  assert.equal(unlisted.length, 1);
  assert.equal(unlisted[0].relationship, 'https://example.test/rel#archivedBy');
  assert.deepEqual(r.notFollowed, [{ relationship: 'https://example.test/rel#mentions', assertions: 1 }]);
  assert.equal(gaps(r, 'no-relationship-master-list').length, 0);
  const directions = new Set(r.paths.flatMap(p => p.steps).map(s => s.direction).filter(Boolean));
  assert.deepEqual([...directions].sort(), ['object-to-subject', 'subject-to-object']);
});

test('the rendered assessment pins the master list (B10)', () => {
  const record = json(2, 'assessment.json');
  assert.equal(record.pins.masterList.status, 'listed');
  const md = steps[3].outputs['assessment.md'].toString('utf8');
  const { catalogCommit, sha256 } = record.pins.masterList;
  assert.match(sha256, /^[0-9a-f]{64}$/);
  assert.ok(md.includes(`Relationship master list: ${catalogCommit} (sha256 ${sha256})`), md);
});

test('removing the claims link stops the crossing and reports one unlinked term (B13)', () => {
  const r = json(4, 'paths.json');
  assert.equal(crossings(r).length, 0);
  const unlinked = gaps(r, 'unlinked-term');
  assert.equal(unlinked.length, 1);
  assert.equal(unlinked[0].term, 'https://example.test/claims/ontology#Claim');
});

test('a corrupted master list fails before any output', () => {
  assert.equal(steps[5].exit, 1);
  assert.equal(steps[5].stderr, 'invalid relationship master list\n');
  assert.deepEqual(steps[5].files, []);
});
