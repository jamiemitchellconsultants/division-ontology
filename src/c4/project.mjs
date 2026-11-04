import {coverageGaps,coverageInventory} from './graph.mjs';
import {mappingFor} from './registry.mjs';
import {verifyCodeSupplement} from './code-evidence.mjs';
const levels=['context','container','component','code'];
const same=(a,b)=>a?.sourceId===b?.sourceId && a?.nodeIri===b?.nodeIri;
const cmp=(a,b)=>a<b?-1:a>b?1:0;
const id=x=>JSON.stringify([x.sourceId,x.nodeIri]);
export function projectView({snapshot,registry,focus,level,supplement=null}) {
 if(!levels.includes(level)) throw new Error('invalid C4 level');
 const gaps=[...(registry.gaps??[]),...coverageGaps(snapshot)], focusMapping=mappingFor(registry,focus.sourceId,focus.nodeIri);
 const expected={context:'context',container:'context',component:'container',code:'component'}[level];
 const view={snapshotId:snapshot.id,registryCommit:registry.commit,registryDigests:{mappings:registry.mappingSha256,bindings:registry.bindingSha256},focus,level,boundary:focus,coverage:coverageInventory(snapshot),items:[],edges:[],nextFocus:[],gaps};
 if(!focusMapping || focusMapping.level!==expected) {gaps.push({code:'unsupported-focus',...focus,reason:'Focus lacks the required supported mapping'}); return view;}
 const selected=level==='context' ? registry.mappings.filter(m=>m.level==='context' && (same(m,focus) || (m.sourceId===focus.sourceId && [...snapshot.sources.get(m.sourceId).graphState.assertions.values()].some(a=>(a.subject===focus.nodeIri && a.object===m.nodeIri)||(a.object===focus.nodeIri && a.subject===m.nodeIri))))) : registry.mappings.filter(m=>m.level===level && same(m.parent,focus));
 if(level==='container' || level==='component') {
  const inner=new Set(selected.map(m=>id(m)));
  for(const m of registry.mappings) {
   if(inner.has(id(m)) || same(m,focus)) continue;
   const graph=snapshot.sources.get(m.sourceId)?.graphState;
   if([...graph?.assertions?.values() ?? []].some(a=>a.relationship!=='https://ontology-kit.dev/schema#contains' && ((inner.has(id({sourceId:m.sourceId,nodeIri:a.subject})) && a.object===m.nodeIri)||(inner.has(id({sourceId:m.sourceId,nodeIri:a.object})) && a.subject===m.nodeIri)))) selected.push(m);
  }
 }
 for(const m of selected) {
  const source=snapshot.sources.get(m.sourceId), graph=source.graphState, node=graph.nodes.get(m.nodeIri), assertion=graph.assertions.get(m.supportAssertionIri), evidence=graph.evidence.get(m.supportEvidenceIri);
  if(!node || !assertion || !evidence) {gaps.push({code:'unsupported-node',sourceId:m.sourceId,nodeIri:m.nodeIri,reason:'Mapping support is unavailable'});continue;}
  const item={key:{sourceId:m.sourceId,nodeIri:m.nodeIri},label:node.label,role:m.role,insideBoundary:level==='context' ? same(m,focus) : same(m.parent,focus),supported:true,assertionIri:m.supportAssertionIri,evidenceIris:[m.supportEvidenceIri],evidence:[evidence],commit:source.commit};
  view.items.push(item);
  const index=levels.indexOf(level); if(index<3 && registry.mappings.some(x=>same(x.parent,item.key) && x.level===levels[index+1])) view.nextFocus.push({focus:item.key,level:levels[index+1]});
 }
 if(supplement) {
  if(level!=='code') throw new Error('code supplement requires a code view');
  const inspected=verifyCodeSupplement({snapshot,focus,supplement});
  view.items.push({key:{sourceId:inspected.sourceId,nodeIri:`inspected:${inspected.blobId}:${inspected.startLine}-${inspected.endLine}`},label:inspected.path,role:'code',insideBoundary:true,supported:true,basis:inspected.basis,path:inspected.path,evidenceIris:[],evidence:[inspected],commit:inspected.commit});
 }
 const included=new Set(view.items.map(x=>id(x.key)));
 for(const [sourceId,source] of snapshot.sources) for(const a of source.graphState?.assertions?.values() ?? []) {
  const from={sourceId,nodeIri:a.subject},to={sourceId,nodeIri:a.object};
  if(included.has(id(from)) && included.has(id(to))) view.edges.push({from,to,assertionIri:a.iri,relationship:a.relationship,basis:a.basis,evidenceIris:a.evidenceIris,evidence:a.evidenceIris.map(i=>source.graphState.evidence.get(i)),commit:source.commit});
 }
 view.items.sort((a,b)=>cmp(id(a.key),id(b.key))); view.edges.sort((a,b)=>cmp(a.assertionIri,b.assertionIri));
 if(!view.items.length) gaps.push({code:'empty-level',reason:'No supported items at this level; coverage is unknown'});
 return view;
}
