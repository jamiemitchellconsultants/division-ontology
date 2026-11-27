import { execFileSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { lstatSync, mkdirSync, mkdtempSync, renameSync, rmSync, writeFileSync } from 'node:fs';
import { basename, dirname, join, resolve } from 'node:path';
import { listAtCommit, readAtCommit } from '../c4/git.mjs';
import { coverageGaps } from '../c4/graph.mjs';
import { loadRegistries } from '../c4/registry.mjs';
import { loadSnapshot } from '../c4/snapshot.mjs';
import { buildImpactInput, impactInputBytes } from '../c4/impact-input.mjs';
import { loadMasterList, MASTER_LIST_PATH } from '../c4/master-list.mjs';

export const hash = bytes => createHash('sha256').update(bytes).digest('hex');
const compare = (a, b) => a < b ? -1 : a > b ? 1 : 0;
const digestPattern = /^[0-9a-f]{64}$/;
const key = (sourceId, kind, identifier) => ({ sourceId, kind, identifier });
const keyText = ({ sourceId, kind, identifier }) => JSON.stringify([sourceId, kind, identifier]);

function ordered(value) {
  if (Array.isArray(value)) return value.map(ordered);
  if (value === null || typeof value !== 'object') return value;
  return Object.fromEntries(Object.keys(value).sort(compare).map(name => [name, ordered(value[name])]));
}

export const jsonBytes = value => Buffer.from(JSON.stringify(ordered(value)) + '\n', 'utf8');

export function pinnedBytes(root, commit, path, required = true) {
  // The existing loader rejects unsafe paths, unavailable commits and symlinks.
  if (readAtCommit(root, commit, path) === null) {
    if (!required) return null;
    throw new Error(`missing pinned file ${path}`);
  }
  return execFileSync('git', ['cat-file', 'blob', `${commit}:${path}`],
    { cwd: root, stdio: ['ignore', 'pipe', 'pipe'], maxBuffer: 64 * 1024 * 1024 });
}

function project(snapshot, registry) {
  const entities = new Map();
  const relationshipTypes = new Map();
  const edges = new Map();
  const gaps = [...coverageGaps(snapshot), ...(registry?.gaps ?? [])];
  const catalogTerms = new Map();
  const sourceCommit = sourceId => {
    if (sourceId === 'division-core') return snapshot.id.catalogCommit;
    const source = snapshot.sources.get(sourceId);
    if (!source) throw new Error(`unknown published source ${sourceId}`);
    return source.commit;
  };
  const provenance = (recordKey, basis = null, evidence = []) => ({
    key: recordKey, sourceCommit: sourceCommit(recordKey.sourceId), basis, evidence
  });
  function add(table, row) {
    const id = keyText(row.key);
    if (table.has(id)) throw new Error(`duplicate published identity ${id}`);
    table.set(id, row);
  }
  function rememberTerm(recordKey) {
    const keys = catalogTerms.get(recordKey.identifier) ?? [];
    keys.push(recordKey);
    catalogTerms.set(recordKey.identifier, keys);
  }
  function findTerm(sourceId, iri, allowUniqueElsewhere = false) {
    const candidates = catalogTerms.get(iri) ?? [];
    const local = candidates.filter(candidate => candidate.sourceId === sourceId);
    return local.length === 1 ? local[0]
      : local.length === 0 && allowUniqueElsewhere && candidates.length === 1 ? candidates[0] : null;
  }
  const addEdge = (recordKey, from, to, label, relationshipType, basis, evidence, context) => {
    add(edges, { key: recordKey, from, to, label, relationshipType, provenance: provenance(recordKey, basis, evidence), context });
  };

  for (const concept of snapshot.catalog.concepts) {
    const recordKey = key(concept.source, 'concept', concept.iri);
    add(entities, { key: recordKey, label: concept.label, description: concept.definition ?? null,
      types: concept.types ?? [], provenance: provenance(recordKey) });
    rememberTerm(recordKey);
  }
  for (const relation of snapshot.catalog.relations) {
    const recordKey = key(relation.source, 'relationshipType', relation.iri);
    add(relationshipTypes, { key: recordKey, label: relation.label ?? relation.iri, description: null,
      domain: relation.domain ?? null, range: relation.range ?? null,
      context: { from: relation.from ?? null, to: relation.to ?? null }, provenance: provenance(recordKey) });
    rememberTerm(recordKey);
  }
  for (const link of snapshot.catalog.links) {
    const local = findTerm(link.source, link.local);
    const shared = findTerm('division-core', link.shared);
    if (!local || !shared) throw new Error('reviewed link references a missing published term');
    const recordKey = key(link.source, 'reviewedLink', JSON.stringify([link.local, link.shared, link.relation]));
    addEdge(recordKey, local, shared, link.relation, null, null, [], { relation: link.relation });
  }

  const mappings = new Map((registry?.mappings ?? []).map(mapping =>
    [keyText(key(mapping.sourceId, 'node', mapping.nodeIri)), mapping]));
  for (const source of snapshot.sources.values()) {
    const { id, graphState } = source;
    for (const node of graphState.nodes.values()) {
      const recordKey = key(id, 'node', node.iri);
      const c4 = mappings.get(keyText(recordKey));
      add(entities, { key: recordKey, label: node.label, description: null, types: [node.kind],
        provenance: provenance(recordKey), ...(c4 ? { c4: { ...c4, registryCommit: registry.commit } } : {}) });
      if (node.denotes) {
        const term = findTerm(id, node.denotes, true);
        if (term) {
          addEdge(key(id, 'denotation', JSON.stringify([node.iri, node.denotes])), recordKey, term,
            'denotes', null, null, [], null);
        } else {
          gaps.push({ code: 'denotation-unresolved', sourceId: id, nodeIri: node.iri,
            reason: 'The referenced ontology term is missing or ambiguous in this snapshot' });
        }
      }
    }
    for (const assertion of graphState.assertions.values()) {
      const typeKey = key(id, 'relationshipType', assertion.relationship);
      if (!relationshipTypes.has(keyText(typeKey))) {
        add(relationshipTypes, { key: typeKey, label: assertion.relationship, description: null,
          domain: null, range: null, provenance: provenance(typeKey) });
      }
      addEdge(key(id, 'assertion', assertion.iri), key(id, 'node', assertion.subject),
        key(id, 'node', assertion.object), relationshipTypes.get(keyText(typeKey)).label,
        typeKey, assertion.basis, assertion.evidenceIris.map(iri => graphState.evidence.get(iri)), null);
    }
  }
  for (const binding of registry?.bindings ?? []) {
    const { fromSourceId, externalNodeIri, toSourceId, targetNodeIri } = binding;
    const support = snapshot.sources.get(fromSourceId).graphState.evidence.get(binding.sourceEvidenceIri);
    const recordKey = key(fromSourceId, 'c4Binding',
      JSON.stringify([externalNodeIri, toSourceId, targetNodeIri]));
    addEdge(recordKey, key(fromSourceId, 'node', externalNodeIri), key(toSourceId, 'node', targetNodeIri),
      'Reviewed C4 binding', null, null, [support], { registryCommit: registry.commit, ...binding });
  }
  const rows = table => [...table.entries()].sort(([a], [b]) => compare(a, b)).map(([, row]) => row);
  return { entities: rows(entities), relationshipTypes: rows(relationshipTypes), edges: rows(edges),
    gaps: gaps.sort((a, b) => compare(JSON.stringify(ordered(a)), JSON.stringify(ordered(b)))) };
}

export function buildBundle({ divisionRoot, catalogCommit, registryCommit = null, checkouts }) {
  const snapshot = loadSnapshot({ divisionRoot, catalogCommit, checkouts });
  if (snapshot.catalog.schemaVersion !== 1) throw new Error('unsupported catalog schema');
  const registry = registryCommit ? loadRegistries({ divisionRoot, registryCommit, snapshot }) : null;
  const files = new Map();
  const blobs = new Map();
  function include(role, sourceId, path, bytes) {
    if (bytes === null) return;
    const sha256 = hash(bytes);
    const id = JSON.stringify([role, sourceId, path]);
    const previous = files.get(id);
    if (previous && previous.sha256 !== sha256) throw new Error(`conflicting pinned bytes for ${path}`);
    files.set(id, { role, sourceId, path, sha256, bytes: bytes.length });
    blobs.set(sha256, bytes);
  }

  const catalog = pinnedBytes(divisionRoot, catalogCommit, 'dist/catalog.json');
  include('catalog', 'division-core', 'dist/catalog.json', catalog);
  include('nquads', 'division-core', 'dist/combined.nq', pinnedBytes(divisionRoot, catalogCommit, 'dist/combined.nq'));
  include('sourceInventory', 'division-core', 'sources.json', pinnedBytes(divisionRoot, catalogCommit, 'sources.json'));
  const sources = [];
  for (const source of snapshot.sources.values()) {
    const { id, commit, checkout, config, graphState } = source;
    const available = graphState.status === 'available';
    sources.push({ id, commit, graphSchemaVersion: available ? graphState.schemaVersion : null, graphStatus: graphState.status });
    include('graphConfig', id, 'ontology.config.json', pinnedBytes(checkout, commit, 'ontology.config.json', false));
    if (available) {
      for (const path of listAtCommit(checkout, commit, config.sourceGraph.root)) {
        if (path.endsWith('.ttl')) include('graphTurtle', id, path, pinnedBytes(checkout, commit, path));
      }
      for (const evidence of graphState.evidence.values()) {
        include('graphEvidence', id, evidence.path, pinnedBytes(checkout, commit, evidence.path));
      }
    }
  }
  if (registry) {
    for (const path of ['c4/mappings.json', 'c4/bindings.json']) {
      include('c4Registry', 'division-core', path, pinnedBytes(divisionRoot, registryCommit, path));
    }
    for (const { identityEvidence } of registry.bindings) {
      const source = snapshot.sources.get(identityEvidence.sourceId);
      include('graphEvidence', source.id, identityEvidence.path,
        pinnedBytes(source.checkout, source.commit, identityEvidence.path));
    }
  }
  const masterList = loadMasterList({ divisionRoot, catalogCommit });
  include('impactInput', 'division-core', 'impact-input.json',
    impactInputBytes(buildImpactInput({ snapshot, registry, masterList })));
  if (masterList) include('relationshipMasterList', 'division-core', MASTER_LIST_PATH, masterList.bytes);
  include('projection', 'division-core', 'projection.json', jsonBytes(project(snapshot, registry)));
  const manifest = { schemaVersion: 1, divisionCommit: catalogCommit, catalogSha256: hash(catalog),
    sources, registryCommit, files: [...files.entries()].sort(([a], [b]) => compare(a, b)).map(([, row]) => row) };
  const manifestBytes = jsonBytes(manifest);
  return { id: hash(manifestBytes), manifest, manifestBytes, blobs };
}

export function writeBundle(bundle, outputDir) {
  const { id, manifest, manifestBytes, blobs } = bundle;
  if (!digestPattern.test(id) || !jsonBytes(manifest).equals(manifestBytes) || hash(manifestBytes) !== id) {
    throw new Error('manifest digest mismatch');
  }
  const verified = new Map();
  for (const file of manifest.files) {
    const bytes = blobs.get(file.sha256);
    if (!digestPattern.test(file.sha256) || !Buffer.isBuffer(bytes) ||
        bytes.length !== file.bytes || hash(bytes) !== file.sha256) {
      throw new Error(`bundle object digest or length mismatch: ${file.path}`);
    }
    verified.set(file.sha256, bytes);
  }

  const target = resolve(outputDir);
  if (lstatSync(target, { throwIfNoEntry: false })) throw new Error('bundle output already exists');
  mkdirSync(dirname(target), { recursive: true });
  const staging = mkdtempSync(join(dirname(target), `.${basename(target)}-`));
  try {
    mkdirSync(join(staging, 'blobs'));
    mkdirSync(join(staging, 'manifests'));
    for (const [sha256, bytes] of verified) writeFileSync(join(staging, 'blobs', sha256), bytes);
    writeFileSync(join(staging, 'manifests', `${id}.json`), manifestBytes);
    renameSync(staging, target);
  } finally {
    rmSync(staging, { force: true, recursive: true });
  }
}
