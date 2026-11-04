import test from 'node:test';
import assert from 'node:assert/strict';
import {withPilotFixture} from './fixtures/c4/fixture-repos.mjs';
import {loadSnapshot} from '../src/c4/snapshot.mjs';
import {verifyCodeSupplement} from '../src/c4/code-evidence.mjs';
test('verified source line is visibly architect-inspected', async () => {
 await withPilotFixture(async f=>{const result=verifyCodeSupplement({snapshot:loadSnapshot(f),focus:f.keys.alphaComponent,supplement:f.codeSupplement()}); assert.equal(result.basis,'architect-inspected');assert.equal(result.commit,f.sourceCommits.alpha);assert.equal(result.path,'src/app.ts');});
});
test('wrong blob, focus and line range fail', async () => {
 await withPilotFixture(async f=>{const snapshot=loadSnapshot(f),good=f.codeSupplement();
 assert.throws(()=>verifyCodeSupplement({snapshot,focus:f.keys.alphaComponent,supplement:{...good,blobId:'0'.repeat(40)}}),/blob/i);
 assert.throws(()=>verifyCodeSupplement({snapshot,focus:f.keys.alphaComponent,supplement:{...good,endLine:9999}}),/line/i);
 assert.throws(()=>verifyCodeSupplement({snapshot,focus:f.keys.alphaSystem,supplement:good}),/focus|component/i);
 });
});
test('deployment evidence must match its configured selector', async () => {
 await withPilotFixture(async f=>{const snapshot=loadSnapshot(f),good=f.codeSupplement(); snapshot.sources.get('fixture/Alpha').config.sourceGraph.evidence.deployment=['**/*.tf']; assert.throws(()=>verifyCodeSupplement({snapshot,focus:f.keys.alphaComponent,supplement:{...good,role:'deployment'}}),/role|selector/i);});
});
test('code view includes only verified architect-inspected evidence', async () => {
 await withPilotFixture(async f=>{
  const {projectView}=await import('../src/c4/project.mjs'),snapshot=loadSnapshot(f),focus=f.keys.alphaComponent;
  const registry={commit:'a'.repeat(40),mappings:[{...focus,level:'component'}],bindings:[],gaps:[]};
  const view=projectView({snapshot,registry,focus,level:'code',supplement:f.codeSupplement()});
  assert.ok(view.items.some(item=>item.basis==='architect-inspected' && item.path==='src/app.ts'));
  assert.throws(()=>projectView({snapshot,registry,focus,level:'code',supplement:{...f.codeSupplement(),blobId:'0'.repeat(40)}}),/blob/i);
 });
});
test('assessment can cite a verified inspected code view',async()=>{
 await withPilotFixture(async f=>{
  const {projectView}=await import('../src/c4/project.mjs');const {newAssessment,validateAssessment,viewDigest}=await import('../src/c4/assessment.mjs');
  const snapshot=loadSnapshot(f),focus=f.keys.alphaComponent,registry={commit:'a'.repeat(40),mappingSha256:'b',bindingSha256:'c',mappings:[{...focus,level:'component'}],bindings:[],gaps:[]};
  const view=projectView({snapshot,registry,focus,level:'code',supplement:f.codeSupplement()}),item=view.items.find(x=>x.basis==='architect-inspected');
  const a=newAssessment({requestId:'CR-1',description:'Change',snapshot,registry});a.likelyImpacts=[{key:item.key,reason:'Inspected code',viewIds:[viewDigest(view)],pathIds:[],evidenceIris:[]}];
  assert.doesNotThrow(()=>validateAssessment(a,{snapshot,registry,paths:[],views:[view]}));
  assert.throws(()=>validateAssessment(a,{snapshot,registry,paths:[],views:[]}),/view/i);
 });
});
