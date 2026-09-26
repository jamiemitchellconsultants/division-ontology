import { readFileSync } from 'node:fs';
import { DataFactory } from 'n3';

const RDF_TYPE = 'http://www.w3.org/1999/02/22-rdf-syntax-ns#type';
const RDFS_LABEL = 'http://www.w3.org/2000/01/rdf-schema#label';
const RDFS_COMMENT = 'http://www.w3.org/2000/01/rdf-schema#comment';
const RDFS_DOMAIN = 'http://www.w3.org/2000/01/rdf-schema#domain';
const RDFS_RANGE = 'http://www.w3.org/2000/01/rdf-schema#range';
const RDFS_SUBCLASS = 'http://www.w3.org/2000/01/rdf-schema#subClassOf';
const OWL_OBJECT_PROPERTY = 'http://www.w3.org/2002/07/owl#ObjectProperty';
const OWL_EQUIVALENT_CLASS = 'http://www.w3.org/2002/07/owl#equivalentClass';
const OWL_VERSION_INFO = 'http://www.w3.org/2002/07/owl#versionInfo';
const { namedNode, quad } = DataFactory;

function byCodepoint(a, b) { return a < b ? -1 : a > b ? 1 : 0; }
function iri(value, field) {
  if (typeof value !== 'string' || !/^https?:\/\/[^\s]+$/.test(value)) throw new Error(`${field} must be an HTTP IRI`);
  return value;
}

export function loadLinks(path) {
  const document = JSON.parse(readFileSync(path, 'utf8'));
  if (!document || typeof document.source !== 'string' || !Array.isArray(document.links)) throw new Error(`${path}: source and links are required`);
  const seen = new Set();
  return document.links.map((link) => {
    if (!['exact', 'specializes'].includes(link?.relation)) throw new Error(`link relation must be exact or specializes`);
    const local = iri(link.local, 'link local');
    const shared = iri(link.shared, 'link shared');
    if (seen.has(local)) throw new Error(`duplicate link for ${local}`);
    seen.add(local);
    return { source: document.source, local, shared, relation: link.relation };
  });
}

function resources(quads, namespace, source) {
  const bySubject = new Map();
  for (const item of quads) {
    if (item.subject.termType !== 'NamedNode' || !item.subject.value.startsWith(namespace)) continue;
    const local = item.subject.value.slice(namespace.length);
    if (!local || local.startsWith('_')) continue;
    if (!bySubject.has(item.subject.value)) bySubject.set(item.subject.value, []);
    bySubject.get(item.subject.value).push(item);
  }
  const concepts = [];
  const relations = [];
  for (const [subject, statements] of bySubject) {
    const objects = (predicate) => statements.filter((item) => item.predicate.value === predicate).map((item) => item.object.value);
    const label = objects(RDFS_LABEL)[0];
    const types = objects(RDF_TYPE).sort(byCodepoint);
    if (!label && !types.length) continue;
    concepts.push({ iri: subject, label: label ?? subject.slice(namespace.length), types, definition: objects(RDFS_COMMENT)[0] ?? '', source });
    if (types.includes(OWL_OBJECT_PROPERTY)) {
      relations.push({ iri: subject, label: label ?? subject.slice(namespace.length), source, domain: objects(RDFS_DOMAIN)[0] ?? null, range: objects(RDFS_RANGE)[0] ?? null,
        from: statements.find((item) => item.predicate.value.endsWith('#fromRaw'))?.object.value ?? null,
        to: statements.find((item) => item.predicate.value.endsWith('#toRaw'))?.object.value ?? null });
    }
  }
  concepts.sort((a, b) => byCodepoint(a.iri, b.iri));
  relations.sort((a, b) => byCodepoint(a.iri, b.iri));
  return { concepts, relations };
}

