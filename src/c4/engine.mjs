import {mkdirSync,rmSync,existsSync,readdirSync,readFileSync,renameSync,statSync} from 'node:fs';
import {join,relative} from 'node:path';
import {createHash} from 'node:crypto';
import {execFileSync} from 'node:child_process';

const root = join(import.meta.dirname, '..', '..');
const cacheRoot = join(root, '.cache', 'impact-cli');
const projects = ['runtime/RuntimeKnowledge.Impact', 'runtime/RuntimeKnowledge.Impact.Cli'];
const shared = ['runtime/Directory.Packages.props', 'runtime/global.json'];
const dllName = 'RuntimeKnowledge.Impact.Cli.dll';

function buildInputs() {
 const files = [...shared];
 for (const project of projects)
  for (const name of readdirSync(join(root, project), { recursive: true })) {
   const path = join(project, name);
   if (/(^|[\\/])(bin|obj)([\\/]|$)/.test(relative(project, path)) || !statSync(join(root, path)).isFile()) continue;
   files.push(path);
  }
 return files.sort();
}

// A build is reused only for the exact engine sources it was built from, so a pull or branch switch rebuilds.
export function cliKey() {
 const hash = createHash('sha256');
 for (const path of buildInputs()) hash.update(path.replaceAll('\\', '/')).update('\0').update(readFileSync(join(root, path))).update('\0');
 return hash.digest('hex').slice(0, 16);
}

export function buildImpactCli(key = cliKey()) {
 const out = join(cacheRoot, key), dll = join(out, dllName), lock = `${out}.lock`;
 mkdirSync(cacheRoot, { recursive: true });
 for (let i = 0; ; i++) {
  if (existsSync(dll)) return dll;
  try { mkdirSync(lock); break; }
  catch (e) { if (e.code !== 'EEXIST' || i > 600) throw e; execFileSync('sleep', ['0.5']); }
 }
 const partial = `${out}.partial-${process.pid}`;
 try {
  if (existsSync(dll)) return dll;
  rmSync(partial, { recursive: true, force: true });
  execFileSync('dotnet', ['build', join(root, 'runtime/RuntimeKnowledge.Impact.Cli'), '-c', 'Release', '-o', partial, '--nologo', '-v', 'q'],
   { stdio: ['ignore', 'pipe', 'pipe'] });
  renameSync(partial, out);
  return dll;
 } finally {
  rmSync(partial, { recursive: true, force: true });
  rmSync(lock, { recursive: true, force: true });
 }
}

export function impactCli() {
 if (process.env.DIVISION_IMPACT_CLI) return process.env.DIVISION_IMPACT_CLI;
 return buildImpactCli();
}

export function runImpactCli(args) {
 try {
  execFileSync('dotnet', [impactCli(), ...args], { encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] });
 } catch (error) {
  if (error.status === 1) throw new Error(String(error.stderr).replace(/\n$/, ''));
  throw error;
 }
}
