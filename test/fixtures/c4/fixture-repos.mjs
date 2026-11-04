import {mkdtempSync, mkdirSync, writeFileSync, readFileSync, rmSync} from 'node:fs';
import {tmpdir} from 'node:os';
import {join, dirname} from 'node:path';
import {execFileSync} from 'node:child_process';
import {Writer, DataFactory} from 'n3';
const {namedNode, literal, quad}=DataFactory;
const K='https://ontology-kit.dev/schema#', RDF='http://www.w3.org/1999/02/22-rdf-syntax-ns#', RDFS='http://www.w3.org/2000/01/rdf-schema#';
function graph(root, base) {
  const path='src/app.ts', code='export const api = 1;\n'; put(root,path,code);
  const blob=git(root,'hash-object',path), w=new Writer();
  const add=(s,p,o)=>w.addQuad(quad(namedNode(base+s),namedNode(p),o));
  const iri=x=>namedNode(K+x), lit=x=>literal(String(x));
  const type=(s,t)=>add(s,RDF+'type',iri(t));
  type('graph','SourceKnowledgeGraph'); add('graph',K+'schemaVersion',lit(1));
  for (const [n,label,kind] of [['system','Alpha API','system'],['external','Beta','system']]) {type(n,'GraphNode'); add(n,K+'kind',iri(kind)); add(n,RDFS+'label',lit(label));}
  type('evidence','GraphEvidence'); for(const [key,val] of Object.entries({path,startLine:1,endLine:1,blobId:blob})) add('evidence',K+key,lit(val)); add('evidence',K+'role',iri('implementation'));
  type('assertion','GraphAssertion'); for(const [key,val] of Object.entries({subject:'system',object:'external'})) add('assertion',K+key,namedNode(base+val)); add('assertion',K+'relationship',iri('communicates-with')); add('assertion',K+'basis',iri('inferred')); add('assertion',K+'evidence',namedNode(base+'evidence'));
  type('coverage','GraphCoverage'); add('coverage',K+'pathSelector',lit('src/**')); add('coverage',K+'priority',lit(1)); add('coverage',K+'status',iri('covered'));
  let result; w.end((e,text)=>{if(e) throw e; result=text}); put(root,'graph/manifest.ttl',result);
  put(root,'ontology.config.json',JSON.stringify({sourceGraph:{enabled:true,root:'graph',evidence:{implementation:['src/**']}}}));
}

const git = (root, ...args) => execFileSync('git', args, {cwd:root, encoding:'utf8', stdio:['ignore','pipe','pipe']}).trim();
function put(root, path, data) { const f=join(root,path); mkdirSync(dirname(f),{recursive:true}); writeFileSync(f,data); }
function init(root, url) { mkdirSync(root,{recursive:true}); git(root,'init','-q'); git(root,'config','user.name','Fixture'); git(root,'config','user.email','fixture@example.test'); git(root,'remote','add','origin',url); }
function commit(root, message) { git(root,'add','.'); git(root,'commit','-qm',message); return git(root,'rev-parse','HEAD'); }
export async function withPilotFixture(run) {
  const root=mkdtempSync(join(tmpdir(),'c4-fixture-'));
  try {
    const divisionRoot=join(root,'division'), alpha=join(root,'alpha'), beta=join(root,'beta');
    const sources=[['fixture/Alpha',alpha],['fixture/Beta',beta]];
    for (const [id,path] of sources) { init(path,`https://github.com/${id}.git`); put(path,'README.md',id); }
    graph(alpha,'https://alpha.example/#');
    const sourceCommits={alpha:commit(alpha,'initial'),beta:commit(beta,'initial')};
    init(divisionRoot,'https://github.com/fixture/division.git');
    put(divisionRoot,'sources.json',JSON.stringify({sources:sources.map(([id])=>({id,url:`https://github.com/${id}.git`}))}));
    put(divisionRoot,'dist/catalog.json',JSON.stringify({schemaVersion:1,sources:[{id:'fixture/Alpha',commit:sourceCommits.alpha},{id:'fixture/Beta',commit:sourceCommits.beta}],concepts:[]}));
    const catalogCommit=commit(divisionRoot,'catalog');
    await run({divisionRoot,catalogCommit,registryCommit:catalogCommit,checkouts:new Map(sources),sourceCommits,outputDir:join(root,'output'),writeDivision:(p,d)=>put(divisionRoot,p,d),readDivision:p=>readFileSync(join(divisionRoot,p),'utf8'),commitDivision:m=>commit(divisionRoot,m),readSource:(id,p)=>readFileSync(join(new Map(sources).get(id),p),'utf8'),writeSource:(id,p,d)=>put(new Map(sources).get(id),p,d),commitSource:(id,m)=>commit(new Map(sources).get(id),m)});
  } finally { rmSync(root,{recursive:true,force:true}); }
}
