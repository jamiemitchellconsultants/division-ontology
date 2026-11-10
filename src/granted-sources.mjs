import { execFileSync } from 'node:child_process';
import { InfrastructureError } from './errors.mjs';
import { IMPORT_CONFIG_MAX_BYTES, IMPORT_CONFIG_PATH, parseImportConfiguration } from './import-config.mjs';
import { REASON_CODES, reason } from './import-status.mjs';
import { readSourceOntology } from './rdf.mjs';
import { resolveSource } from './sources.mjs';

const git = (args, cwd) => execFileSync('git', args, { cwd, encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] });
const byCodepoint = (a, b) => a < b ? -1 : a > b ? 1 : 0;

function readConfiguration(directory, commit) {
  const listing = git(['ls-tree', commit, '--', IMPORT_CONFIG_PATH], directory).trimEnd();
  if (!listing) return { ok: false, reason: reason('config-missing', IMPORT_CONFIG_PATH) };
  const match = /^([0-9]{6}) blob ([0-9a-f]+)\t/.exec(listing);
  if (!match || !['100644', '100755'].includes(match[1])) return { ok: false, reason: reason('config-invalid', 'not a regular file') };
  if (Number(git(['cat-file', '-s', match[2]], directory)) > IMPORT_CONFIG_MAX_BYTES) return { ok: false, reason: reason('config-invalid', 'file is larger than 16 KiB') };
  return parseImportConfiguration(git(['show', `${commit}:${IMPORT_CONFIG_PATH}`], directory));
}

// A granted ontology may only make statements about its own terms (ADR 0012): the namespace itself,
// without its trailing # or /, is allowed as the ontology header.
function foreignSubject(quads, namespace) {
  const header = namespace.replace(/[#/]$/, '');
  for (const item of quads) {
    if (item.subject.termType !== 'NamedNode') continue;
    if (!item.subject.value.startsWith(namespace) && item.subject.value !== header) return item.subject.value;
  }
  return null;
}

function importOne(repository, urlFor, checkoutOverrides) {
  const { id, defaultBranch } = repository;
  let resolved;
  try { resolved = resolveSource({ id, url: urlFor(id), ref: defaultBranch }, checkoutOverrides); }
  catch (error) { throw new InfrastructureError(`${id}: ${error.message}`, { cause: error }); }
  try {
    const { directory, commit } = resolved;
    const configuration = readConfiguration(directory, commit);
    if (!configuration.ok) return { id, attemptedCommit: commit, result: configuration };
    const { ontologyPath, namespace } = configuration.config;
    try {
      const quads = readSourceOntology({ id, ontologyPath, namespace }, directory, commit);
      const foreign = foreignSubject(quads, namespace);
      if (foreign) return { id, attemptedCommit: commit, result: { ok: false, reason: reason('concept-outside-namespace', foreign) } };
      return { id, attemptedCommit: commit, result: { ok: true, namespace, quads } };
    } catch (error) {
      if (!REASON_CODES.includes(error.code)) throw error;
      return { id, attemptedCommit: commit, result: { ok: false, reason: reason(error.code, error.detail) } };
    }
  } catch (error) {
    if (error instanceof InfrastructureError) throw error;
    throw new InfrastructureError(`${id}: ${error.message}`, { cause: error });
  } finally { resolved.cleanup(); }
}

export function importGrantedSources({ repositories, published, urlFor = (id) => `https://github.com/${id}.git`, checkoutOverrides = new Map() }) {
  return [...repositories]
    .sort((a, b) => byCodepoint(a.id, b.id))
    .filter((repository) => repository.hasConfig || published.has(repository.id))
    .map((repository) => importOne(repository, urlFor, checkoutOverrides));
}
