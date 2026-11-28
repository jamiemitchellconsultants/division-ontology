import test from 'node:test';
import assert from 'node:assert/strict';
import {existsSync,readFileSync,writeFileSync} from 'node:fs';
import {join} from 'node:path';
import {withPilotFixture} from './fixtures/c4/fixture-repos.mjs';
import {runC4Cli} from '../src/c4/cli.mjs';
test('description, paths and four views run at one snapshot',async()=>{
 await withPilotFixture(async f=>{
  const common=['--catalog-commit',f.catalogCommit,'--registry-commit',f.registryCommit,...[...f.checkouts].flatMap(([id,path])=>['--checkout',`${id}=${path}`]),'--output',f.outputDir];
  assert.equal(await runC4Cli(['search',...common,'--query','Alpha API'],{cwd:f.divisionRoot}),0);
  assert.equal(await runC4Cli(['paths',...common,'--start',`fixture/Alpha=${f.keys.alphaSystem.nodeIri}`],{cwd:f.divisionRoot}),0);
  for(const [level,focus] of [['context',f.keys.alphaSystem],['container',f.keys.alphaSystem],['component',f.keys.alphaContainer],['code',f.keys.alphaComponent]]) assert.equal(await runC4Cli(['view',...common,'--level',level,'--focus',`${focus.sourceId}=${focus.nodeIri}`],{cwd:f.divisionRoot}),0);
  assert.ok(existsSync(join(f.outputDir,'view-context.json')));assert.match(readFileSync(join(f.outputDir,'view-context.md'),'utf8'),/Evidence/);
 });
});
test('invalid flags leave no output',async()=>{await withPilotFixture(async f=>{assert.notEqual(await runC4Cli(['search','--unknown','x','--output',f.outputDir],{cwd:f.divisionRoot}),0);assert.equal(existsSync(f.outputDir),false);});});
test('assessment and feedback render against the pinned snapshot',async()=>{
 await withPilotFixture(async f=>{
  const common=['--catalog-commit',f.catalogCommit,'--registry-commit',f.registryCommit,...[...f.checkouts].flatMap(([id,path])=>['--checkout',`${id}=${path}`]),'--output',f.outputDir];
  const description=join(f.divisionRoot,'description.txt');writeFileSync(description,'Change Alpha API');
  assert.equal(await runC4Cli(['assessment-init',...common,'--request-id','CR-1','--description-file',description],{cwd:f.divisionRoot}),0);
  const input=join(f.outputDir,'assessment.json'),record=JSON.parse(readFileSync(input,'utf8'));record.assessor='Architect';record.confirmedLeads=[f.keys.alphaSystem];writeFileSync(input,JSON.stringify(record));
  assert.equal(await runC4Cli(['assessment-render',...common,'--input',input],{cwd:f.divisionRoot}),0);
  const markdown=readFileSync(join(f.outputDir,'assessment.md'),'utf8'),digest=markdown.match(/Assessment digest: ([a-f0-9]{64})/)[1];
  const feedback=join(f.outputDir,'feedback-input.json');writeFileSync(feedback,JSON.stringify({schemaVersion:1,assessmentDigest:digest,architect:'Architect',reviewer:'Reviewer',perceivedAccuracy:'good',perceivedCompleteness:'partial',confidence:'medium',usefulDiscoveries:[],suspectedMisses:[],falseLeads:[],examples:[],openQuestions:[]}));
  assert.equal(await runC4Cli(['feedback-render',...common,'--input',feedback,'--assessment-file',input],{cwd:f.divisionRoot}),0);assert.ok(existsSync(join(f.outputDir,'feedback.md')));
 });
});
test('new source revision is pinned and absent graph remains available as a gap',async()=>{
 await withPilotFixture(async f=>{
  const id='fixture/Alpha';f.writeSource(id,'ontology.config.json',JSON.stringify({sourceGraph:{enabled:false}}));const newer=f.commitSource(id,'disable graph');
  const catalog=JSON.parse(f.readDivision('dist/catalog.json'));catalog.sources[0].commit=newer;f.writeDivision('dist/catalog.json',JSON.stringify(catalog));const catalogCommit=f.commitDivision('new snapshot');
  const common=['--catalog-commit',catalogCommit,'--registry-commit',f.registryCommit,...[...f.checkouts].flatMap(([name,path])=>['--checkout',`${name}=${path}`]),'--output',f.outputDir];
  assert.equal(await runC4Cli(['search',...common,'--query','unmatched'],{cwd:f.divisionRoot}),0);
  const result=JSON.parse(readFileSync(join(f.outputDir,'candidates.json'),'utf8'));assert.equal(result.snapshotId.sources[0].commit,newer);assert.ok(result.gaps.some(g=>g.code==='graph-unavailable'));
 });
});
test('failed validation writes no output directory',async()=>{
 await withPilotFixture(async f=>{
  const common=['--catalog-commit','0'.repeat(40),'--registry-commit',f.registryCommit,...[...f.checkouts].flatMap(([id,path])=>['--checkout',`${id}=${path}`]),'--output',f.outputDir];
  assert.notEqual(await runC4Cli(['search',...common,'--query','Alpha'],{cwd:f.divisionRoot}),0);assert.equal(existsSync(f.outputDir),false);
 });
});
test('code views accept pinned C#, TypeScript, Java and Python source lines',async()=>{
 await withPilotFixture(async f=>{
  const {execFileSync}=await import('node:child_process');const checkout=f.checkouts.get('fixture/Alpha');
  const common=['--catalog-commit',f.catalogCommit,'--registry-commit',f.registryCommit,...[...f.checkouts].flatMap(([id,path])=>['--checkout',`${id}=${path}`]),'--output',f.outputDir];
  for(const path of ['src/App.cs','src/app.ts','src/App.java','src/app.py']) {
   const blob=execFileSync('git',['rev-parse',`${f.sourceCommits.alpha}:${path}`],{cwd:checkout,encoding:'utf8'}).trim(),supplement={...f.codeSupplement(),path,blobId:blob};
   const file=join(f.divisionRoot,'supplement.json');writeFileSync(file,JSON.stringify(supplement));
   assert.equal(await runC4Cli(['view',...common,'--level','code','--focus',`${f.keys.alphaComponent.sourceId}=${f.keys.alphaComponent.nodeIri}`,'--supplement',file],{cwd:f.divisionRoot}),0);
   assert.match(readFileSync(join(f.outputDir,'view-code.md'),'utf8'),/architect-inspected/);
  }
 });
});
test('assessment renders a cited inspected-code view',async()=>{
 await withPilotFixture(async f=>{
  const common=['--catalog-commit',f.catalogCommit,'--registry-commit',f.registryCommit,...[...f.checkouts].flatMap(([id,path])=>['--checkout',`${id}=${path}`]),'--output',f.outputDir];
  const supplement=join(f.divisionRoot,'supplement.json');writeFileSync(supplement,JSON.stringify(f.codeSupplement()));
  assert.equal(await runC4Cli(['view',...common,'--level','code','--focus',`${f.keys.alphaComponent.sourceId}=${f.keys.alphaComponent.nodeIri}`,'--supplement',supplement],{cwd:f.divisionRoot}),0);
  const viewPath=join(f.outputDir,'view-code.json'),view=JSON.parse(readFileSync(viewPath,'utf8'));
  const desc=join(f.divisionRoot,'desc.txt');writeFileSync(desc,'Change');assert.equal(await runC4Cli(['assessment-init',...common,'--request-id','CR-1','--description-file',desc],{cwd:f.divisionRoot}),0);
  const input=join(f.outputDir,'assessment.json'),a=JSON.parse(readFileSync(input,'utf8'));a.assessor='Architect';
  const {viewDigest}=await import('./fixtures/c4/digest.mjs');a.likelyImpacts=[{key:view.items.find(x=>x.basis==='architect-inspected').key,reason:'Inspected',viewIds:[viewDigest(view)],pathIds:[],evidenceIris:[]}];writeFileSync(input,JSON.stringify(a));
  assert.equal(await runC4Cli(['assessment-render',...common,'--input',input,'--view-input',viewPath],{cwd:f.divisionRoot}),0);
  const digest=readFileSync(join(f.outputDir,'assessment.md'),'utf8').match(/Assessment digest: ([a-f0-9]{64})/)[1];
  const feedback=join(f.divisionRoot,'feedback-input.json');writeFileSync(feedback,JSON.stringify({schemaVersion:1,assessmentDigest:digest,architect:'Architect',reviewer:'Reviewer',perceivedAccuracy:'good',perceivedCompleteness:'partial',confidence:'medium',usefulDiscoveries:[],suspectedMisses:[],falseLeads:[],examples:[],openQuestions:[]}));
  const stderr={text:'',write(t){this.text+=t;}};
  assert.equal(await runC4Cli(['feedback-render',...common,'--input',feedback,'--assessment-file',input,'--view-input',viewPath],{cwd:f.divisionRoot,stderr}),0,stderr.text);
 });
});
test('feedback rendering requires the assessment file',async()=>{
 await withPilotFixture(async f=>{
  const common=['--catalog-commit',f.catalogCommit,'--registry-commit',f.registryCommit,...[...f.checkouts].flatMap(([id,path])=>['--checkout',`${id}=${path}`]),'--output',f.outputDir];
  const feedback=join(f.divisionRoot,'feedback-input.json');writeFileSync(feedback,JSON.stringify({schemaVersion:1,assessmentDigest:'0'.repeat(64),architect:'Architect',reviewer:'Reviewer',perceivedAccuracy:'good',perceivedCompleteness:'partial',confidence:'medium',usefulDiscoveries:[],suspectedMisses:[],falseLeads:[],examples:[],openQuestions:[]}));
  const stderr={text:'',write(t){this.text+=t;}};
  assert.equal(await runC4Cli(['feedback-render',...common,'--input',feedback],{cwd:f.divisionRoot,stderr}),1);
  assert.match(stderr.text,/assessment file is required/);assert.equal(existsSync(f.outputDir),false);
 });
});
test('a run leaves no staging directory behind, on success or failure',async()=>{
 await withPilotFixture(async f=>{
  const {readdirSync}=await import('node:fs');const {tmpdir}=await import('node:os');
  const staged=()=>new Set(readdirSync(tmpdir()).filter(n=>n.startsWith('division-impact-')));
  const before=staged();
  const common=['--catalog-commit',f.catalogCommit,'--registry-commit',f.registryCommit,...[...f.checkouts].flatMap(([id,path])=>['--checkout',`${id}=${path}`]),'--output',f.outputDir];
  assert.equal(await runC4Cli(['search',...common,'--query','Alpha API'],{cwd:f.divisionRoot}),0);
  assert.notEqual(await runC4Cli(['paths',...common,'--start','fixture/Alpha=urn:missing','--max-depth','9'],{cwd:f.divisionRoot,stderr:{write(){}}}),0);
  assert.deepEqual([...staged()].filter(n=>!before.has(n)),[]);
 });
});
test('a relative description file resolves against the supplied working directory',async()=>{
 await withPilotFixture(async f=>{
  const common=['--catalog-commit',f.catalogCommit,'--registry-commit',f.registryCommit,...[...f.checkouts].flatMap(([id,path])=>['--checkout',`${id}=${path}`]),'--output',f.outputDir];
  writeFileSync(join(f.divisionRoot,'description.txt'),'Change Alpha API');
  const stderr={text:'',write(t){this.text+=t;}};
  assert.equal(await runC4Cli(['assessment-init',...common,'--request-id','CR-1','--description-file','description.txt'],{cwd:f.divisionRoot,stderr}),0,stderr.text);
  assert.equal(JSON.parse(readFileSync(join(f.outputDir,'assessment.json'),'utf8')).description,'Change Alpha API');
 });
});
test('depth accepts an integral decimal and reports the range for anything else',async()=>{
 await withPilotFixture(async f=>{
  const common=['--catalog-commit',f.catalogCommit,'--registry-commit',f.registryCommit,...[...f.checkouts].flatMap(([id,path])=>['--checkout',`${id}=${path}`]),'--output',f.outputDir];
  const start=['--start',`fixture/Alpha=${f.keys.alphaSystem.nodeIri}`];
  assert.equal(await runC4Cli(['paths',...common,...start,'--max-depth','4.0'],{cwd:f.divisionRoot}),0);
  for(const depth of ['abc','4.5','7']) {
   const stderr={text:'',write(t){this.text+=t;}};
   assert.equal(await runC4Cli(['paths',...common,...start,'--max-depth',depth],{cwd:f.divisionRoot,stderr}),1);
   assert.equal(stderr.text,'depth must be from 1 to 6\n',depth);
  }
 });
});
test('assessment rendering never overwrites its input file',async()=>{
 await withPilotFixture(async f=>{
  const common=['--catalog-commit',f.catalogCommit,'--registry-commit',f.registryCommit,...[...f.checkouts].flatMap(([id,path])=>['--checkout',`${id}=${path}`]),'--output',f.outputDir];
  const desc=join(f.divisionRoot,'desc.txt');writeFileSync(desc,'Change');await runC4Cli(['assessment-init',...common,'--request-id','CR-1','--description-file',desc],{cwd:f.divisionRoot});
  const a=JSON.parse(readFileSync(join(f.outputDir,'assessment.json'),'utf8'));a.assessor='Architect';const input=join(f.outputDir,'assessment.md'),raw=JSON.stringify(a);writeFileSync(input,raw);
  assert.notEqual(await runC4Cli(['assessment-render',...common,'--input',input],{cwd:f.divisionRoot}),0);assert.equal(readFileSync(input,'utf8'),raw);
 });
});
test('output symlinks cannot modify a source checkout',async()=>{
 await withPilotFixture(async f=>{
  const {mkdirSync,symlinkSync}=await import('node:fs');const source=f.checkouts.get('fixture/Alpha'),target=join(source,'README.md'),before=readFileSync(target,'utf8');
  mkdirSync(f.outputDir);symlinkSync(target,join(f.outputDir,'candidates.json'));
  const common=['--catalog-commit',f.catalogCommit,'--registry-commit',f.registryCommit,...[...f.checkouts].flatMap(([id,path])=>['--checkout',`${id}=${path}`]),'--output',f.outputDir];
  assert.notEqual(await runC4Cli(['search',...common,'--query','Alpha'],{cwd:f.divisionRoot}),0);assert.equal(readFileSync(target,'utf8'),before);
 });
});