function rulesFor(quads, source) {
  const rules = [];
  for (const item of quads) {
    const predicate = item.predicate.value;
    if (!predicate.endsWith('#invariantText') && !predicate.endsWith('#invariant')) continue;
    const label = quads.find((candidate) => candidate.subject.equals(item.subject) && candidate.predicate.value.endsWith('#invariantLabel'))?.object.value ?? null;
    rules.push({ source, subject: item.subject.value, label, text: item.object.value });
  }
  return rules.sort((a, b) => byCodepoint(a.subject, b.subject));
}

export function buildCatalog({ coreQuads, sources, links, coreNamespace }) {
  const core = resources(coreQuads, coreNamespace, 'division-core');
  const concepts = [...core.concepts];
  const relations = [...core.relations];
  const rules = [...rulesFor(coreQuads, 'division-core')];
  const namedQuads = coreQuads.map((item) => quad(item.subject, item.predicate, item.object, namedNode('urn:division-ontology:core')));
  const sourceSummaries = [];
  const conceptOwner = new Map(core.concepts.map((concept) => [concept.iri, 'division-core']));
  const orderedSources = [...sources].sort((a, b) => byCodepoint(a.id, b.id));
  for (const source of orderedSources) {
    const projected = resources(source.quads, source.namespace, source.id);
    for (const concept of projected.concepts) {
      if (conceptOwner.has(concept.iri)) throw new Error(`concept IRI ${concept.iri} is defined by both ${conceptOwner.get(concept.iri)} and ${source.id}`);
      conceptOwner.set(concept.iri, source.id);
    }
    concepts.push(...projected.concepts);
    relations.push(...projected.relations);
    rules.push(...rulesFor(source.quads, source.id));
    sourceSummaries.push({ id: source.id, commit: source.commit, namespace: source.namespace, conceptCount: projected.concepts.length });
    const graph = namedNode(`urn:division-ontology:source:${encodeURIComponent(source.id)}:${source.commit}`);
    namedQuads.push(...source.quads.map((item) => quad(item.subject, item.predicate, item.object, graph)));
  }
  const conceptByIri = new Map(concepts.map((concept) => [concept.iri, concept]));
  const seenLinks = new Set();
  const orderedLinks = [...links].sort((a, b) => byCodepoint(`${a.source}|${a.local}`, `${b.source}|${b.local}`));
  for (const link of orderedLinks) {
    if (!sourceSummaries.some((source) => source.id === link.source)) throw new Error(`link source ${link.source} is not configured`);
    const local = conceptByIri.get(link.local);
    if (!local || local.source !== link.source) throw new Error(`link local concept ${link.local} not found in ${link.source}`);
    const shared = conceptByIri.get(link.shared);
    if (!shared || shared.source !== 'division-core') throw new Error(`link shared concept ${link.shared} not found in division core`);
    if (!['exact', 'specializes'].includes(link.relation)) throw new Error(`link relation must be exact or specializes`);
    if (link.relation === 'exact' && local.label !== shared.label) throw new Error(`exact link label differs: ${local.label} vs ${shared.label}`);
    if (seenLinks.has(link.local)) throw new Error(`duplicate link for ${link.local}`);
    seenLinks.add(link.local);
    namedQuads.push(quad(namedNode(link.local), namedNode(link.relation === 'exact' ? OWL_EQUIVALENT_CLASS : RDFS_SUBCLASS), namedNode(link.shared), namedNode('urn:division-ontology:links')));
  }
  concepts.sort((a, b) => byCodepoint(`${a.source}|${a.iri}`, `${b.source}|${b.iri}`));
  relations.sort((a, b) => byCodepoint(`${a.source}|${a.iri}`, `${b.source}|${b.iri}`));
  rules.sort((a, b) => byCodepoint(`${a.source}|${a.subject}`, `${b.source}|${b.subject}`));
  const version = coreQuads.find((item) => item.predicate.value === OWL_VERSION_INFO)?.object.value ?? null;
  return { catalog: { schemaVersion: 1, core: { namespace: coreNamespace, version, conceptCount: core.concepts.length }, sources: sourceSummaries, concepts, relations, rules, links: orderedLinks }, quads: namedQuads };
}
