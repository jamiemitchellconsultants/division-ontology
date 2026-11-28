// Two exported-page sources whose terms are linked to one core term, and a division with a relationship
// master list (I3, Task 21). Callers pin Git identities and dates (deterministic-git.mjs).
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
const REL = 'https://example.test/rel#';
export const CORE_CLAIM = 'https://example.test/core#Claim';

const git = (root, ...args) => execFileSync('git', args, { cwd: root, encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] }).trim();
const blobId = text => { const bytes = Buffer.from(text); return createHash('sha1').update(`blob ${bytes.length}\0`).update(bytes).digest('hex'); };
function put(root, path, data) { const f = join(root, path); mkdirSync(dirname(f), { recursive: true }); writeFileSync(f, data); }
function init(root, url) { mkdirSync(root, { recursive: true }); git(root, 'init', '-q'); git(root, 'remote', 'add', 'origin', url); }
function commit(root, message) { git(root, 'add', '.'); git(root, 'commit', '-qm', message); return git(root, 'rev-parse', 'HEAD'); }

// One source: an exported page cited by every assertion, nodes [name, label, denotes?] and assertions [subject, relationship, object].
function writeSource(root, base, page, nodes, assertions) {
  put(root, page.path, page.text);
  put(root, 'ontology.config.json', JSON.stringify({ sourceGraph: { enabled: true, root: 'graph', evidence: { 'selected-document': ['pages/**'] } } }));
  const w = new Writer();
  const add = (s, p, o) => w.addQuad(quad(namedNode(base + s), namedNode(p), o));
  const type = (s, t) => add(s, RDF + 'type', namedNode(K + t));
  type('graph', 'SourceKnowledgeGraph');
  add('graph', K + 'schemaVersion', literal(2));
  for (const [name, label, denotes] of nodes) {
    type(name, 'GraphNode');
    add(name, K + 'kind', namedNode('https://example.test/kinds#record'));
    add(name, RDFS + 'label', literal(label));
    if (denotes) add(name, K + 'denotes', namedNode(denotes));
  }
  type('ev-page', 'GraphEvidence');
  add('ev-page', K + 'path', literal(page.path));
  add('ev-page', K + 'startLine', literal(String(page.startLine)));
  add('ev-page', K + 'endLine', literal(String(page.endLine)));
  add('ev-page', K + 'blobId', literal(blobId(page.text)));
  add('ev-page', K + 'role', namedNode(K + 'role-selected-document'));
  assertions.forEach(([subject, relationship, object], i) => {
    const name = `assert-${i}`;
    type(name, 'GraphAssertion');
    add(name, K + 'subject', namedNode(base + subject));
    add(name, K + 'object', namedNode(base + object));
    add(name, K + 'relationship', namedNode(REL + relationship));
    add(name, K + 'basis', namedNode(K + 'direct'));
    add(name, K + 'evidence', namedNode(base + 'ev-page'));
  });
  type('coverage', 'GraphCoverage');
  add('coverage', K + 'pathSelector', literal('pages/**'));
  add('coverage', K + 'priority', literal('1'));
  add('coverage', K + 'status', namedNode(K + 'covered'));
  let text;
  w.end((e, result) => { if (e) throw e; text = result; });
  put(root, 'graph/manifest.ttl', text);
  return commit(root, 'initial');
}

