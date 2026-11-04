import {readFileSync,mkdirSync,writeFileSync} from 'node:fs';
import {resolve,join,relative,isAbsolute} from 'node:path';
import {pathToFileURL} from 'node:url';
import {loadSnapshot} from './snapshot.mjs';
import {loadRegistries} from './registry.mjs';
import {findCandidates} from './candidates.mjs';
import {readImpactPaths} from './paths.mjs';
import {projectView} from './project.mjs';
import {renderView} from './render.mjs';
import {newAssessment,validateAssessment,renderAssessment} from './assessment.mjs';
import {renderFeedback} from './feedback.mjs';
const allowed={search:['query','exact-iri'],paths:['start','max-depth'],view:['focus','level','supplement'], 'assessment-init':['request-id','description-file'],'assessment-render':['input'],'feedback-render':['input','assessment-file']};
const common=['catalog-commit','registry-commit','checkout','output'];
function parse(argv) {
 const [command,...args]=argv;if(!allowed[command]) throw new Error('unknown command');const values={};
 for(let i=0;i<args.length;i+=2) {const flag=args[i];if(!flag?.startsWith('--') || !args[i+1] || args[i+1].startsWith('--')) throw new Error('invalid argument');const name=flag.slice(2);if(![...common,...allowed[command]].includes(name)) throw new Error(`unknown flag ${flag}`);if(['checkout','start'].includes(name)) (values[name]??=[]).push(args[i+1]);else {if(name in values) throw new Error(`duplicate flag ${flag}`);values[name]=args[i+1];}}
 for(const name of ['catalog-commit','registry-commit','output']) if(!values[name]) throw new Error(`missing --${name}`);
 return {command,values};
}
function pair(value) {const index=value?.indexOf('=')??-1;if(index<1 || index===value.length-1) throw new Error('expected source ID=value');return [value.slice(0,index),value.slice(index+1)];}
const sourceKey=x=>{const [sourceId,nodeIri]=pair(x);return {sourceId,nodeIri};};
const json=x=>JSON.stringify(x,null,2)+'\n';
export async function runC4Cli(argv,{cwd=process.cwd(),stdout=process.stdout,stderr=process.stderr}={}) {
 try {
  const {command,values:v}=parse(argv),checkouts=new Map();
  for(const c of v.checkout??[]) {const [id,path]=pair(c);if(checkouts.has(id)||!isAbsolute(path)) throw new Error('invalid checkout');checkouts.set(id,path);}
  const output=resolve(cwd,v.output);
  for(const root of [resolve(cwd),...checkouts.values()]) {const rel=relative(root,output);if(!rel || (!rel.startsWith('..')&&!isAbsolute(rel))) throw new Error('output must be outside repositories');}
  const snapshot=loadSnapshot({divisionRoot:cwd,catalogCommit:v['catalog-commit'],checkouts});
  const registry=loadRegistries({divisionRoot:cwd,registryCommit:v['registry-commit'],snapshot});
  const files=new Map();
  if(command==='search') {const result=findCandidates({snapshot,registry,query:v.query,exactIri:v['exact-iri']});files.set('candidates.json',json(result));}
  if(command==='paths') {const result=readImpactPaths({snapshot,registry,starts:(v.start??[]).map(sourceKey),maxDepth:v['max-depth']===undefined?4:Number(v['max-depth'])});files.set('paths.json',json(result));}
  if(command==='view') {if(!v.focus||!v.level) throw new Error('focus and level required');const supplement=v.supplement?JSON.parse(readFileSync(resolve(cwd,v.supplement),'utf8')):null;const result=renderView(projectView({snapshot,registry,focus:sourceKey(v.focus),level:v.level,supplement}));files.set(`view-${v.level}.json`,result.json);files.set(`view-${v.level}.md`,result.markdown);}
  if(command==='assessment-init') {if(!v['request-id']||!v['description-file']) throw new Error('request ID and description file required');const record=newAssessment({requestId:v['request-id'],description:readFileSync(resolve(cwd,v['description-file']),'utf8'),snapshot,registry});files.set('assessment.json',json(record));}
  if(command==='assessment-render'||command==='feedback-render') {
   if(!v.input) throw new Error('input is required');
   const input=JSON.parse(readFileSync(resolve(cwd,v.input),'utf8'));
   const record=command==='assessment-render'?input:JSON.parse(readFileSync(resolve(cwd,v['assessment-file']??''),'utf8'));
   const paths=record.confirmedLeads.length ? readImpactPaths({snapshot,registry,starts:record.confirmedLeads}).paths : [];
   const context={snapshot,registry,paths,views:[]};
   if(command==='assessment-render') files.set('assessment.md',renderAssessment(record,context));
   else {const digest=validateAssessment(record,context);files.set('feedback.md',renderFeedback(input,{requestId:record.requestId,digest}));}
  }
  mkdirSync(output,{recursive:true});for(const [name,content] of files) writeFileSync(join(output,name),content);stdout?.write?.(`Wrote ${files.size} file(s)\n`);return 0;
 } catch(e) {stderr?.write?.(`${e.message}\n`);return 1;}
}
if(process.argv[1] && import.meta.url===pathToFileURL(process.argv[1]).href) process.exitCode=await runC4Cli(process.argv.slice(2));
