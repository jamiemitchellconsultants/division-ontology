import {createHash} from 'node:crypto';
import {readAtCommit} from './git.mjs';
const hash=s=>createHash('sha256').update(s).digest('hex');
const levels=['context','container','component','code'];
const allowed={context:['person','software-system'],container:['container'],component:['component'],code:['code']};
const key=(source,iri)=>JSON.stringify([source,iri]);
const nonempty=x=>typeof x==='string' && x.length>0;
function validateIdentity(snapshot,e) {
  const source=snapshot.sources.get(e.sourceId);
  if(!source || !nonempty(e.reason) || !Number.isSafeInteger(e.startLine) || !Number.isSafeInteger(e.endLine) || e.startLine<1 || e.endLine<e.startLine || !/^[a-f0-9]{40}$/.test(e.blobId)) return false;
  let content; try {content=readAtCommit(source.checkout,source.commit,e.path);} catch {return false;}
  if(content===null) return false;
  const bytes=Buffer.from(content), blob=createHash('sha1').update(`blob ${bytes.length}\0`).update(bytes).digest('hex');
  return blob===e.blobId && e.endLine<=(content.endsWith('\n')?content.split('\n').length-1:content.split('\n').length);
}
function support(snapshot,sourceId,nodeIri,assertionIri,evidenceIri) {
  const graph=snapshot.sources.get(sourceId)?.graphState, a=graph?.assertions?.get(assertionIri);
  return Boolean(graph?.nodes?.has(nodeIri) && a && (a.subject===nodeIri || a.object===nodeIri) && a.evidenceIris.includes(evidenceIri) && graph.evidence.has(evidenceIri));
}
export function mappingFor(registry,sourceId,nodeIri) {return registry.mappings.find(m=>m.sourceId===sourceId && m.nodeIri===nodeIri);}
export function bindingFrom(registry,sourceId,nodeIri) {return registry.bindings.find(b=>b.fromSourceId===sourceId && b.externalNodeIri===nodeIri);}
export function loadRegistries({divisionRoot,registryCommit,snapshot}) {
  const mappingRaw=readAtCommit(divisionRoot,registryCommit,'c4/mappings.json'), bindingRaw=readAtCommit(divisionRoot,registryCommit,'c4/bindings.json');
  if(mappingRaw===null || bindingRaw===null) throw new Error('missing registry');
  const m=JSON.parse(mappingRaw), b=JSON.parse(bindingRaw);
  if(m.schemaVersion!==1 || b.schemaVersion!==1 || !Array.isArray(m.mappings) || !Array.isArray(b.bindings)) throw new Error('invalid registry schema');
  const gaps=[], seen=new Set(), candidates=[];
  for(const entry of m.mappings) {
    if(!entry || ![entry.sourceId,entry.nodeIri,entry.supportAssertionIri,entry.supportEvidenceIri].every(nonempty) || !allowed[entry.level]?.includes(entry.role) || (entry.parent && ![entry.parent.sourceId,entry.parent.nodeIri].every(nonempty))) throw new Error('invalid mapping');
    const id=key(entry.sourceId,entry.nodeIri); if(seen.has(id)) throw new Error('duplicate mapping'); seen.add(id);
    const kind=snapshot.sources.get(entry.sourceId)?.graphState?.nodes?.get(entry.nodeIri)?.kind;
    const expectedKind={ 'software-system':'system', container:'component', component:'component', code:'source-symbol' }[entry.role];
    const kindValid=expectedKind ? kind===`https://ontology-kit.dev/schema#${expectedKind}` : Boolean(kind && !kind.startsWith('https://ontology-kit.dev/schema#'));
    if(kindValid && support(snapshot,entry.sourceId,entry.nodeIri,entry.supportAssertionIri,entry.supportEvidenceIri)) candidates.push(entry); else gaps.push({code:'stale-mapping',sourceId:entry.sourceId,nodeIri:entry.nodeIri,reason:'Mapping support is unavailable'});
  }
  const mappings=[];
  for(const level of levels) for(const entry of candidates.filter(x=>x.level===level)) {
    const expected=levels.indexOf(level)-1;
    if((expected>=0 && (!entry.parent || !mappings.some(p=>p.sourceId===entry.parent.sourceId && p.nodeIri===entry.parent.nodeIri && levels.indexOf(p.level)===expected))) || (expected<0 && entry.parent)) gaps.push({code:'stale-mapping',sourceId:entry.sourceId,nodeIri:entry.nodeIri,reason:'Parent mapping is unsupported'});
    else mappings.push(entry);
  }
  seen.clear(); const bindings=[];
  for(const entry of b.bindings) {
    if(!entry || ![entry.fromSourceId,entry.externalNodeIri,entry.toSourceId,entry.targetNodeIri,entry.sourceAssertionIri,entry.sourceEvidenceIri].every(nonempty) || !entry.identityEvidence || !nonempty(entry.identityEvidence.path) || !nonempty(entry.identityEvidence.sourceId)) throw new Error('invalid binding');
    if(entry.identityEvidence.path.startsWith('/') || entry.identityEvidence.path.split('/').some(x=>!x || x==='.' || x==='..') || entry.identityEvidence.path.includes('\\')) throw new Error('unsafe identity evidence path');
    const id=key(entry.fromSourceId,entry.externalNodeIri); if(seen.has(id)) throw new Error('duplicate binding'); seen.add(id);
    if(!support(snapshot,entry.fromSourceId,entry.externalNodeIri,entry.sourceAssertionIri,entry.sourceEvidenceIri) || !snapshot.sources.get(entry.toSourceId)?.graphState?.nodes?.has(entry.targetNodeIri) || !validateIdentity(snapshot,entry.identityEvidence)) gaps.push({code:'stale-binding',sourceId:entry.fromSourceId,nodeIri:entry.externalNodeIri,reason:'Binding support or identity evidence is unavailable'}); else bindings.push(entry);
  }
  const compare=(a,b)=>a<b?-1:a>b?1:0;
  mappings.sort((a,b)=>compare(key(a.sourceId,a.nodeIri),key(b.sourceId,b.nodeIri)));
  bindings.sort((a,b)=>compare(key(a.fromSourceId,a.externalNodeIri),key(b.fromSourceId,b.externalNodeIri)));
  return {commit:registryCommit,mappingSha256:hash(mappingRaw),bindingSha256:hash(bindingRaw),mappings,bindings,gaps};
}
