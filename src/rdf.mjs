import { execFileSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { DataFactory, Parser } from 'n3';

const RDF_TYPE = 'http://www.w3.org/1999/02/22-rdf-syntax-ns#type';
const OWL = 'http://www.w3.org/2002/07/owl#';
const RDFS = 'http://www.w3.org/2000/01/rdf-schema#';
const RDF = 'http://www.w3.org/1999/02/22-rdf-syntax-ns#';
const conceptTypes = new Set([`${OWL}Class`, `${OWL}ObjectProperty`, `${OWL}DatatypeProperty`, `${RDFS}Class`, `${RDF}Property`]);

export function parseTurtle(text, baseIRI) {
  try {
    const prefix = `b${createHash('sha256').update(baseIRI).digest('hex').slice(0, 16)}_`;
    let generated = 0;
    const factory = { ...DataFactory, blankNode: (label) => DataFactory.blankNode(label ?? `${prefix}g${generated++}`) };
    return new Parser({ baseIRI, format: 'text/turtle', blankNodePrefix: prefix, factory }).parse(text);
  } catch (error) {
    throw new Error(`invalid Turtle: ${error.message}`, { cause: error });
  }
}

export function readSourceOntology(source, directory, commit) {
  if (!/^[0-9a-f]{40,64}$/.test(commit ?? '')) throw new Error(`${source.id}: a source commit is required`);
  const listing = execFileSync('git', ['ls-tree', commit, '--', source.ontologyPath], { cwd: directory, encoding: 'utf8' }).trimEnd();
  const [entry] = listing.split('\n');
  const match = /^([0-9]{6}) blob [0-9a-f]+\t(.+)$/.exec(entry);
  if (!match || match[2] !== source.ontologyPath || !['100644', '100755'].includes(match[1])) {
    throw new Error(`${source.id}: ontologyPath must identify a committed regular blob, not a symlink`);
  }
  const text = execFileSync('git', ['show', `${commit}:${source.ontologyPath}`], { cwd: directory, encoding: 'utf8' });
  const quads = parseTurtle(text, source.namespace);
  for (const item of quads) {
    if (item.subject.termType === 'NamedNode' && item.predicate.value === RDF_TYPE && conceptTypes.has(item.object.value) && !item.subject.value.startsWith(source.namespace)) {
      throw new Error(`${source.id}: concept ${item.subject.value} is outside configured namespace ${source.namespace}`);
    }
  }
  if (!quads.some((item) => item.subject.termType === 'NamedNode' && item.subject.value.startsWith(source.namespace) && !item.subject.value.slice(source.namespace.length).startsWith('_') && (item.predicate.value === RDF_TYPE || item.predicate.value === `${RDFS}label`))) {
    throw new Error(`${source.id}: ontology has no concepts in configured namespace ${source.namespace}`);
  }
  return quads;
}
