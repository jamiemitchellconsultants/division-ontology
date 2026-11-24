import {Parser, Store, DataFactory} from 'n3';
import {createHash} from 'node:crypto';
import {listAtCommit,readAtCommit} from './git.mjs';
const K='https://ontology-kit.dev/schema#', RDF='http://www.w3.org/1999/02/22-rdf-syntax-ns#', RDFS='http://www.w3.org/2000/01/rdf-schema#';
const XSD = 'http://www.w3.org/2001/XMLSchema#';
const EVIDENCE_ROLES = ['implementation', 'test', 'selected-document', 'ontology', 'deployment'];
const DESIGN_STATUSES = new Map([
  [K + 'behaviour', new Set([K + 'current', K + 'retired'])],
  [K + 'decision', new Set([K + 'proposed', K + 'accepted', K + 'superseded', K + 'deprecated'])],
]);
const DESIGN_SUCCESSORS = new Set([K + 'replaced-by', K + 'superseded-by']);
const n=DataFactory.namedNode;
export function matches(selector,path) {
  if (typeof selector!=='string' || selector.startsWith('/') || selector.split('/').includes('..')) throw new Error('invalid selector');
  let pattern='^';
  for(let i=0;i<selector.length;i++) { const c=selector[i]; if(c==='*') { if(selector[i+1]==='*') {i++; if(selector[i+1]==='/') {i++; pattern+='(?:.*/)?';} else pattern+='.*';} else pattern+='[^/]*'; } else if(c==='?') pattern+='[^/]'; else pattern+=c.replace(/[.*+?^${}()|[\]\\]/g,'\\$&'); }
  return new RegExp(pattern+'$').test(path);
}
export function coverageFor(graph,path) {
  const found=graph.coverage.filter(c=>matches(c.selector,path)).sort((a,b)=>b.priority-a.priority);
  if (!found.length) return {status:'unknown',reason:'No matching coverage selector'};
  if(found[1]?.priority===found[0].priority && found[1].status!==found[0].status) throw new Error('conflicting coverage priority');
  const c=found[0]; return {status:c.status,reason:c.reason,coverageIri:c.iri};
}
export function loadSourceGraph({checkout,commit,config}) {
  const cfg=config?.sourceGraph;
  if(!cfg?.enabled) return {status:'unavailable',reason:'Source graph not enabled',nodes:new Map(),assertions:new Map(),evidence:new Map(),coverage:[]};
  const entries=listAtCommit(checkout,commit,cfg.root);
  if(!entries.length) return {status:'unavailable',reason:'Source graph absent at pinned commit',nodes:new Map(),assertions:new Map(),evidence:new Map(),coverage:[]};
  const files=entries.filter(f=>f.endsWith('.ttl') && !f.endsWith('/graph-schema.ttl'));
  if(!files.length) throw new Error('present source graph has no Turtle data');
  const store=new Store();
  for(const file of files) {try {store.addQuads(new Parser().parse(readAtCommit(checkout,commit,file)));} catch(e) {throw new Error(`invalid Turtle in ${file}: ${e.message}`);} }
  const objects=(subject,predicate)=>store.getObjects(n(subject),n(predicate),null);
  const one=(subject,predicate,type,optional=false)=>{const values=objects(subject,predicate); if(optional && !values.length) return undefined; if(values.length!==1 || values[0].termType!==type) throw new Error(`invalid ${predicate} on ${subject}`); return values[0].value;};
  const resources=type=>store.getSubjects(n(RDF+'type'),n(K+type),null).map(x=>{if(x.termType!=='NamedNode') throw new Error('graph resource must have named IRI'); return x.value;});
  const manifests = store.getSubjects(n(RDF + 'type'), n(K + 'SourceKnowledgeGraph'), null);
  if (manifests.length !== 1 || manifests[0].termType !== 'NamedNode') {
    throw new Error('invalid source graph manifest');
  }
  const [version, extraVersion] = objects(manifests[0].value, K + 'schemaVersion');
  // The bundled schema 1 fixtures encode the version as a plain literal.
  const validDatatype = version?.termType === 'Literal' &&
    (version.datatype.value === XSD + 'integer' ||
      (version.value === '1' && version.datatype.value === XSD + 'string'));
  if (!validDatatype || extraVersion || (version.value !== '1' && version.value !== '2')) {
    throw new Error('invalid graph schema version');
  }
  const schemaVersion = Number(version.value);
  const nodes=new Map(), assertions=new Map(), evidence=new Map(), coverage=[];
  for(const iri of resources('GraphCoverage')) {
    const selector=one(iri,K+'pathSelector','Literal'), statusIri=one(iri,K+'status','NamedNode'), status=statusIri.startsWith(K)?statusIri.slice(K.length):'', priority=Number(one(iri,K+'priority','Literal')), reason=one(iri,K+'reason','Literal',true);
    if(!['covered','unknown','excluded'].includes(status) || !Number.isSafeInteger(priority) || (status!=='covered'&&!reason)) throw new Error('invalid graph coverage');
    matches(selector,''); coverage.push({iri,selector,status,priority,reason});
  }
  const graph={status:'available',schemaVersion,nodes,assertions,evidence,coverage};
  for(const iri of resources('GraphEvidence')) {
    const path=one(iri,K+'path','Literal'), startLine=Number(one(iri,K+'startLine','Literal')), endLine=Number(one(iri,K+'endLine','Literal')), blobId=one(iri,K+'blobId','Literal'), roleIri=one(iri,K+'role','NamedNode');
    const role = EVIDENCE_ROLES.find(key =>
      roleIri === K + 'role-' + key || (schemaVersion === 1 && roleIri === K + key));
    if (!role) throw new Error(`evidence role selector mismatch on ${iri}: ${roleIri}`);
    const content=readAtCommit(checkout,commit,path);
    if(content===null) throw new Error('evidence path missing');
    const bytes=Buffer.from(content), actual=createHash('sha1').update(`blob ${bytes.length}\0`).update(bytes).digest('hex');
    if(actual!==blobId) throw new Error('evidence blob mismatch');
    const lineCount=content.endsWith('\n')?content.split('\n').length-1:content.split('\n').length;
    if(!Number.isSafeInteger(startLine)||!Number.isSafeInteger(endLine)||startLine<1||endLine<startLine||endLine>lineCount) throw new Error('invalid evidence line range');
    if(!Array.isArray(cfg.evidence?.[role]) || !cfg.evidence[role].some(selector=>matches(selector,path))) throw new Error(`evidence role selector mismatch on ${iri}: ${roleIri}`);
    if(coverageFor(graph,path).status!=='covered') throw new Error('evidence path is not covered');
    evidence.set(iri,{iri,path,startLine,endLine,blobId,role});
  }
  for (const iri of resources('GraphNode')) {
    const kind = one(iri, K + 'kind', 'NamedNode');
    const statuses = DESIGN_STATUSES.get(kind);
    const hasDesignFields = objects(iri, K + 'designStatus').length || objects(iri, K + 'evidence').length;
    if (schemaVersion === 1 && (statuses || hasDesignFields)) {
      throw new Error(`design records require graph schema version 2 on ${iri}`);
    }
    const node = { iri, kind, label: one(iri, RDFS + 'label', 'Literal'), denotes: one(iri, K + 'denotes', 'NamedNode', true) };
    if (statuses) {
      const designStatus = one(iri, K + 'designStatus', 'NamedNode');
      if (!statuses.has(designStatus)) throw new Error(`invalid designStatus ${designStatus} on ${iri}`);
      const evidenceIri = one(iri, K + 'evidence', 'NamedNode');
      if (evidence.get(evidenceIri)?.role !== 'selected-document') {
        throw new Error(`invalid selected-document evidence ${evidenceIri} on ${iri}`);
      }
      node.designStatus = designStatus;
      node.evidenceIris = [evidenceIri];
    } else if (hasDesignFields) {
      throw new Error(`designStatus and node evidence require a behaviour or decision on ${iri}`);
    }
    nodes.set(iri, node);
  }
  for(const iri of resources('GraphAssertion')) {
    const subject=one(iri,K+'subject','NamedNode'), object=one(iri,K+'object','NamedNode'), relationship=one(iri,K+'relationship','NamedNode'), basisIri=one(iri,K+'basis','NamedNode'), basis=basisIri.startsWith(K)?basisIri.slice(K.length):'', evidenceIris=objects(iri,K+'evidence').map(x=>{if(x.termType!=='NamedNode') throw new Error('invalid evidence reference');return x.value;}).sort();
    if (schemaVersion === 1 && DESIGN_SUCCESSORS.has(relationship)) {
      throw new Error(`design relationship ${relationship} requires graph schema version 2 on ${iri}`);
    }
    if(!nodes.has(subject)||!nodes.has(object)||!['direct','inferred'].includes(basis)||!evidenceIris.length||evidenceIris.some(x=>!evidence.has(x))) throw new Error('invalid graph assertion references');
    assertions.set(iri,{iri,subject,object,relationship,basis,evidenceIris});
  }
  return graph;
}
export function coverageGaps(snapshot) {
 const gaps=[];
 for(const source of snapshot.sources.values()) {
  const graph=source.graphState;
  if(graph?.status!=='available') {gaps.push({code:'graph-unavailable',sourceId:source.id,reason:graph?.reason??'Graph unavailable'});continue;}
  for(const c of graph.coverage ?? []) if(c.status!=='covered') gaps.push({code:'coverage-'+c.status,sourceId:source.id,coverageIri:c.iri,selector:c.selector,reason:c.reason});
 }
 return gaps.sort((a,b)=>JSON.stringify(a)<JSON.stringify(b)?-1:JSON.stringify(a)>JSON.stringify(b)?1:0);
}
export function coverageInventory(snapshot) {
 const records=[];
 for(const source of snapshot.sources.values()) for(const entry of source.graphState?.coverage??[]) records.push({sourceId:source.id,...entry});
 return records.sort((a,b)=>JSON.stringify(a)<JSON.stringify(b)?-1:JSON.stringify(a)>JSON.stringify(b)?1:0);
}
