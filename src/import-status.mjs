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
  const reserved = new Map(reservedNamespaces);
  const outcomeById = new Map(outcomes.map((outcome) => [outcome.id, outcome]));
  const present = new Map(discovery.repositories.map((repository) => [repository.id, repository]));
  const rows = [];
  const sources = [];
  const carry = (id, previous) => {
    sources.push({ id, namespace: previous.namespace, commit: previous.commit, quads: previous.quads, origin: 'granted', stale: true });
    reserved.set(previous.namespace, id);
  };

  const publishedIds = [...published.keys()].sort(byCodepoint);
  for (const id of publishedIds) {
    if (present.has(id) || registeredIds.has(id)) continue;
    const previous = published.get(id);
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
      if (holder) failure = reason('namespace-conflict', `overlaps ${holder}`);
    }
    if (!failure) {
      const { namespace, quads } = outcome.result;
      rows.push(row(id, 'current', outcome.attemptedCommit, outcome.attemptedCommit));
      sources.push({ id, namespace, commit: outcome.attemptedCommit, quads, origin: 'granted', stale: false });
      reserved.set(namespace, id);
      continue;
    }
    rows.push(row(id, 'invalid', outcome?.attemptedCommit ?? null, previous?.commit ?? null, failure));
    if (previous) carry(id, previous);
  }
  rows.sort((a, b) => byCodepoint(a.source, b.source));
  sources.sort((a, b) => byCodepoint(a.id, b.id));
  return { rows, sources };
}
