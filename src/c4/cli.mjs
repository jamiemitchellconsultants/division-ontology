import {createHash} from 'node:crypto';
import {readFileSync,mkdirSync,writeFileSync,existsSync,realpathSync,lstatSync,readdirSync,chmodSync,rmSync,mkdtempSync} from 'node:fs';
import {resolve,join,relative,isAbsolute,dirname,basename} from 'node:path';
import {tmpdir} from 'node:os';
import {pathToFileURL} from 'node:url';
import {loadSnapshot} from './snapshot.mjs';
import {loadRegistries} from './registry.mjs';
import {buildImpactCli,runImpactCli} from './engine.mjs';
const allowed={search:['query','exact-iri'],paths:['start','max-depth'],view:['focus','level','supplement'], 'assessment-init':['request-id','description-file'],'assessment-render':['input','view-input'],'feedback-render':['input','assessment-file','view-input']};
const common=['catalog-commit','registry-commit','checkout','output'];
function parse(argv) {
 const [command,...args]=argv;if(!allowed[command]) throw new Error('unknown command');const values={};
 for(let i=0;i<args.length;i+=2) {const flag=args[i];if(!flag?.startsWith('--') || !args[i+1] || args[i+1].startsWith('--')) throw new Error('invalid argument');const name=flag.slice(2);if(![...common,...allowed[command]].includes(name)) throw new Error(`unknown flag ${flag}`);if(['checkout','start','view-input'].includes(name)) (values[name]??=[]).push(args[i+1]);else {if(name in values) throw new Error(`duplicate flag ${flag}`);values[name]=args[i+1];}}
 for(const name of ['catalog-commit','registry-commit','output']) if(!values[name]) throw new Error(`missing --${name}`);
 return {command,values};
}
function pair(value) {const index=value?.indexOf('=')??-1;if(index<1 || index===value.length-1) throw new Error('expected source ID=value');return [value.slice(0,index),value.slice(index+1)];}
const sourceKey=x=>{const [sourceId,nodeIri]=pair(x);return {sourceId,nodeIri};};
function canonicalPath(path) {const abs=resolve(path);return existsSync(abs)?realpathSync(abs):join(canonicalPath(dirname(abs)),basename(abs));}
function buildBundle(stagingDir, impactInputPath) {
 const bundleDir=join(stagingDir,'bundle');
 const manifestDir=join(bundleDir,'manifests');
 const blobDir=join(bundleDir,'blobs');
 mkdirSync(manifestDir,{recursive:true});
 mkdirSync(blobDir,{recursive:true});
 const content=readFileSync(impactInputPath);
 const sha256=createHash('sha256').update(content).digest('hex');
 writeFileSync(join(blobDir,sha256),content);
 const manifest={schemaVersion:1,files:[{role:'impactInput',sourceId:'division-core',path:'impact-input.json',sha256,bytes:content.length}]};
 const manifestJson=JSON.stringify(manifest)+'\n';
 const manifestSha256=createHash('sha256').update(manifestJson).digest('hex');
 writeFileSync(join(manifestDir,manifestSha256+'.json'),manifestJson);
 return manifestSha256;
}
export async function runC4Cli(argv,{cwd=process.cwd(),stdout=process.stdout,stderr=process.stderr}={}) {
 try {
  const {command,values:v}=parse(argv),checkouts=new Map();
  for(const c of v.checkout??[]) {const [id,path]=pair(c);if(checkouts.has(id)||!isAbsolute(path)) throw new Error('invalid checkout');checkouts.set(id,path);}
  const output=canonicalPath(resolve(cwd,v.output));
  for(const root of [resolve(cwd),...checkouts.values()]) {const rel=relative(canonicalPath(root),output);if(!rel || (!rel.startsWith('..')&&!isAbsolute(rel))) throw new Error('output must be outside repositories');}
  const snapshot=loadSnapshot({divisionRoot:cwd,catalogCommit:v['catalog-commit'],checkouts});
  const registry=loadRegistries({divisionRoot:cwd,registryCommit:v['registry-commit'],snapshot});

  // Pre-validate inputs that can throw before calling the engine
  if(command==='view') {if(!v.focus||!v.level) throw new Error('focus and level required');if(v.supplement) JSON.parse(readFileSync(resolve(cwd,v.supplement),'utf8'));}
  if(command==='assessment-init') {if(!v['request-id']||!v['description-file']) throw new Error('request ID and description file required');readFileSync(resolve(cwd,v['description-file']),'utf8');}
  if(command==='assessment-render'||command==='feedback-render') {
   if(!v.input) throw new Error('input is required');
   JSON.parse(readFileSync(resolve(cwd,v.input),'utf8'));
   if(command==='feedback-render' && v['assessment-file']) JSON.parse(readFileSync(resolve(cwd,v['assessment-file']),'utf8'));
   for(const p of (v['view-input']??[])) JSON.parse(readFileSync(resolve(cwd,p),'utf8'));
  }

  // Create staging directory
  const staging=mkdtempSync(join(tmpdir(),'division-impact-'));
  chmodSync(staging, 0o700);
  const outDir=join(staging,'out');
  mkdirSync(outDir);

  // Build impact bundle from dist/impact-input.json
  const impactInputPath=resolve(cwd,'dist','impact-input.json');
  if(!existsSync(impactInputPath)) throw new Error('missing dist/impact-input.json');
  const manifestId=buildBundle(staging, impactInputPath);

  // Build catalog sources for the .NET engine
  const catalogSources=JSON.stringify(snapshot.id.sources);

  // Build tool flags
  const toolArgs=[command,'--bundle',join(staging,'bundle'),'--manifest',manifestId,'--staging',outDir,'--catalog-sources',catalogSources];

  // Add command-specific flags (skip Node-only flags)
  const skipFlags=new Set([...common.filter(f=>f!=='checkout'),'catalog-sources']);
  const pathFlags=new Set(['supplement','input','assessment-file','view-input']);
  for(let i=0;i<argv.length;i++) {
   const flag=argv[i];
   if(flag?.startsWith('--') && !skipFlags.has(flag.slice(2))) {
    toolArgs.push(flag);
    if(i+1<argv.length && !argv[i+1]?.startsWith('--')) {
     const val=argv[i+1];
     // Resolve $checkout and $output references
     if(val.startsWith('$checkout:')) {
      const id=val.slice('$checkout:'.length);
      const checkoutPath=checkouts.get(id);
      if(!checkoutPath) throw new Error(`unknown checkout ${id}`);
      toolArgs.push(checkoutPath);
      i++;
      continue;
     }
     if(val.startsWith('$output')) {
      // Skip output references
      i++;
      continue;
     }
     if(val.startsWith('$input/')) {
      // For conformance tests, resolve to corpus input path
      const relPath=val.slice('$input/'.length);
      // This is handled by the conformance test wrapper
      toolArgs.push(val);
      i++;
      continue;
     }
     // Resolve checkout flags to --checkout sourceId=path
     if(flag.slice(2)==='checkout' && val.includes('=')) {
      toolArgs.push(val);
      i++;
      continue;
     }
     // Only resolve path-type flags to absolute paths
     const flagName=flag.slice(2);
     if(pathFlags.has(flagName) && !isAbsolute(val)) toolArgs.push(resolve(cwd,val));
     else toolArgs.push(val);
     i++;
    }
   }
  }

  runImpactCli(toolArgs);

  // Read output files
  const files=new Map();
  for(const name of readdirSync(outDir).sort()) {
   let content=readFileSync(join(outDir,name),'utf8');
   if(content.charCodeAt(0)===0xFEFF) content=content.slice(1);
   files.set(name, content);
  }

  // Output checks (symlink, overwriting input)
  const inputPaths=[v.input,v['assessment-file'],v['description-file'],v.supplement,...(v['view-input']??[])].filter(Boolean).map(p=>canonicalPath(resolve(cwd,p)));
  for(const name of files.keys()) {
   const target=join(output,name);
   let stat;
   try { stat=lstatSync(target); } catch(e) { if(e.code!=='ENOENT') throw e; }
   if(stat?.isSymbolicLink()) throw new Error('output file is a symlink');
   if(inputPaths.includes(canonicalPath(target))) throw new Error('output would overwrite an input');
  }

  mkdirSync(output,{recursive:true});
  for(const [name,content] of files) writeFileSync(join(output,name),content);
  stdout?.write?.(`Wrote ${files.size} file(s)\n`);
  return 0;
 } catch(e) { stderr?.write?.(`${e.message}\n`); return 1; }
 finally {
  // Clean up staging
  try { rmSync(staging, { recursive: true, force: true }); } catch {}
 }
}
if(process.argv[1] && import.meta.url===pathToFileURL(process.argv[1]).href) process.exitCode=await runC4Cli(process.argv.slice(2));
