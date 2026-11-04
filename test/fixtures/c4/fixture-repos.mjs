import {mkdtempSync, mkdirSync, writeFileSync, readFileSync, rmSync} from 'node:fs';
import {tmpdir} from 'node:os';
import {join, dirname} from 'node:path';
import {execFileSync} from 'node:child_process';
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
    const sourceCommits={alpha:commit(alpha,'initial'),beta:commit(beta,'initial')};
    init(divisionRoot,'https://github.com/fixture/division.git');
    put(divisionRoot,'sources.json',JSON.stringify({sources:sources.map(([id])=>({id,url:`https://github.com/${id}.git`}))}));
    put(divisionRoot,'dist/catalog.json',JSON.stringify({schemaVersion:1,sources:[{id:'fixture/Alpha',commit:sourceCommits.alpha},{id:'fixture/Beta',commit:sourceCommits.beta}],concepts:[]}));
    const catalogCommit=commit(divisionRoot,'catalog');
    await run({divisionRoot,catalogCommit,registryCommit:catalogCommit,checkouts:new Map(sources),sourceCommits,outputDir:join(root,'output'),writeDivision:(p,d)=>put(divisionRoot,p,d),readDivision:p=>readFileSync(join(divisionRoot,p),'utf8'),commitDivision:m=>commit(divisionRoot,m),writeSource:(id,p,d)=>put(new Map(sources).get(id),p,d),commitSource:(id,m)=>commit(new Map(sources).get(id),m)});
  } finally { rmSync(root,{recursive:true,force:true}); }
}
