import {coverageGaps,coverageInventory} from './graph.mjs';
import {mappingFor} from './registry.mjs';
const cmp=(a,b)=>a<b?-1:a>b?1:0;
export function findCandidates({snapshot,registry,query='',exactIri}) {
  if (!exactIri && !query.trim()) throw new Error('query or exact IRI is required');
  const phrase=query.trim().toLowerCase(), tokens=[...new Set(phrase.split(/[^\p{L}\p{N}]+/u).filter(Boolean))];
  const leads=[];
  function add(sourceId,nodeIri,text,kind,supported,reason) {
    const lower=text.toLowerCase(); let rank,matchKind;
    if(exactIri) {if(nodeIri!==exactIri)return; rank=0; matchKind='exact-iri';}
    else if(lower.includes(phrase)) {rank=1; matchKind='phrase';}
    else {const terms=new Set(lower.split(/[^\p{L}\p{N}]+/u).filter(Boolean)); if(!tokens.some(x=>terms.has(x)))return; rank=2; matchKind='token';}
    leads.push({key:{sourceId,nodeIri},matchedText:text,matchKind,sourceCommit:snapshot.sources.get(sourceId)?.commit,supported,reason,_rank:rank,kind});
  }
  for(const [id,source] of snapshot.sources) for(const node of source.graphState?.nodes?.values() ?? []) {
    const supported=Boolean(mappingFor(registry,id,node.iri)); add(id,node.iri,node.label,'graph',supported,supported?'Reviewed mapping':'No supported mapping');
  }
  for(const concept of snapshot.catalog.concepts ?? []) {
    if(!snapshot.sources.has(concept.source)) continue;
    const text=[concept.label,concept.definition].filter(Boolean).join(' — ');
    add(concept.source,concept.iri,text,'concept',false,'Catalog concept is not a supported graph node');
  }
  leads.sort((a,b)=>a._rank-b._rank || cmp(a.key.sourceId,b.key.sourceId) || cmp(a.key.nodeIri,b.key.nodeIri) || cmp(a.kind,b.kind));
  for(const lead of leads) delete lead._rank;
  const gaps=[...(registry.gaps??[]),...coverageGaps(snapshot)];
  if(!leads.length) gaps.push({code:'unknown-search',reason:'No matching lead; coverage does not prove no impact'});
  return {snapshotId:snapshot.id,registryCommit:registry.commit,coverage:coverageInventory(snapshot),leads,gaps};
}
