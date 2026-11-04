import {execFileSync} from 'node:child_process';
import {isAbsolute} from 'node:path';
import {realpathSync} from 'node:fs';

const sha = /^[0-9a-f]{40}$/;
function run(root, args) { return execFileSync('git', args, {cwd:root, encoding:'utf8', stdio:['ignore','pipe','pipe']}); }
function commitOk(root, commit) {
  if (!sha.test(commit)) throw new Error('invalid commit');
  try { run(root,['cat-file','-e',`${commit}^{commit}`]); } catch { throw new Error(`unavailable commit ${commit}`); }
}
function safePath(path) {
  if (typeof path !== 'string' || !path || isAbsolute(path) || path.includes('\\') || path.split('/').some(s => !s || s === '.' || s === '..') || /[\x00-\x1f]/.test(path)) throw new Error('unsafe repository path');
}
export function readAtCommit(root, commit, path) {
  commitOk(root,commit); safePath(path);
  const type=run(root,['ls-tree',commit,'--',path]).trim();
  if (!type) return null;
  const line=type.split('\n').find(x => x.endsWith(`\t${path}`));
  if (!line) return null;
  if (!line.startsWith('100644 blob ') && !line.startsWith('100755 blob ')) throw new Error(`unsafe non-file path ${path}`);
  return run(root,['show',`${commit}:${path}`]);
}
export function listAtCommit(root, commit, path) {
  commitOk(root,commit); safePath(path);
  return run(root,['ls-tree','-r','--name-only',commit,'--',path]).split('\n').filter(Boolean).sort();
}
export function verifyCheckout(root, expectedUrl, commit) {
  if (!root || !isAbsolute(root)) throw new Error('missing checkout');
  let top, origin;
  try { top=run(root,['rev-parse','--show-toplevel']).trim(); origin=run(root,['remote','get-url','origin']).trim(); }
  catch { throw new Error('invalid checkout'); }
  if (realpathSync(top)!==realpathSync(root)) throw new Error('checkout must be repository root');
  const normalize = url => url.replace(/\.git\/?$/,'').replace(/\/$/,'').replace(/^git@([^:]+):/,'https://$1/');
  if (normalize(origin)!==normalize(expectedUrl)) throw new Error('checkout origin identity mismatch');
  if (run(root,['status','--porcelain','--untracked-files=normal']).trim()) throw new Error('dirty checkout');
  commitOk(root,commit);
}
