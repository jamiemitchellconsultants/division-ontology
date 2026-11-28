// One source with 250 nodes labelled "Claim 000" to "Claim 249", each with one evidenced assertion to the
// next and no mappings, so an impact search pages over 100-lead pages (I4, Task 24). Callers pin Git.
import { mkdtempSync, mkdirSync, writeFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, dirname } from 'node:path';
import { execFileSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { Writer, DataFactory } from 'n3';
const { namedNode, literal, quad } = DataFactory;
const K = 'https://ontology-kit.dev/schema#';
const RDF = 'http://www.w3.org/1999/02/22-rdf-syntax-ns#';
const RDFS = 'http://www.w3.org/2000/01/rdf-schema#';
const G = 'https://example.test/paging/graph#';

const git = (root, ...args) => execFileSync('git', args, { cwd: root, encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] }).trim();
const blobId = text => { const bytes = Buffer.from(text); return createHash('sha1').update(`blob ${bytes.length}\0`).update(bytes).digest('hex'); };
function put(root, path, data) { const f = join(root, path); mkdirSync(dirname(f), { recursive: true }); writeFileSync(f, data); }
function init(root, url) { mkdirSync(root, { recursive: true }); git(root, 'init', '-q'); git(root, 'remote', 'add', 'origin', url); }
function commit(root, message) { git(root, 'add', '.'); git(root, 'commit', '-qm', message); return git(root, 'rev-parse', 'HEAD'); }

export const PAGING_NODES = 250;

export async function withPagingFixture(run) {
  const root = mkdtempSync(join(tmpdir(), 'paging-fixture-'));
  try {
    const divisionRoot = join(root, 'division'), claims = join(root, 'claims');
    init(claims, 'https://github.com/fixture/paging.git');
    const text = 'Each claim leads to the next claim.\n';
    put(claims, 'src/claims.txt', text);
    put(claims, 'ontology.config.json', JSON.stringify({ sourceGraph: { enabled: true, root: 'graph', evidence: { implementation: ['src/**'] } } }));

    const w = new Writer();
    const add = (s, p, o) => w.addQuad(quad(namedNode(G + s), namedNode(p), o));
    const type = (s, t) => add(s, RDF + 'type', namedNode(K + t));
    type('graph', 'SourceKnowledgeGraph');
    add('graph', K + 'schemaVersion', literal(2));
    type('evidence', 'GraphEvidence');
    add('evidence', K + 'path', literal('src/claims.txt'));
    add('evidence', K + 'startLine', literal('1'));
    add('evidence', K + 'endLine', literal('1'));
    add('evidence', K + 'blobId', literal(blobId(text)));
    add('evidence', K + 'role', namedNode(K + 'role-implementation'));
    const name = i => `claim-${String(i).padStart(3, '0')}`;
    for (let i = 0; i < PAGING_NODES; i++) {
      type(name(i), 'GraphNode');
      add(name(i), K + 'kind', namedNode('https://example.test/kinds#record'));
      add(name(i), RDFS + 'label', literal(`Claim ${String(i).padStart(3, '0')}`));
      const assertion = `next-${String(i).padStart(3, '0')}`;
      type(assertion, 'GraphAssertion');
      add(assertion, K + 'subject', namedNode(G + name(i)));
      add(assertion, K + 'object', namedNode(G + name((i + 1) % PAGING_NODES)));
      add(assertion, K + 'relationship', namedNode('https://example.test/rel#next'));
      add(assertion, K + 'basis', namedNode(K + 'direct'));
      add(assertion, K + 'evidence', namedNode(G + 'evidence'));
    }
    type('coverage', 'GraphCoverage');
    add('coverage', K + 'pathSelector', literal('src/**'));
    add('coverage', K + 'priority', literal('1'));
    add('coverage', K + 'status', namedNode(K + 'covered'));
    let graph;
    w.end((e, result) => { if (e) throw e; graph = result; });
    put(claims, 'graph/manifest.ttl', graph);
    const sourceCommit = commit(claims, 'initial');

    init(divisionRoot, 'https://github.com/fixture/division.git');
    put(divisionRoot, 'sources.json', JSON.stringify({ sources: [{ id: 'fixture/paging', url: 'https://github.com/fixture/paging.git' }] }));
    put(divisionRoot, 'dist/catalog.json', JSON.stringify({ schemaVersion: 1, sources: [{ id: 'fixture/paging', commit: sourceCommit }],
      concepts: [], relations: [], links: [] }));
    put(divisionRoot, 'c4/mappings.json', JSON.stringify({ schemaVersion: 1, mappings: [] }));
    put(divisionRoot, 'c4/bindings.json', JSON.stringify({ schemaVersion: 1, bindings: [] }));
    const catalogCommit = commit(divisionRoot, 'catalog');

    await run({ divisionRoot, catalogCommit, registryCommit: catalogCommit,
      checkouts: new Map([['fixture/paging', claims]]), outputDir: join(root, 'output') });
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
}
