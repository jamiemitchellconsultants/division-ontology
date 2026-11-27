import {mkdtempSync,mkdirSync,rmSync,existsSync} from 'node:fs';
import {join} from 'node:path';
import {tmpdir} from 'node:os';
import {execFileSync} from 'node:child_process';
import {readFileSync} from 'node:fs';

const root = join(import.meta.dirname, '..', '..');
const cacheRoot = join(root, '.cache');
const out = join(cacheRoot, 'impact-cli');
const dll = join(out, 'RuntimeKnowledge.Impact.Cli.dll');

export function buildImpactCli() {
 const lock = join(cacheRoot, 'impact-cli.lock');
 mkdirSync(cacheRoot, { recursive: true });
 for (let i = 0; ; i++) {
  try { mkdirSync(lock); break; }
  catch (e) { if (e.code !== 'EEXIST' || i > 600) throw e; execFileSync('sleep', ['0.5']); if (existsSync(dll)) return dll; }
 }
 try {
  execFileSync('dotnet', ['build', join(root, 'runtime/RuntimeKnowledge.Impact.Cli'), '-c', 'Release', '-o', out, '--nologo', '-v', 'q'],
   { stdio: ['ignore', 'pipe', 'pipe'] });
  return dll;
 } finally { rmSync(lock, { recursive: true, force: true }); }
}

export function impactCli() {
 if (process.env.DIVISION_IMPACT_CLI) return process.env.DIVISION_IMPACT_CLI;
 return existsSync(dll) ? dll : buildImpactCli();
}

export function runImpactCli(args) {
 try {
  execFileSync('dotnet', [impactCli(), ...args], { encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] });
 } catch (error) {
  if (error.status === 1) throw new Error(String(error.stderr).replace(/\n$/, ''));
  throw error;
 }
}
