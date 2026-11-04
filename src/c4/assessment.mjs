import {createHash} from 'node:crypto';
import {projectView} from './project.mjs';
const canonical=x=>Array.isArray(x)?x.map(canonical):x && typeof x==='object'?Object.fromEntries(Object.keys(x).sort().map(k=>[k,canonical(x[k])])):x;
export const viewDigest=view=>createHash('sha256').update(JSON.stringify(canonical(view))+'\n').digest('hex');
const text=x=>typeof x==='string' && x.trim().length>0;
const escaped=x=>String(x??'').replace(/[\r\n]+/g,' ').replaceAll('|','&#124;').replaceAll('<','&lt;').replaceAll('>','&gt;');
export function newAssessment({requestId,description,snapshot,registry}) {
 if(!text(requestId)||!text(description)) throw new Error('request ID and description are required');
 return {schemaVersion:1,requestId,description,snapshotId:structuredClone(snapshot.id),registryCommit:registry.commit,registryDigests:{mappings:registry.mappingSha256,bindings:registry.bindingSha256},architect:'',confirmedLeads:[],rejectedLeads:[],likelyImpacts:[],unresolved:[]};
}
export function validateAssessment(record,{snapshot,registry,paths,views}) {
 if(record.schemaVersion!==1 || !text(record.requestId) || !text(record.description)) throw new Error('invalid assessment');
 if(JSON.stringify(canonical(record.snapshotId))!==JSON.stringify(canonical(snapshot.id))) throw new Error('snapshot mismatch');
 if(record.registryCommit!==registry.commit || record.registryDigests?.mappings!==registry.mappingSha256 || record.registryDigests?.bindings!==registry.bindingSha256) throw new Error('registry mismatch');
 for(const field of ['confirmedLeads','rejectedLeads','likelyImpacts','unresolved']) if(!Array.isArray(record[field])) throw new Error(`invalid ${field}`);
 const nodeExists=k=>Boolean(k && snapshot.sources.get(k.sourceId)?.graphState?.nodes?.has(k.nodeIri));
 for(const lead of record.confirmedLeads) if(!nodeExists(lead)) throw new Error('invalid confirmed lead');
 for(const lead of record.rejectedLeads) if(!nodeExists(lead.key)||!text(lead.reason)) throw new Error('invalid rejected lead');
 const pathMap=new Map(paths.map(p=>[p.id,p])), viewMap=new Map((views??[]).map(v=>[viewDigest(v),v]));
 const same=(a,b)=>a?.sourceId===b?.sourceId && a?.nodeIri===b?.nodeIri;
 for(const impact of record.likelyImpacts) {
  if(!impact.key || !text(impact.reason) || !Array.isArray(impact.pathIds) || !Array.isArray(impact.evidenceIris) || (impact.viewIds!==undefined && !Array.isArray(impact.viewIds))) throw new Error('invalid impact');
  if(impact.pathIds.length) {
   if(!nodeExists(impact.key)||!impact.evidenceIris.length) throw new Error('invalid path impact');
   const selected=impact.pathIds.map(id=>pathMap.get(id)); if(selected.some(p=>!p)) throw new Error('unresolved path');
   if(!selected.some(p=>p.steps.some(s=>[s.subject,s.object,s.from,s.to].some(k=>same(k,impact.key))))) throw new Error('impact is not present in a cited path');
   const evidence=new Set(selected.flatMap(p=>p.steps.flatMap(s=>s.evidenceIris??[])));
   if(impact.evidenceIris.some(i=>!evidence.has(i))) throw new Error('unresolved evidence');
  } else {
   if(!impact.viewIds?.length || impact.evidenceIris.length) throw new Error('view evidence is required');
   const selected=impact.viewIds.map(id=>viewMap.get(id)); if(selected.some(v=>!v)) throw new Error('unresolved view');
   let verified=false;
   for(const view of selected) {
    const inspected=view.items.find(item=>same(item.key,impact.key) && item.basis==='architect-inspected');
    if(!inspected) continue;
    const reconstructed=projectView({snapshot,registry,focus:view.focus,level:view.level,supplement:inspected.evidence[0]});
    if(viewDigest(reconstructed)===viewDigest(view)) verified=true;
   }
   if(!verified) throw new Error('view evidence is not verified for impact');
  }
 }
 for(const gap of record.unresolved) if(!text(gap.reason)) throw new Error('unresolved gap requires reason');
 if('approved' in record || 'approvalStatus' in record) throw new Error('approval is not supported');
 return createHash('sha256').update(JSON.stringify(canonical(record))+'\n').digest('hex');
}
export function renderAssessment(record,context) {
 if(!text(record.architect)) throw new Error('architect is required');
 const digest=validateAssessment(record,context);
 const lines=[`# Impact assessment: ${escaped(record.requestId)}`,'',escaped(record.description),'',`Architect: ${escaped(record.architect)}`,`Snapshot: ${escaped(record.snapshotId.catalogCommit)}`,`Assessment digest: ${digest}`,'','## Confirmed leads'];
 for(const k of record.confirmedLeads) lines.push(`- ${escaped(k.sourceId)}: ${escaped(k.nodeIri)}`);
 lines.push('','## Not expected to be affected'); for(const x of record.rejectedLeads) lines.push(`- ${escaped(x.key.sourceId)}: ${escaped(x.key.nodeIri)} — ${escaped(x.reason)}`);
 lines.push('','## Likely impacts'); for(const x of record.likelyImpacts) lines.push(`- ${escaped(x.key.sourceId)}: ${escaped(x.key.nodeIri)} — ${escaped(x.reason)}; paths ${x.pathIds.map(escaped).join(', ')}; evidence ${x.evidenceIris.map(escaped).join(', ')}; views ${(x.viewIds??[]).map(escaped).join(', ')}`);
 lines.push('','## Unresolved gaps and questions'); for(const x of record.unresolved) lines.push(`- ${escaped(x.code)}: ${escaped(x.reason)}`);
 return lines.join('\n')+'\n';
}
