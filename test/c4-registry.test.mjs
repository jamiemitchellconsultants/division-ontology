import test from 'node:test';
import assert from 'node:assert/strict';
import {withPilotFixture} from './fixtures/c4/fixture-repos.mjs';
import {loadSnapshot} from '../src/c4/snapshot.mjs';
import {loadRegistries,mappingFor,bindingFrom} from '../src/c4/registry.mjs';
test('supported mapping and binding keep both identities', async () => {
 await withPilotFixture(async f => {
  const r=loadRegistries({...f,snapshot:loadSnapshot(f)});
  assert.equal(mappingFor(r,'fixture/Alpha',f.keys.alphaSystem.nodeIri).level,'context');
  const b=bindingFrom(r,'fixture/Alpha',f.keys.externalBeta.nodeIri);
  assert.equal(b.toSourceId,'fixture/Beta'); assert.notEqual(b.externalNodeIri,b.targetNodeIri);
 });
});
test('stale mapping support becomes unresolved', async () => {
 await withPilotFixture(async f => {
  const m=JSON.parse(f.readDivision('c4/mappings.json')); m.mappings[0].supportEvidenceIri='https://alpha.example/#missing';
  f.writeDivision('c4/mappings.json',JSON.stringify(m)); const registryCommit=f.commitDivision('stale registry');
  const r=loadRegistries({...f,registryCommit,snapshot:loadSnapshot(f)});
  assert.equal(mappingFor(r,'fixture/Alpha',f.keys.alphaSystem.nodeIri),undefined); assert.ok(r.gaps.some(g=>g.code==='stale-mapping'));
 });
});
test('duplicate mapping is rejected', async () => {
 await withPilotFixture(async f=>{const m=JSON.parse(f.readDivision('c4/mappings.json')); m.mappings.push({...m.mappings[0]}); f.writeDivision('c4/mappings.json',JSON.stringify(m)); const registryCommit=f.commitDivision('duplicate'); assert.throws(()=>loadRegistries({...f,registryCommit,snapshot:loadSnapshot(f)}),/duplicate/i);});
});
test('unjoined target and stale identity are unresolved', async () => {
 await withPilotFixture(async f=>{const b=JSON.parse(f.readDivision('c4/bindings.json')); b.bindings[0].toSourceId='fixture/Unjoined'; f.writeDivision('c4/bindings.json',JSON.stringify(b)); let registryCommit=f.commitDivision('unjoined'); let r=loadRegistries({...f,registryCommit,snapshot:loadSnapshot(f)}); assert.equal(bindingFrom(r,'fixture/Alpha',f.keys.externalBeta.nodeIri),undefined); assert.ok(r.gaps.length); b.bindings[0].toSourceId='fixture/Beta'; b.bindings[0].identityEvidence.blobId='0'.repeat(40); f.writeDivision('c4/bindings.json',JSON.stringify(b)); registryCommit=f.commitDivision('stale identity'); r=loadRegistries({...f,registryCommit,snapshot:loadSnapshot(f)}); assert.equal(bindingFrom(r,'fixture/Alpha',f.keys.externalBeta.nodeIri),undefined); assert.ok(r.gaps.length);});
});
test('a mapping with the wrong node kind is unresolved', async () => {
 await withPilotFixture(async f=>{
  const id='fixture/Alpha';
  const ttl=f.readSource(id,'graph/manifest.ttl').replace('ontology-kit.dev/schema#system>', 'ontology-kit.dev/schema#component>');
  f.writeSource(id,'graph/manifest.ttl',ttl); const sourceCommit=f.commitSource(id,'change kind');
  const catalog=JSON.parse(f.readDivision('dist/catalog.json')); catalog.sources[0].commit=sourceCommit;
  f.writeDivision('dist/catalog.json',JSON.stringify(catalog)); const catalogCommit=f.commitDivision('new snapshot');
  const r=loadRegistries({...f,snapshot:loadSnapshot({...f,catalogCommit})});
  assert.equal(r.mappings.length,0); assert.ok(r.gaps.length);
 });
});
