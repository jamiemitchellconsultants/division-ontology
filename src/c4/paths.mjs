import {coverageGaps} from './graph.mjs';
import {createHash} from 'node:crypto';
import {mappingFor,bindingFrom} from './registry.mjs';
const key=x=>JSON.stringify([x.sourceId,x.nodeIri]);
const cmp=(a,b)=>a<b?-1:a>b?1:0;
export function readImpactPaths({snapshot,registry,starts,maxDepth=4}) {
 if(!Number.isInteger(maxDepth)||maxDepth<1||maxDepth>6) throw new Error('depth must be from 1 to 6');
 if(!Array.isArray(starts)||!starts.length) throw new Error('at least one start is required');
 const paths=[],gaps=[...(registry.gaps??[]),...coverageGaps(snapshot)];
 function walk(current,steps,visited) {
  if(steps.length>=maxDepth) return;
  const source=snapshot.sources.get(current.sourceId), graph=source?.graphState;
  if(!graph || graph.status!=='available') {gaps.push({code:'graph-unavailable',sourceId:current.sourceId,reason:'Graph unavailable'});return;}
  const edges=[];
  for(const assertion of graph.assertions.values()) if(assertion.subject===current.nodeIri) {
   const target={sourceId:current.sourceId,nodeIri:assertion.object};
   edges.push({target,step:{type:'assertion',subject:current,object:target,direction:'outgoing',assertionIri:assertion.iri,relationship:assertion.relationship,basis:assertion.basis,evidenceIris:assertion.evidenceIris,evidence:assertion.evidenceIris.map(i=>graph.evidence.get(i)),commit:source.commit}});
  }
  const binding=bindingFrom(registry,current.sourceId,current.nodeIri);
  if(binding) {const target={sourceId:binding.toSourceId,nodeIri:binding.targetNodeIri}; edges.push({target,step:{type:'binding',from:current,to:target,registryCommit:registry.commit,identityEvidence:binding.identityEvidence}});}
  edges.sort((a,b)=>cmp(key(a.target),key(b.target)) || cmp(a.step.assertionIri??'',b.step.assertionIri??''));
  for(const {target,step} of edges) {
   if(visited.has(key(target))) continue;
   const next=[...steps,step], id=createHash('sha256').update(JSON.stringify(next)).digest('hex');
   paths.push({id,steps:next}); const seen=new Set(visited); seen.add(key(target)); walk(target,next,seen);
  }
 }
 for(const start of [...starts].sort((a,b)=>cmp(key(a),key(b)))) {
  if(!mappingFor(registry,start.sourceId,start.nodeIri)) {gaps.push({code:'unsupported-start',...start,reason:'Start lacks a supported mapping'});continue;}
  walk(start,[],new Set([key(start)]));
 }
 if(!paths.length) gaps.push({code:'unknown-paths',reason:'No paths found; missing relationships do not prove no impact'});
 return {snapshotId:snapshot.id,registryCommit:registry.commit,paths,gaps};
}
