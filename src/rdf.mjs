import { readFileSync } from 'node:fs';
import { join } from 'node:path';
import { Parser } from 'n3';

export function parseTurtle(text, baseIRI) {
  try {
    return new Parser({ baseIRI, format: 'text/turtle' }).parse(text);
  } catch (error) {
    throw new Error(`invalid Turtle: ${error.message}`, { cause: error });
  }
}

export function readSourceOntology(source, directory) {
  const text = readFileSync(join(directory, source.ontologyPath), 'utf8');
  const quads = parseTurtle(text, source.namespace);
  if (!quads.some((quad) => quad.subject.termType === 'NamedNode' && quad.subject.value.startsWith(source.namespace))) {
    throw new Error(`${source.id}: ontology does not contain a subject in configured namespace ${source.namespace}`);
  }
  return quads;
}
