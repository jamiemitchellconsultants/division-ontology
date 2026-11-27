import { execFileSync } from 'node:child_process';
import { appendFileSync, readFileSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import { runCli } from '../../src/cli.mjs';
import { setup, teardown } from '../fixtures/representative/setup.mjs';

export const key = (source, node) => ({
  sourceId: 'fixture/' + source,
  nodeIri: 'https://example.test/' + source + '/graph#' + node
});
export const shop = key('ecommerce', 'system');
export const api = key('ecommerce', 'container');
export const order = key('ecommerce', 'component');
export const warehouse = key('warehouse', 'system');
export const accounting = key('accounting', 'system');
export const bank = key('accounting', 'external');
export const address = key('ecommerce', 'address');
export const store = key('ecommerce', 'store');
export const unresolved = key('ecommerce', 'unresolved');

export const git = (cwd, ...args) =>
  execFileSync('git', args, { cwd, encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] }).trim();
function commit(cwd, message) {
  git(cwd, 'add', '.');
  git(cwd, '-c', 'commit.gpgsign=false', 'commit', '-qm', message);
  return git(cwd, 'rev-parse', 'HEAD');
}

export async function acceptanceFixture(root) {
  const fixture = await setup(root);
  const { divisionRoot, checkouts } = fixture;
  try {
    const checkout = checkouts.get(shop.sourceId);
    const graph = join(checkout, 'graph/manifest.ttl');
    const evidence = 'docs/acceptance-architecture.md';
    writeFileSync(join(checkout, evidence), [
      'The shopper uses the Ecommerce system.',
      'Storefront API writes to the Order store. Order processing calls Address validation.',
      'Address validation reads the Order store. Its registry parent is the Storefront API.',
      'The service inventory records Storefront API and Order store as containers.',
      'The component inventory records Address validation. Legacy routing is unverified.'
    ].join('\n') + '\n');
    const blob = git(checkout, 'hash-object', evidence);
    appendFileSync(graph, `
@prefix accept: <https://example.test/ecommerce/graph#> .
@prefix kit: <https://ontology-kit.dev/schema#> .
@prefix label: <http://www.w3.org/2000/01/rdf-schema#> .
accept:shopper a kit:GraphNode; kit:kind <https://example.test/person>; label:label "Shopper" .
accept:store a kit:GraphNode; kit:kind kit:component; label:label "Order store" .
accept:address a kit:GraphNode; kit:kind kit:component; label:label "Address validation" .
accept:unresolved a kit:GraphNode; kit:kind kit:component; label:label "Legacy routing" .
accept:architecture a kit:GraphEvidence; kit:path "${evidence}"; kit:startLine 1; kit:endLine 5; kit:blobId "${blob}"; kit:role kit:implementation .
accept:uses a kit:GraphAssertion; kit:subject accept:shopper; kit:object accept:system; kit:relationship kit:communicates-with; kit:basis kit:direct; kit:evidence accept:architecture .
accept:has-store a kit:GraphAssertion; kit:subject accept:system; kit:object accept:store; kit:relationship kit:contains; kit:basis kit:direct; kit:evidence accept:architecture .
accept:has-address a kit:GraphAssertion; kit:subject accept:container; kit:object accept:address; kit:relationship kit:contains; kit:basis kit:direct; kit:evidence accept:architecture .
accept:writes a kit:GraphAssertion; kit:subject accept:container; kit:object accept:store; kit:relationship kit:communicates-with; kit:basis kit:direct; kit:evidence accept:architecture .
accept:validates a kit:GraphAssertion; kit:subject accept:component; kit:object accept:address; kit:relationship kit:communicates-with; kit:basis kit:inferred; kit:evidence accept:architecture .
accept:reads a kit:GraphAssertion; kit:subject accept:address; kit:object accept:store; kit:relationship kit:communicates-with; kit:basis kit:direct; kit:evidence accept:architecture .
`);
    const mappingsPath = join(divisionRoot, 'c4/mappings.json');
    const registry = JSON.parse(readFileSync(mappingsPath, 'utf8'));
    for (const [node, level, role, parent, assertion] of [
      ['shopper', 'context', 'person', null, 'uses'],
      ['store', 'container', 'container', shop, 'has-store'],
      ['address', 'component', 'component', api, 'has-address'],
      ['unresolved', 'component', 'component', api, 'missing-support']
    ]) {
      registry.mappings.push({
        ...key('ecommerce', node), level, role, ...(parent ? { parent } : {}),
        supportAssertionIri: key('ecommerce', assertion).nodeIri,
        supportEvidenceIri: key('ecommerce', 'architecture').nodeIri
      });
    }
    writeFileSync(mappingsPath, JSON.stringify(registry, null, 2) + '\n');
    const oldSource = commit(checkout, 'Add acceptance architecture evidence');
    const flags = [...checkouts].flatMap(([id, path]) => ['--checkout', id + '=' + path]);
    await runCli(['build', ...flags], { root: divisionRoot });
    const baseline = commit(divisionRoot, 'Publish acceptance baseline catalog');

    writeFileSync(graph, readFileSync(graph, 'utf8').replace('"Order store"', '"Order store v2"'));
    appendFileSync(join(checkout, evidence), 'The second snapshot renames the Order store to Order store v2.\n');
    // Re-pin the evidence when its committed file changes.
    writeFileSync(graph, readFileSync(graph, 'utf8').replace(blob, git(checkout, 'hash-object', evidence)));
    const newSource = commit(checkout, 'Publish revised Order store label');
    await runCli(['build', ...flags], { root: divisionRoot });
    await runCli(['check', ...flags], { root: divisionRoot });
    const newer = commit(divisionRoot, 'Publish revised acceptance catalog');
    return { ...fixture, flags, baseline, newer, oldSource, newSource };
  } catch (error) {
    teardown(root);
    throw error;
  }
}
