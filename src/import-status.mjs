import { existsSync, readFileSync } from 'node:fs';
import { join } from 'node:path';
import { Parser } from 'n3';

export const STATUSES = ['current', 'invalid', 'not configured', 'access removed'];
export const REASON_CODES = ['config-missing', 'config-invalid', 'ontology-unreadable', 'ontology-invalid-turtle',
  'concept-outside-namespace', 'no-concepts', 'namespace-conflict', 'access-removed'];

const MAX_DETAIL = 200;
const secrets = [
  /\bgh[pousr]_\w{16,}/g,
  /\bgithub_pat_\w{16,}/g,
  /x-access-token/gi,
];

export function safeDetail(value) {
  let text = String(value ?? '').replace(/[\u0000-\u001f\u007f]/g, ' ');
  text = text.replace(/\/\/[^/\s:@]+:[^/\s@]+@/g, '//[redacted]@');
  for (const pattern of secrets) text = text.replace(pattern, '[redacted]');
  text = text.trim();
  return text.length > MAX_DETAIL ? `${text.slice(0, MAX_DETAIL - 1)}…` : text;
}

export function reason(code, detail = '') {
  if (!REASON_CODES.includes(code)) throw new Error(`unknown reason code ${code}`);
  return { code, detail: safeDetail(detail) };
}

const byCodepoint = (a, b) => a < b ? -1 : a > b ? 1 : 0;
const row = (source, status, attemptedCommit, publishedCommit, why) => ({ source, status, attemptedCommit, publishedCommit, ...(why ? { reason: why } : {}) });

function overlappingHolder(reserved, namespace, id) {
  for (const [held, holder] of reserved) {
    if (holder !== id && (held.startsWith(namespace) || namespace.startsWith(held))) return holder;
  }
  return null;
}

// Turns discovery, per-source import outcomes and the committed catalog into one status row per granted source
// and the granted sources the catalog should carry (ADR 0011, ADR 0012).
export function classifyGrantedSources({ discovery, outcomes, published, registeredIds, reservedNamespaces }) {
  const registered = new Set([...registeredIds].map((id) => id.toLowerCase()));
  const isRegistered = (id) => registered.has(id.toLowerCase());
  const reserved = new Map(reservedNamespaces);
  // Every published granted source keeps its namespace until it is replaced, so no other source can displace it (review 2).
  for (const [id, previous] of published) if (!isRegistered(id) && !reserved.has(previous.namespace)) reserved.set(previous.namespace, id);
  const outcomeById = new Map(outcomes.map((outcome) => [outcome.id, outcome]));
  const present = new Map(discovery.repositories.map((repository) => [repository.id, repository]));
  const rows = [];
  const sources = [];
  // A published version whose namespace the core or a steward-registered source now holds is dropped, not carried (review 4).
  const blockedBy = (previous, id) => overlappingHolder(reservedNamespaces, previous.namespace, id);
  const conflict = (holder) => reason('namespace-conflict', `overlaps ${holder}`);
  const carry = (id, previous) => {
    sources.push({ id, namespace: previous.namespace, commit: previous.commit, quads: previous.quads, origin: 'granted', stale: true });
    if (!reserved.has(previous.namespace)) reserved.set(previous.namespace, id);
  };

  const publishedIds = [...published.keys()].sort(byCodepoint);
  for (const id of publishedIds) {
    if (present.has(id) || isRegistered(id)) continue;
    const previous = published.get(id);
    const holder = blockedBy(previous, id);
    if (holder) { rows.push(row(id, 'invalid', null, null, conflict(holder))); continue; }
    rows.push(row(id, 'access removed', null, previous.commit, reason('access-removed', 'repository is no longer in the installation')));
    carry(id, previous);
  }

  const ids = [...present.keys()].sort(byCodepoint);
  const ordered = [...ids.filter((id) => published.has(id)), ...ids.filter((id) => !published.has(id))];
  for (const id of ordered) {
    const previous = published.get(id);
    const outcome = outcomeById.get(id);
    if (!outcome && !previous) {
      if (discovery.selection === 'selected') rows.push(row(id, 'not configured', null, null, reason('config-missing', '.division-ontology.json')));
      continue;
    }
    let failure = outcome ? (outcome.result.ok ? null : outcome.result.reason) : reason('config-missing', '.division-ontology.json');
    if (outcome?.result.ok) {
      const holder = overlappingHolder(reserved, outcome.result.namespace, id);
      if (holder) failure = conflict(holder);
    }
    if (!failure) {
      const { namespace, quads } = outcome.result;
      if (previous && previous.namespace !== namespace && reserved.get(previous.namespace) === id) reserved.delete(previous.namespace);
      rows.push(row(id, 'current', outcome.attemptedCommit, outcome.attemptedCommit));
      sources.push({ id, namespace, commit: outcome.attemptedCommit, quads, origin: 'granted', stale: false });
      reserved.set(namespace, id);
      continue;
    }
    const blocker = previous ? blockedBy(previous, id) : null;
    rows.push(row(id, 'invalid', outcome?.attemptedCommit ?? null, blocker ? null : previous?.commit ?? null, blocker ? conflict(blocker) : failure));
    if (previous && !blocker) carry(id, previous);
  }
  rows.sort((a, b) => byCodepoint(a.source, b.source));
  sources.sort((a, b) => byCodepoint(a.id, b.id));
  return { rows, sources };
}

// The committed dist/ is a build input: it holds the last published version of every granted source (ADR 0011).
export function readPublishedState(root) {
  const catalogPath = join(root, 'dist/catalog.json');
  const published = new Map();
  if (!existsSync(catalogPath)) return published;
  const granted = JSON.parse(readFileSync(catalogPath, 'utf8')).sources.filter((source) => source.origin === 'granted');
  if (!granted.length) return published;
  const all = new Parser({ format: 'N-Quads', blankNodePrefix: '' }).parse(readFileSync(join(root, 'dist/combined.nq'), 'utf8'));
  for (const { id, commit, namespace } of granted) {
    const graph = `urn:division-ontology:source:${encodeURIComponent(id)}:${commit}`;
    const quads = all.filter((item) => item.graph.value === graph);
    if (!quads.length) throw new Error(`${id}: published graph is missing from dist/combined.nq`);
    published.set(id, { commit, namespace, quads });
  }
  return published;
}
