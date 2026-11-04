import test from 'node:test';
import assert from 'node:assert/strict';
import {withPilotFixture} from './fixtures/c4/fixture-repos.mjs';
import {loadSnapshot} from '../src/c4/snapshot.mjs';
import {loadSourceGraph,coverageFor} from '../src/c4/graph.mjs';
test('preserves inferred assertion, evidence and coverage', async () => {
 await withPilotFixture(async f => {
  const s=loadSnapshot(f).sources.get('fixture/Alpha'), g=loadSourceGraph(s);
  const a=[...g.assertions.values()].find(x=>x.relationship.endsWith('#communicates-with'));
  assert.equal(a.basis,'inferred'); assert.ok(g.evidence.has(a.evidenceIris[0]));
  assert.equal(coverageFor(g,'src/app.ts').status,'covered');
  assert.equal(coverageFor(g,'unknown/file').status,'unknown');
 });
});
test('disabled graph is unavailable', async () => {
 await withPilotFixture(async f => { const s=loadSnapshot(f).sources.get('fixture/Alpha'); assert.equal(loadSourceGraph({...s,config:{sourceGraph:{enabled:false}}}).status,'unavailable'); });
});
test('highest coverage priority wins and equal conflicts fail', () => {
 const graph={coverage:[{iri:'a',selector:'src/**',priority:1,status:'covered'},{iri:'b',selector:'src/private/**',priority:2,status:'excluded',reason:'private'}]};
 assert.equal(coverageFor(graph,'src/private/a.ts').status,'excluded');
 graph.coverage.push({iri:'c',selector:'src/private/**',priority:2,status:'unknown',reason:'unknown'});
 assert.throws(()=>coverageFor(graph,'src/private/a.ts'),/conflict/i);
});
test('stale evidence is rejected at the pinned commit', async () => {
 await withPilotFixture(async f => { f.writeSource('fixture/Alpha','src/app.ts','changed\n'); const commit=f.commitSource('fixture/Alpha','stale'); const s=loadSnapshot(f).sources.get('fixture/Alpha'); assert.throws(()=>loadSourceGraph({...s,commit}),/blob/i); });
});
test('malformed graph is rejected', async () => {
 await withPilotFixture(async f => { f.writeSource('fixture/Alpha','graph/broken.ttl','not turtle'); const commit=f.commitSource('fixture/Alpha','bad'); const s=loadSnapshot(f).sources.get('fixture/Alpha'); assert.throws(()=>loadSourceGraph({...s,commit}),/turtle|parse/i); });
});

test('Terraform-only deployment role rejects a non-Terraform citation', async () => {
 await withPilotFixture(async f => {
  const id='fixture/Alpha', s=loadSnapshot(f).sources.get(id);
  const ttl=f.readSource(id,'graph/manifest.ttl').replace('#implementation>', '#deployment>');
  f.writeSource(id,'graph/manifest.ttl',ttl);
  f.writeSource(id,'ontology.config.json',JSON.stringify({sourceGraph:{enabled:true,root:'graph',evidence:{deployment:['**/*.tf']}}}));
  const commit=f.commitSource(id,'deployment selector');
  assert.throws(()=>loadSourceGraph({...s,commit,config:{sourceGraph:{enabled:true,root:'graph',evidence:{deployment:['**/*.tf']}}}}),/role selector/i);
 });
});
