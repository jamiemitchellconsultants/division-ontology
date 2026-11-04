import test from 'node:test';
import assert from 'node:assert/strict';
import {makeModel} from './fixtures/c4/fixture-repos.mjs';
import {findCandidates} from '../src/c4/candidates.mjs';
test('description gives leads without an impact conclusion', () => {
 const {snapshot,registry}=makeModel(), r=findCandidates({snapshot,registry,query:'change Alpha API'});
 assert.ok(r.leads.some(x=>x.key.sourceId==='fixture/Alpha')); assert.ok(r.leads.every(x=>!('impact' in x)));
});
test('exact IRI and no-match gap', () => {
 const {snapshot,registry,keys}=makeModel();
 assert.equal(findCandidates({snapshot,registry,exactIri:keys.alphaSystem.nodeIri}).leads[0].key.nodeIri,keys.alphaSystem.nodeIri);
 const empty=findCandidates({snapshot,registry,query:'unmatched phrase'}); assert.equal(empty.leads.length,0); assert.ok(empty.gaps.length);
});
test('catalog-only matches stay unsupported leads', () => {
 const {snapshot,registry}=makeModel(), r=findCandidates({snapshot,registry,query:'catalog only'});
 assert.ok(r.leads.some(x=>x.key.nodeIri==='https://alpha.example/concept' && x.supported===false));
});
test('equal labels remain separate source-owned leads', () => {
 const {snapshot,registry}=makeModel(), r=findCandidates({snapshot,registry,query:'Beta'});
 assert.ok(r.leads.some(x=>x.key.sourceId==='fixture/Alpha')); assert.ok(r.leads.some(x=>x.key.sourceId==='fixture/Beta'));
});
