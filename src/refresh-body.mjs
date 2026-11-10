import { execFileSync } from 'node:child_process';
import { readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { pathToFileURL } from 'node:url';
import { classifyRefresh } from './refresh-policy.mjs';

export function renderRefreshBody({ catalog, mode, reasons, fingerprint }) {
  if (!/^[0-9a-f]{12}$/.test(fingerprint)) throw new Error('fingerprint must be 12 lowercase hex characters');
  const lines = [
    '## Catalog refresh', '',
    `Status: **${mode === 'automatic' ? 'automatic' : 'steward review required'}**. Generated files are checked against a fresh build from the configured source refs.`, '',
    '| Source | Commit | Concepts |', '| --- | --- | ---: |',
    ...catalog.sources.map((source) => `| ${source.id} | \`${source.commit}\` | ${source.conceptCount} |`), '',
  ];
  if (catalog.importStatus?.length) {
    const commit = (value) => value ? `\`${value.slice(0, 12)}\`` : '—';
    lines.push('### Import status', '', '| Source | Status | Published commit | Reason |', '| --- | --- | --- | --- |',
      ...catalog.importStatus.map((item) => `| ${item.source} | ${item.status} | ${commit(item.publishedCommit)} | ${item.reason ? `\`${`${item.reason.code}: ${item.reason.detail}`.replace(/`/g, "'").replace(/\|/g, '\\|')}\`` : ''} |`), '');
  }
  if (reasons.length) lines.push('Review reasons:', '', ...reasons.map((reason) => `- ${reason}`), '');
  lines.push(`AI-Fingerprint: sha256:${fingerprint}`, '');
  return lines.join('\n');
}

function committed(root, path) { return execFileSync('git', ['show', `HEAD:${path}`], { cwd: root, encoding: 'utf8' }); }

export function prepareRefreshBody(root, bodyPath) {
  const changedPaths = execFileSync('git', ['diff', '--name-only', '--', 'dist/'], { cwd: root, encoding: 'utf8' }).trim().split('\n').filter(Boolean);
  if (!changedPaths.length) return 'none';
  const nextCatalog = JSON.parse(readFileSync(join(root, 'dist/catalog.json'), 'utf8'));
  const result = classifyRefresh({
    previousCatalog: JSON.parse(committed(root, 'dist/catalog.json')),
    nextCatalog,
    previousNQuads: committed(root, 'dist/combined.nq'),
    nextNQuads: readFileSync(join(root, 'dist/combined.nq'), 'utf8'),
    changedPaths,
  });
  writeFileSync(bodyPath, renderRefreshBody({ catalog: nextCatalog, ...result, fingerprint: '000000000000' }));
  return result.mode;
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  const [command, bodyPath, fingerprint] = process.argv.slice(2);
  if (command === 'prepare') console.log(prepareRefreshBody(process.cwd(), bodyPath));
  else if (command === 'finalize') {
    const body = readFileSync(bodyPath, 'utf8');
    writeFileSync(bodyPath, body.replace(/^AI-Fingerprint: sha256:[0-9a-f]{12}$/m, `AI-Fingerprint: sha256:${fingerprint}`));
  } else throw new Error('usage: refresh-body.mjs prepare|finalize body-path [fingerprint]');
}
