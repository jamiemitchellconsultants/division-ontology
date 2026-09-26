import { existsSync, mkdtempSync, readFileSync, renameSync, rmSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { Writer } from 'n3';

const names = ['catalog.json', 'catalog.md', 'combined.nq'];
const byCodepoint = (a, b) => a < b ? -1 : a > b ? 1 : 0;
const cell = (value) => String(value ?? '').replace(/\|/g, '\\|').replace(/\r?\n/g, ' ').trim();

function markdown(catalog) {
  const lines = ['# Division ontology catalog', '', `Shared core version: ${catalog.core.version ?? 'unversioned'}. Concepts: ${catalog.core.conceptCount}.`, '', '## Sources', '', '| Repository | Commit | Concepts |', '| --- | --- | ---: |'];
  for (const source of catalog.sources) lines.push(`| ${source.id} | \`${source.commit}\` | ${source.conceptCount} |`);
  lines.push('', '## Shared vocabulary', '', '| Term | IRI | Definition |', '| --- | --- | --- |');
  for (const concept of catalog.concepts.filter((item) => item.source === 'division-core')) lines.push(`| \`${cell(concept.label)}\` | ${cell(concept.iri)} | ${cell(concept.definition)} |`);
  for (const source of catalog.sources) {
    lines.push('', `## ${source.id}`, '', '| Term | IRI | Definition | Shared link |', '| --- | --- | --- | --- |');
    for (const concept of catalog.concepts.filter((item) => item.source === source.id)) {
      const link = catalog.links.find((item) => item.source === source.id && item.local === concept.iri);
      lines.push(`| \`${cell(concept.label)}\` | ${cell(concept.iri)} | ${cell(concept.definition)} | ${link ? `${link.relation}: ${cell(link.shared)}` : ''} |`);
    }
  }
  if (catalog.relations.length) {
    lines.push('', '## Relationships', '', '| Source | Relationship | From | To |', '| --- | --- | --- | --- |');
    for (const relation of catalog.relations) lines.push(`| ${cell(relation.source)} | ${cell(relation.label)} | ${cell(relation.domain ?? relation.from)} | ${cell(relation.range ?? relation.to)} |`);
  }
  if (catalog.rules.length) {
    lines.push('', '## Rules and invariants', '', '| Source | Concept | Rule |', '| --- | --- | --- |');
    for (const rule of catalog.rules) lines.push(`| ${cell(rule.source)} | ${cell(rule.label ?? rule.subject)} | ${cell(rule.text)} |`);
  }
  return `${lines.join('\n')}\n`;
}

function nquads(quads) {
  const writer = new Writer({ format: 'N-Quads' });
  const key = (item) => JSON.stringify([item.graph.termType, item.graph.id, item.subject.termType, item.subject.id, item.predicate.id, item.object.termType, item.object.id]);
  const ordered = [...quads].sort((a, b) => byCodepoint(key(a), key(b)));
  writer.addQuads(ordered);
  return new Promise((resolve, reject) => writer.end((error, result) => error ? reject(error) : resolve(result)));
}

export async function renderOutputs(catalog, quads) {
  return {
    'catalog.json': `${JSON.stringify(catalog, null, 2)}\n`,
    'catalog.md': markdown(catalog),
    'combined.nq': await nquads(quads),
  };
}

export function checkOutputs(root, outputs) {
  const stale = names.filter((name) => !existsSync(join(root, 'dist', name)) || readFileSync(join(root, 'dist', name), 'utf8') !== outputs[name]);
  if (stale.length) throw new Error(`generated outputs are stale: ${stale.join(', ')}`);
}

export function writeOutputs(root, outputs) {
  const temporary = mkdtempSync(join(root, '.dist-build-'));
  const destination = join(root, 'dist');
  const backup = `${temporary}-previous`;
  let movedOld = false;
  try {
    for (const name of names) writeFileSync(join(temporary, name), outputs[name]);
    if (existsSync(destination)) { renameSync(destination, backup); movedOld = true; }
    try { renameSync(temporary, destination); }
    catch (error) {
      if (movedOld) renameSync(backup, destination);
      throw error;
    }
    if (movedOld) rmSync(backup, { recursive: true, force: true });
  } finally {
    if (existsSync(temporary)) rmSync(temporary, { recursive: true, force: true });
  }
}
