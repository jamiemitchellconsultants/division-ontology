import test from 'node:test';
import assert from 'node:assert/strict';
import {makeModel} from './fixtures/c4/fixture-repos.mjs';
import {readImpactPaths} from '../src/c4/paths.mjs';
test('cross-source path has distinct inferred assertion and binding', () => {
 const {snapshot,registry,keys}=makeModel(), r=readImpactPaths({snapshot,registry,starts:[keys.alphaSystem],maxDepth:5});
 const p=r.paths.find(x=>x.steps.some(s=>s.type==='binding'));
 assert.ok(p); assert.ok(p.steps.some(s=>s.type==='assertion' && s.basis==='inferred')); assert.ok(p.steps.every(s=>s.type==='binding' || s.assertionIri));
});
test('equal labels without binding produce no cross-source step', () => {
 const {snapshot,registry,keys}=makeModel(), r=readImpactPaths({snapshot,registry:{...registry,bindings:[]},starts:[keys.externalBeta]});
 assert.ok(!r.paths.some(p=>p.steps.some(s=>s.type==='binding'))); assert.ok(r.gaps.length);
});
test('preserves direction, bounds cycles and reports unknown empty paths', () => {
 const {snapshot,registry,keys}=makeModel(), graph=snapshot.sources.get('fixture/Alpha').graphState;
 graph.assertions.set('reverse',{iri:'reverse',subject:keys.alphaContainer.nodeIri,object:keys.alphaSystem.nodeIri,relationship:'cycle',basis:'direct',evidenceIris:['e']});
 const r=readImpactPaths({snapshot,registry,starts:[keys.alphaSystem],maxDepth:6});
 assert.ok(r.paths.every(p=>p.steps.length<=6)); assert.ok(r.paths.every(p=>!p.steps.some(s=>s.assertionIri==='reverse')));
 assert.throws(()=>readImpactPaths({snapshot,registry,starts:[keys.alphaSystem],maxDepth:7}),/depth/i);
});
test('stale bindings remain gaps and never become steps', () => {
 const {snapshot,registry,keys}=makeModel();
 const r=readImpactPaths({snapshot,registry:{...registry,bindings:[],gaps:[{code:'stale-binding',reason:'identity changed'}]},starts:[keys.alphaSystem]});
 assert.ok(r.gaps.some(g=>g.code==='stale-binding')); assert.ok(r.paths.every(p=>p.steps.every(s=>s.type!=='binding')));
});
test('partial graph coverage remains visible even when paths exist',()=>{
 const {snapshot,registry,keys}=makeModel();snapshot.sources.get('fixture/Alpha').graphState.coverage.push({iri:'gap',selector:'legacy/**',status:'excluded',priority:5,reason:'Not reviewed'});
 const r=readImpactPaths({snapshot,registry,starts:[keys.alphaSystem]});assert.ok(r.paths.length);assert.ok(r.gaps.some(g=>g.reason==='Not reviewed' && g.selector==='legacy/**'));
});
