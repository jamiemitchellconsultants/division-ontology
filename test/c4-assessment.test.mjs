import test from 'node:test';
import assert from 'node:assert/strict';
import {makeModel} from './fixtures/c4/fixture-repos.mjs';
import {newAssessment,validateAssessment,renderAssessment} from '../src/c4/assessment.mjs';
test('architect findings render with evidence and gaps',()=>{
 const {snapshot,registry,keys,paths,views}=makeModel(),a=newAssessment({requestId:'CR-1',description:'Change Alpha API',snapshot,registry});
 a.architect='Architect A';a.confirmedLeads=[keys.alphaSystem];a.rejectedLeads=[{key:keys.externalBeta,reason:'Out of scope'}];
 a.likelyImpacts=[{key:keys.alphaComponent,reason:'Call path',pathIds:[paths[0].id],evidenceIris:[paths[0].steps[0].evidenceIris[0]]}];a.unresolved=[{code:'partial-code',reason:'No graph code coverage'}];
 validateAssessment(a,{snapshot,registry,paths,views}); assert.match(renderAssessment(a,{snapshot,registry,paths,views}),/CR-1/);assert.equal('approved' in a,false);
});
test('stale snapshot, paths and evidence fail validation',()=>{
 const {snapshot,registry,keys,paths,views}=makeModel(),context={snapshot,registry,paths,views};
 const a=newAssessment({requestId:'CR-1',description:'Change',snapshot,registry}); a.snapshotId.catalogCommit='0'.repeat(40);
 assert.throws(()=>validateAssessment(a,context),/snapshot/i);
 const b=newAssessment({requestId:'CR-1',description:'Change',snapshot,registry}); b.likelyImpacts=[{key:keys.alphaComponent,reason:'x',pathIds:['missing'],evidenceIris:['missing']}];
 assert.throws(()=>validateAssessment(b,context),/path|evidence/i);
});
test('rendering requires an identified architect',()=>{
 const {snapshot,registry,paths,views}=makeModel(),a=newAssessment({requestId:'CR-1',description:'Change',snapshot,registry});
 assert.throws(()=>renderAssessment(a,{snapshot,registry,paths,views}),/architect/i);
});
test('unrelated paths cannot support an impacted element',()=>{
 const {snapshot,registry,keys,paths,views}=makeModel(),a=newAssessment({requestId:'CR-1',description:'Change',snapshot,registry});
 a.likelyImpacts=[{key:keys.betaSystem,reason:'unrelated',pathIds:[paths[0].id],evidenceIris:[paths[0].steps[0].evidenceIris[0]]}];
 assert.throws(()=>validateAssessment(a,{snapshot,registry,paths,views}),/path.*impact|impact.*path/i);
});
