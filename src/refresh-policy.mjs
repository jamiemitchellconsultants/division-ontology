import { Parser } from 'n3';

const generatedPaths = new Set(['dist/catalog.json', 'dist/catalog.md', 'dist/combined.nq']);
const coreGraph = 'urn:division-ontology:core';
const sortJson = (values) => values.map((value) => JSON.stringify(value)).sort();
const same = (left, right) => JSON.stringify(left) === JSON.stringify(right);

function parseQuads(text) {
  return new Parser({ format: 'N-Quads', blankNodePrefix: 'refresh_' }).parse(text);
}

function statements(quads, predicate) {
  return sortJson(quads.filter(predicate).map((item) => [item.subject.termType, item.subject.id, item.predicate.id, item.object.termType, item.object.id]));
}

function conceptView(catalog, predicate) {
  return sortJson(catalog.concepts.filter(predicate).map((item) => [item.iri, item.label, item.definition, [...item.types].sort()]));
}

function sourceView(catalog) {
  return sortJson(catalog.sources.map((item) => [item.id, item.namespace]));
}

function linkView(catalog) {
  return sortJson(catalog.links.map((item) => [item.source, item.local, item.shared, item.relation]));
}

function ruleView(catalog, source, labels) {
  return sortJson(catalog.rules.filter((item) => item.source === source && labels.has(item.label)).map((item) => [item.subject, item.label, item.text]));
}

export function classifyRefresh({ previousCatalog, nextCatalog, previousNQuads, nextNQuads, changedPaths }) {
  if (changedPaths.length === 0) return { mode: 'none', reasons: [] };
  const reasons = [];
  if (changedPaths.some((path) => !generatedPaths.has(path))) reasons.push('change includes a file outside generated dist');
  if (!same(sourceView(previousCatalog), sourceView(nextCatalog))) reasons.push('source registration changed');
  if (!same(linkView(previousCatalog), linkView(nextCatalog))) reasons.push('reviewed links changed');
  if (!same(conceptView(previousCatalog, (item) => item.source === 'division-core'), conceptView(nextCatalog, (item) => item.source === 'division-core')) || !same(previousCatalog.core, nextCatalog.core)) reasons.push('division core changed');

  const previousQuads = parseQuads(previousNQuads);
  const nextQuads = parseQuads(nextNQuads);
  if (!same(statements(previousQuads, (item) => item.graph.value === coreGraph), statements(nextQuads, (item) => item.graph.value === coreGraph))) reasons.push('division core triples changed');

  const linked = new Map();
  for (const link of [...previousCatalog.links, ...nextCatalog.links]) linked.set(link.local, link.source);
  for (const [iri, source] of linked) {
    const previous = previousCatalog.concepts.find((item) => item.iri === iri && item.source === source);
    const next = nextCatalog.concepts.find((item) => item.iri === iri && item.source === source);
    if (!previous || !next || !same([previous.label, previous.definition, [...previous.types].sort()], [next.label, next.definition, [...next.types].sort()])) {
      reasons.push(`linked concept changed: ${iri}`);
      continue;
    }
    if (!same(statements(previousQuads, (item) => item.subject.value === iri), statements(nextQuads, (item) => item.subject.value === iri))) reasons.push(`linked concept triples changed: ${iri}`);
    const labels = new Set([previous.label, next.label]);
    if (!same(ruleView(previousCatalog, source, labels), ruleView(nextCatalog, source, labels))) reasons.push(`linked concept rules changed: ${iri}`);
  }
  return { mode: reasons.length ? 'review' : 'automatic', reasons };
}
