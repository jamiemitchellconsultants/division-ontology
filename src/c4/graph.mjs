import {Parser, Store, DataFactory} from 'n3';
import {createHash} from 'node:crypto';
import {listAtCommit,readAtCommit} from './git.mjs';
const K='https://ontology-kit.dev/schema#', RDF='http://www.w3.org/1999/02/22-rdf-syntax-ns#', RDFS='http://www.w3.org/2000/01/rdf-schema#';
const n=DataFactory.namedNode;
function matches(selector,path) {
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
  const files=listAtCommit(checkout,commit,cfg.root).filter(f=>f.endsWith('.ttl') && !f.endsWith('/graph-schema.ttl'));
  if(!files.length) throw new Error('enabled source graph has no Turtle files');
  const store=new Store();
  for(const file of files) {try {store.addQuads(new Parser().parse(readAtCommit(checkout,commit,file)));} catch(e) {throw new Error(`invalid Turtle in ${file}: ${e.message}`);} }
  const objects=(subject,predicate)=>store.getObjects(n(subject),n(predicate),null);
  const one=(subject,predicate,type,optional=false)=>{const values=objects(subject,predicate); if(optional && !values.length) return undefined; if(values.length!==1 || values[0].termType!==type) throw new Error(`invalid ${predicate} on ${subject}`); return values[0].value;};
  const resources=type=>store.getSubjects(n(RDF+'type'),n(K+type),null).map(x=>{if(x.termType!=='NamedNode') throw new Error('graph resource must have named IRI'); return x.value;});
  const manifests=resources('SourceKnowledgeGraph');
  if(manifests.length!==1 || one(manifests[0],K+'schemaVersion','Literal')!=='1') throw new Error('invalid graph schema version');
  const nodes=new Map(), assertions=new Map(), evidence=new Map(), coverage=[];
  for(const iri of resources('GraphNode')) nodes.set(iri,{iri,kind:one(iri,K+'kind','NamedNode'),label:one(iri,RDFS+'label','Literal'),denotes:one(iri,K+'denotes','NamedNode',true)});
  for(const iri of resources('GraphCoverage')) {
    const selector=one(iri,K+'pathSelector','Literal'), statusIri=one(iri,K+'status','NamedNode'), status=statusIri.startsWith(K)?statusIri.slice(K.length):'', priority=Number(one(iri,K+'priority','Literal')), reason=one(iri,K+'reason','Literal',true);
    if(!['covered','unknown','excluded'].includes(status) || !Number.isSafeInteger(priority) || (status!=='covered'&&!reason)) throw new Error('invalid graph coverage');
    matches(selector,''); coverage.push({iri,selector,status,priority,reason});
  }
  const graph={status:'available',nodes,assertions,evidence,coverage};
  for(const iri of resources('GraphEvidence')) {
    const path=one(iri,K+'path','Literal'), startLine=Number(one(iri,K+'startLine','Literal')), endLine=Number(one(iri,K+'endLine','Literal')), blobId=one(iri,K+'blobId','Literal'), roleIri=one(iri,K+'role','NamedNode'), role=roleIri.startsWith(K)?roleIri.slice(K.length):'';
    const content=readAtCommit(checkout,commit,path);
    if(content===null) throw new Error('evidence path missing');
    const bytes=Buffer.from(content), actual=createHash('sha1').update(`blob ${bytes.length}\0`).update(bytes).digest('hex');
    if(actual!==blobId) throw new Error('evidence blob mismatch');
    const lineCount=content.endsWith('\n')?content.split('\n').length-1:content.split('\n').length;
    if(!Number.isSafeInteger(startLine)||!Number.isSafeInteger(endLine)||startLine<1||endLine<startLine||endLine>lineCount) throw new Error('invalid evidence line range');
    if(!Array.isArray(cfg.evidence?.[role]) || !cfg.evidence[role].some(selector=>matches(selector,path))) throw new Error('evidence role selector mismatch');
    if(coverageFor(graph,path).status!=='covered') throw new Error('evidence path is not covered');
    evidence.set(iri,{iri,path,startLine,endLine,blobId,role});
  }
  for(const iri of resources('GraphAssertion')) {
    const subject=one(iri,K+'subject','NamedNode'), object=one(iri,K+'object','NamedNode'), relationship=one(iri,K+'relationship','NamedNode'), basisIri=one(iri,K+'basis','NamedNode'), basis=basisIri.startsWith(K)?basisIri.slice(K.length):'', evidenceIris=objects(iri,K+'evidence').map(x=>{if(x.termType!=='NamedNode') throw new Error('invalid evidence reference');return x.value;}).sort();
    if(!nodes.has(subject)||!nodes.has(object)||!['direct','inferred'].includes(basis)||!evidenceIris.length||evidenceIris.some(x=>!evidence.has(x))) throw new Error('invalid graph assertion references');
    assertions.set(iri,{iri,subject,object,relationship,basis,evidenceIris});
  }
  return graph;
}
