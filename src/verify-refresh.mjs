import { cpSync, lstatSync, mkdtempSync, readFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { runCli } from './cli.mjs';
import { classifyRefresh } from './refresh-policy.mjs';

const names = ['catalog.json', 'catalog.md', 'combined.nq'];
const generated = new Set(names.map((name) => `dist/${name}`));

function regular(path) {
  try { return lstatSync(path).isFile(); } catch { return false; }
}

export async function verifyRefresh({ baseRoot, candidateDist, changedPaths, checkoutOverrides = new Map() }) {
  if (changedPaths.length === 0) return { mode: 'none', reasons: [] };
  const generatedChange = changedPaths.some((path) => generated.has(path));
  const otherChange = changedPaths.some((path) => !generated.has(path));
  if (otherChange && generatedChange) throw new Error('generated catalog files cannot be mixed with other PR changes');
  if (otherChange) return { mode: 'review', reasons: ['change includes a file outside generated dist'] };

  const temporary = mkdtempSync(join(tmpdir(), 'division-verify-'));
  try {
    cpSync(join(baseRoot, 'core'), join(temporary, 'core'), { recursive: true });
    cpSync(join(baseRoot, 'links'), join(temporary, 'links'), { recursive: true });
    cpSync(join(baseRoot, 'sources.json'), join(temporary, 'sources.json'));
    const args = ['build'];
    for (const [id, checkout] of checkoutOverrides) args.push('--checkout', `${id}=${checkout}`);
    await runCli(args, { root: temporary });
    for (const name of names) {
      const candidate = join(candidateDist, name);
      if (!regular(candidate)) throw new Error(`dist/${name} must be a regular file`);
      if (!readFileSync(candidate).equals(readFileSync(join(temporary, 'dist', name)))) throw new Error(`dist/${name} does not match a fresh build from current sources`);
    }
    return classifyRefresh({
      previousCatalog: JSON.parse(readFileSync(join(baseRoot, 'dist/catalog.json'), 'utf8')),
      nextCatalog: JSON.parse(readFileSync(join(temporary, 'dist/catalog.json'), 'utf8')),
      previousNQuads: readFileSync(join(baseRoot, 'dist/combined.nq'), 'utf8'),
      nextNQuads: readFileSync(join(temporary, 'dist/combined.nq'), 'utf8'),
      changedPaths,
    });
  } finally { rmSync(temporary, { recursive: true, force: true }); }
}