export async function withMeaningFixture(run) {
  const root = mkdtempSync(join(tmpdir(), 'meaning-fixture-'));
  try {
    const divisionRoot = join(root, 'division'), claims = join(root, 'claims'), ledger = join(root, 'ledger');
    const C = 'https://example.test/claims/graph#', L = 'https://example.test/ledger/graph#';
    const claimsTerm = 'https://example.test/claims/ontology#Claim', ledgerTerm = 'https://example.test/ledger/ontology#Claim';

    init(claims, 'https://github.com/fixture/claims.git');
    const claimsCommit = writeSource(claims, C,
      { path: 'pages/claim-policy.md', startLine: 5, endLine: 8,
        text: '---\ntitle: Claim policy\nconfluence_url: https://confluence.example.test/pages/3001\n---\n' +
          'A claim affects its case.\nAuditors review every claim.\nA claim relates to its archive record.\nClaims mention the archive and are archived by it.\n' },
      [['claim', 'Claim', claimsTerm], ['case', 'Case', null], ['audit', 'Audit', null], ['archive', 'Archive record', null]],
      [['claim', 'affects', 'case'], ['audit', 'reviews', 'claim'], ['claim', 'relatesTo', 'archive'],
        ['claim', 'mentions', 'archive'], ['claim', 'archivedBy', 'archive']]);

    init(ledger, 'https://github.com/fixture/ledger.git');
    const ledgerCommit = writeSource(ledger, L,
      { path: 'pages/ledger.md', startLine: 5, endLine: 5,
        text: '---\ntitle: Ledger\nconfluence_url: https://confluence.example.test/pages/4001\n---\nA ledger entry affects its account.\n' },
      [['entry', 'Ledger entry', ledgerTerm], ['account', 'Account', null]],
      [['entry', 'affects', 'account']]);

    init(divisionRoot, 'https://github.com/fixture/division.git');
    const sources = [['fixture/claims', claimsCommit], ['fixture/ledger', ledgerCommit]];
    put(divisionRoot, 'sources.json', JSON.stringify({ sources: sources.map(([id]) => ({ id, url: `https://github.com/${id}.git` })) }));
    const concepts = [
      { iri: claimsTerm, source: 'fixture/claims', label: 'Claim' },
      { iri: ledgerTerm, source: 'fixture/ledger', label: 'Claim' },
      { iri: CORE_CLAIM, source: 'division-core', label: 'Claim' }];
    const links = [
      { source: 'fixture/claims', local: claimsTerm, shared: CORE_CLAIM, relation: 'exact' },
      { source: 'fixture/ledger', local: ledgerTerm, shared: CORE_CLAIM, relation: 'exact' }];
    const catalog = linked => JSON.stringify({ schemaVersion: 1, sources: sources.map(([id, commit]) => ({ id, commit })),
      concepts, relations: [], links: linked });
    put(divisionRoot, 'dist/catalog.json', catalog(links));
    put(divisionRoot, 'c4/mappings.json', JSON.stringify({ schemaVersion: 1, mappings: [] }));
    put(divisionRoot, 'c4/bindings.json', JSON.stringify({ schemaVersion: 1, bindings: [] }));
    put(divisionRoot, 'relationships/master-list.json', JSON.stringify({ schemaVersion: 1, relationships: [
      { iri: REL + 'affects', impact: 'subject-to-object' },
      { iri: REL + 'reviews', impact: 'object-to-subject' },
      { iri: REL + 'relatesTo', impact: 'both' },
      { iri: REL + 'mentions', impact: 'none' }] }, null, 2) + '\n');
    const catalogCommit = commit(divisionRoot, 'catalog with master list');

    put(divisionRoot, 'dist/catalog.json', catalog(links.filter(l => l.source !== 'fixture/claims')));
    const unlinkedCommit = commit(divisionRoot, 'remove the claims link');

    put(divisionRoot, 'relationships/master-list.json', '{"schemaVersion":1,"relationships":[{"iri":"x","impact":"sometimes"}]}\n');
    const corruptCommit = commit(divisionRoot, 'corrupt the master list');

    await run({
      divisionRoot, catalogCommit, registryCommit: catalogCommit, unlinkedCommit, corruptCommit,
      checkouts: new Map([['fixture/claims', claims], ['fixture/ledger', ledger]]),
      keys: {
        claim: { sourceId: 'fixture/claims', nodeIri: C + 'claim' },
        case: { sourceId: 'fixture/claims', nodeIri: C + 'case' },
        entry: { sourceId: 'fixture/ledger', nodeIri: L + 'entry' }
      },
      outputDir: join(root, 'output')
    });
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
}
