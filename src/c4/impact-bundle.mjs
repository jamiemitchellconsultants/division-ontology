// Writes a verified bundle for the .NET impact engine (ADR 0029).
import { mkdirSync, mkdtempSync, renameSync, rmSync, writeFileSync, lstatSync } from 'node:fs';
import { basename, dirname, join, resolve } from 'node:path';
import { hash, jsonBytes, pinnedBytes } from '../workspace/bundle.mjs';
import { buildImpactInput, impactInputBytes } from './impact-input.mjs';
import { listAtCommit, readAtCommit } from './git.mjs';
import { MASTER_LIST_PATH } from './master-list.mjs';

const digestPattern = /^[0-9a-f]{64}$/;

export function buildImpactBundle({ snapshot, registry, masterList = null, catalogBytes, extraFiles = [] }) {
  const files = new Map();
  const blobs = new Map();
  function include(role, sourceId, path, bytes) {
    if (bytes === null) return;
    const sha256 = hash(bytes);
    const id = JSON.stringify([role, sourceId, path]);
    files.set(id, { role, sourceId, path, sha256, bytes: bytes.length });
    blobs.set(sha256, bytes);
  }

  // impactInput
  const impactInput = buildImpactInput({ snapshot, registry, masterList });
  include('impactInput', 'division-core', 'impact-input.json', impactInputBytes(impactInput));
  if (masterList) include('relationshipMasterList', 'division-core', MASTER_LIST_PATH, masterList.bytes);

  // evidence files from each source
  for (const source of snapshot.sources.values()) {
    const { id, commit, checkout, config, graphState: g } = source;
    if (g.status !== 'available') continue;
    for (const evidence of g.evidence.values()) {
      include('graphEvidence', id, evidence.path, pinnedBytes(checkout, commit, evidence.path));
    }
  }

  // identity evidence from bindings
  if (registry) {
    for (const binding of registry.bindings) {
      const source = snapshot.sources.get(binding.identityEvidence.sourceId);
      if (source) {
        include('graphEvidence', source.id, binding.identityEvidence.path,
          pinnedBytes(source.checkout, source.commit, binding.identityEvidence.path));
      }
    }
  }

  // extra files (inspectedSource)
  for (const { sourceId, path } of extraFiles) {
    const source = snapshot.sources.get(sourceId);
    if (!source) continue;
    const content = readAtCommit(source.checkout, source.commit, path);
    if (content !== null) {
      include('inspectedSource', sourceId, path, Buffer.from(content, 'utf8'));
    }
  }

  // manifest
  const sources = [...snapshot.sources.values()].map(({ id, commit, graphState: g }) => ({
    id, commit, graphSchemaVersion: g.status === 'available' ? g.schemaVersion : null,
    graphStatus: g.status
  }));
  const catalogSha256 = hash(catalogBytes);
  const manifest = { schemaVersion: 1, divisionCommit: snapshot.id.catalogCommit, catalogSha256,
    sources, registryCommit: registry?.commit ?? null, files: [...files.values()] };
  const manifestBytes = jsonBytes(manifest);
  const id = hash(manifestBytes);
  return { id, manifest, manifestBytes, blobs };
}

export function writeImpactBundle(bundle, outputDir) {
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
