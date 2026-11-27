import { mkdirSync, writeFileSync, readFileSync, rmSync, lstatSync } from 'node:fs';
import { join, dirname, resolve } from 'node:path';
import { execFileSync } from 'node:child_process';
import { pathToFileURL } from 'node:url';
import { Writer, DataFactory } from 'n3';
import { runCli } from '../../../src/cli.mjs';

const { namedNode, literal, quad } = DataFactory;
const K = 'https://ontology-kit.dev/schema#', RDF = 'http://www.w3.org/1999/02/22-rdf-syntax-ns#', RDFS = 'http://www.w3.org/2000/01/rdf-schema#';
const marker = 'division-ontology-representative-fixture-v1\n';
const definitions = [
  { name:'ecommerce', title:'Ecommerce', concepts:['Customer','Product','Basket','Order','OrderLine','Payment','DeliveryAddress','Refund'], container:'Storefront API', component:'Order processing', external:'warehouse', externalLabel:'Warehouse fulfilment', detail:'Orders are sent to Warehouse fulfilment for picking and dispatch.' },
  { name:'warehouse', title:'Warehouse fulfilment', concepts:['Order','FulfilmentOrder','InventoryItem','StockLocation','Pick','Package','Shipment','Carrier'], container:'Warehouse service', component:'Fulfilment processing', external:'accounting', externalLabel:'Accounting', detail:'Shipment completion is sent to Accounting for sales invoicing.' },
  { name:'accounting', title:'Accounting', concepts:['Order','SalesInvoice','Receipt','PurchaseInvoice','Supplier','Payment','LedgerAccount','JournalEntry','GeneralLedger'], container:'Accounting service', component:'Sales and purchase ledgers', external:'bank', externalLabel:'External bank', detail:'Accounting sends payment instructions to an external bank, which is not a participating source.' }
];
function put(root, file, data) { const target=join(root,file); mkdirSync(dirname(target),{recursive:true}); writeFileSync(target,data); }
function git(root,...args) { return execFileSync('git',args,{cwd:root,encoding:'utf8',stdio:['ignore','pipe','pipe']}).trim(); }
function init(root, origin) { mkdirSync(root,{recursive:true}); git(root,'init','-q','-b','main'); git(root,'config','user.name','Synthetic Fixture'); git(root,'config','user.email','fixture@example.invalid'); git(root,'remote','add','origin',origin); }
function commit(root) { git(root,'add','.'); git(root,'commit','-qm','Create synthetic fixture'); return git(root,'rev-parse','HEAD'); }
function graph(root, d) {
  const base=`https://example.test/${d.name}/graph#`, path='docs/architecture.md';
  put(root,path,`# ${d.title}\n\n${d.title} contains ${d.container}, which contains ${d.component}.\n${d.detail}\nThis is fabricated evidence for a synthetic acceptance fixture.\n`);
  const blob=git(root,'hash-object',path), w=new Writer();
  const add=(s,p,o)=>w.addQuad(quad(namedNode(base+s),namedNode(p),o));
  const iri=x=>namedNode(K+x), lit=x=>literal(String(x));
  const type=(s,t)=>add(s,RDF+'type',iri(t));
  type('graph','SourceKnowledgeGraph'); add('graph',K+'schemaVersion',lit(1));
  for(const [name,label,kind] of [['system',d.title,'system'],['container',d.container,'component'],['component',d.component,'component'],['external',d.externalLabel,'system']]) { type(name,'GraphNode'); add(name,K+'kind',iri(kind)); add(name,RDFS+'label',lit(label)); }
  type('evidence','GraphEvidence'); for(const [key,value] of Object.entries({path,startLine:1,endLine:5,blobId:blob})) add('evidence',K+key,lit(value)); add('evidence',K+'role',iri('implementation'));
  for(const [name,subject,object,relationship] of [['contains-system','system','container','contains'],['contains-container','container','component','contains'],['connects','system','external','communicates-with']]) { type(name,'GraphAssertion'); add(name,K+'subject',namedNode(base+subject)); add(name,K+'object',namedNode(base+object)); add(name,K+'relationship',iri(relationship)); add(name,K+'basis',iri('direct')); add(name,K+'evidence',namedNode(base+'evidence')); }
  for(const [name,selector,status,reason] of [['docs','docs/**','covered',null],['code','src/**','unknown','No application code is included in this synthetic fixture.'],['private','private/**','excluded','Private integration details are deliberately omitted.']]) { type(name,'GraphCoverage'); add(name,K+'pathSelector',lit(selector)); add(name,K+'priority',lit(1)); add(name,K+'status',iri(status)); if(reason) add(name,K+'reason',lit(reason)); }
  let turtle; w.end((error,text)=>{ if(error) throw error; turtle=text; }); put(root,'graph/manifest.ttl',turtle);
  put(root,'ontology.config.json',JSON.stringify({sourceGraph:{enabled:true,root:'graph',evidence:{implementation:['docs/**']}}},null,2)+'\n');
  return {base,blob};
}
export async function setup(directory) {
  const root=resolve(directory); mkdirSync(root); put(root,'.fixture-marker',marker);
  try {
    const divisionRoot=join(root,'division'), sources=[], checkouts=new Map(), data=[];
    for(const d of definitions) {
      const id=`fixture/${d.name}`, path=join(root,d.name), url=`https://github.com/${id}.git`, namespace=`https://example.test/${d.name}/ontology#`;
      init(path,url);
      const classes=d.concepts.map(c=>`:${c} a owl:Class; rdfs:label "${c}"; rdfs:comment "Synthetic ${d.name} ${c} concept." .`).join('\n');
      put(path,'docs/ontology.ttl',`@prefix : <${namespace}> .\n@prefix owl: <http://www.w3.org/2002/07/owl#> .\n@prefix rdfs: <http://www.w3.org/2000/01/rdf-schema#> .\n${classes}\n`);
      const info=graph(path,d); commit(path); data.push({...d,id,...info}); checkouts.set(id,path);
      sources.push({id,url,ref:'main',ontologyPath:'docs/ontology.ttl',namespace});
    }
    init(divisionRoot,'https://github.com/fixture/division.git');
    put(divisionRoot,'core/ontology.ttl','@prefix : <https://github.com/jamiemitchellconsultants/division-ontology#> .\n@prefix owl: <http://www.w3.org/2002/07/owl#> .\n@prefix rdfs: <http://www.w3.org/2000/01/rdf-schema#> .\n:BusinessOrder a owl:Class; rdfs:label "BusinessOrder"; rdfs:comment "A request for goods made by a customer." .\n');
    put(divisionRoot,'division.json',JSON.stringify({namespace:'https://github.com/jamiemitchellconsultants/division-ontology#',stewards:['steward-one']},null,2)+'\n');
    put(divisionRoot,'sources.json',JSON.stringify({sources},null,2)+'\n');
    const mappings=[], bindings=[];
    for(const d of data) {
      const key=name=>({sourceId:d.id,nodeIri:d.base+name});
      for(const [name,level,role,parent,support] of [['system','context','software-system',null,'contains-system'],['container','container','container','system','contains-system'],['component','component','component','container','contains-container'],['external','context','software-system',null,'connects']]) mappings.push({...key(name),level,role,...(parent?{parent:key(parent)}:{}),supportAssertionIri:d.base+support,supportEvidenceIri:d.base+'evidence'});
      const target=data.find(x=>x.name===d.external);
      if(target) bindings.push({fromSourceId:d.id,externalNodeIri:d.base+'external',toSourceId:target.id,targetNodeIri:target.base+'system',sourceAssertionIri:d.base+'connects',sourceEvidenceIri:d.base+'evidence',identityEvidence:{sourceId:d.id,path:'docs/architecture.md',startLine:3,endLine:4,blobId:d.blob,reason:'Synthetic scenario explicitly identifies the destination system.'}});
    }
    put(divisionRoot,'c4/mappings.json',JSON.stringify({schemaVersion:1,mappings},null,2)+'\n');
    put(divisionRoot,'c4/bindings.json',JSON.stringify({schemaVersion:1,bindings},null,2)+'\n');
    const args=[...checkouts].flatMap(([id,path])=>['--checkout',`${id}=${path}`]);
    await runCli(['build',...args],{root:divisionRoot});
    const revision=commit(divisionRoot);
    put(root,'fixture.json',JSON.stringify({divisionRoot,commit:revision,checkouts:Object.fromEntries(checkouts),output:join(root,'output')},null,2)+'\n');
    return {root,divisionRoot,commit:revision,checkouts};
  } catch(error) { rmSync(root,{recursive:true,force:true}); throw error; }
}
export function teardown(directory) {
  const root=resolve(directory);
  if(!lstatSync(root).isDirectory() || readFileSync(join(root,'.fixture-marker'),'utf8')!==marker) throw new Error('Refusing to remove a directory without the fixture marker');
  rmSync(root,{recursive:true});
}
if(process.argv[1] && import.meta.url===pathToFileURL(process.argv[1]).href) {
  const [action,directory,...extra]=process.argv.slice(2);
  if(!['setup','teardown'].includes(action) || !directory || extra.length) { console.error('Usage: node test/fixtures/representative/setup.mjs setup|teardown /absolute/new-directory'); process.exitCode=1; }
  else try { if(!directory.startsWith('/')) throw new Error('Provide an absolute directory path'); if(action==='setup') { const f=await setup(directory); console.log(`Synthetic fixture created at ${f.root}\nCatalog and registry commit: ${f.commit}\nSee fixture.json for checkout paths.`); } else { teardown(directory); console.log('Synthetic fixture removed.'); } } catch(error) { console.error(error.message); process.exitCode=1; }
}
