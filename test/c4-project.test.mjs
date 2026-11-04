import test from 'node:test';
import assert from 'node:assert/strict';
import {makeModel} from './fixtures/c4/fixture-repos.mjs';
import {projectView} from '../src/c4/project.mjs';
test('all four levels retain focus and reviewed boundaries', () => {
 const {snapshot,registry,keys}=makeModel();
 for(const [level,focus] of [['context',keys.alphaSystem],['container',keys.alphaSystem],['component',keys.alphaContainer],['code',keys.alphaComponent]]) {
  const v=projectView({snapshot,registry,focus,level}); assert.equal(v.level,level); assert.ok(v.boundary); assert.ok(v.items.every(x=>x.supported && x.evidenceIris.length)); assert.ok(v.items.length);
 }
});
test('missing code mapping is an explicit gap', () => {
 const {snapshot,registry,keys}=makeModel(), r={...registry,mappings:registry.mappings.filter(x=>x.level!=='code')};
 const v=projectView({snapshot,registry:r,focus:keys.alphaComponent,level:'code'}); assert.equal(v.items.length,0); assert.ok(v.gaps.length);
});
test('external context is evidenced and retains source identity',()=>{
 const {snapshot,registry,keys}=makeModel(),v=projectView({snapshot,registry,focus:keys.alphaSystem,level:'context'});
 assert.ok(v.items.some(x=>x.key.nodeIri===keys.externalBeta.nodeIri)); assert.ok(v.edges.some(x=>x.basis==='inferred'));
 assert.ok(v.items.every(x=>x.key.sourceId!=='fixture/Unjoined'));
});
test('unsupported focus is a gap and other boundaries stay out',()=>{
 const {snapshot,registry,keys}=makeModel();
 const v=projectView({snapshot,registry:{...registry,mappings:registry.mappings.filter(x=>x.nodeIri!==keys.alphaContainer.nodeIri)},focus:keys.alphaContainer,level:'component'});
 assert.equal(v.items.length,0); assert.ok(v.gaps.length);
});
test('container view includes an evidenced directly connected external system',()=>{
 const {snapshot,registry,keys}=makeModel(),g=snapshot.sources.get('fixture/Alpha').graphState;
 g.assertions.set('link',{iri:'link',subject:keys.alphaContainer.nodeIri,object:keys.externalBeta.nodeIri,relationship:'communicates',basis:'direct',evidenceIris:['https://alpha.example/#evidence']});
 const v=projectView({snapshot,registry,focus:keys.alphaSystem,level:'container'});
 assert.equal(v.items.find(x=>x.key.nodeIri===keys.externalBeta.nodeIri).insideBoundary,false); assert.equal(v.items.find(x=>x.key.nodeIri===keys.alphaContainer.nodeIri).insideBoundary,true); assert.ok(v.edges.some(x=>x.assertionIri==='link'));
});
