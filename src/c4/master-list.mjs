import { createHash } from 'node:crypto';
import { readAtCommit } from './git.mjs';

export const MASTER_LIST_PATH = 'relationships/master-list.json';
const IMPACTS = new Set(['subject-to-object', 'object-to-subject', 'both', 'none']);

// Reads the division's relationship master list at the catalog commit (ADR 0032).
// The engine relies only on schemaVersion and relationships[].{iri, impact}; other fields are kept but ignored.
export function loadMasterList({ divisionRoot, catalogCommit }) {
  const raw = readAtCommit(divisionRoot, catalogCommit, MASTER_LIST_PATH);
  if (raw === null) return null;
  let list;
  try { list = JSON.parse(raw); } catch { throw new Error('invalid relationship master list'); }
  const seen = new Set();
  if (list?.schemaVersion !== 1 || !Array.isArray(list.relationships)) throw new Error('invalid relationship master list');
  for (const entry of list.relationships) {
    if (typeof entry?.iri !== 'string' || !entry.iri || !IMPACTS.has(entry.impact) || seen.has(entry.iri))
      throw new Error('invalid relationship master list');
    seen.add(entry.iri);
  }
  const bytes = Buffer.from(raw, 'utf8');
  return { bytes, identity: { status: 'listed', catalogCommit, sha256: createHash('sha256').update(bytes).digest('hex') } };
}
