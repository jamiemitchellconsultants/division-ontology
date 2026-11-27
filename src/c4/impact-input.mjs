// Packages what the Node loaders verified for the .NET impact engine (ADR 0029).
// Key order is a contract: do not sort keys here.
export function buildImpactInput({ snapshot, registry, masterList = null }) {
  const omitUndefined = obj => Object.fromEntries(Object.entries(obj).filter(([, v]) => v !== undefined));
  const sources = [...snapshot.sources.values()].map(({ id, commit, config, graphState: g }) => ({
    id, commit,
    ...(config?.sourceGraph?.evidence ? { evidenceSelectors: config.sourceGraph.evidence } : {}),
    graph: {
      status: g.status,
      ...(g.status !== 'available' ? { reason: g.reason } : { schemaVersion: g.schemaVersion }),
      nodes: [...g.nodes.values()], assertions: [...g.assertions.values()],
      evidence: [...g.evidence.values()], coverage: g.coverage.map(omitUndefined)
    }
  }));
  return {
    schemaVersion: 1, snapshotId: snapshot.id, sources,
    catalog: { concepts: (snapshot.catalog.concepts ?? []).map(({ iri, source, label, definition }) =>
      definition === undefined ? { iri, source, label } : { iri, source, label, definition }) },
    reviewedLinks: (snapshot.catalog.links ?? []).map(({ source, local, shared, relation }) => ({ source, local, shared, relation })),
    registry: registry ? { commit: registry.commit, mappingSha256: registry.mappingSha256,
      bindingSha256: registry.bindingSha256, mappings: registry.mappings, bindings: registry.bindings, gaps: registry.gaps } : null,
    ...(masterList ? { masterList: masterList.identity } : {})
  };
}
export const impactInputBytes = input => Buffer.from(JSON.stringify(input) + '\n', 'utf8');
