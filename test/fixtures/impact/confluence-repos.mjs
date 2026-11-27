import { mkdtempSync, mkdirSync, writeFileSync, readFileSync, rmSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, dirname } from 'node:path';
import { execFileSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { Writer, DataFactory } from 'n3';
const { namedNode, literal, quad } = DataFactory;
const K = 'https://ontology-kit.dev/schema#';
const RDF = 'http://www.w3.org/1999/02/22-rdf-syntax-ns#';
const RDFS = 'http://www.w3.org/2000/01/rdf-schema#';

const git = (root, ...args) =>
  execFileSync('git', args, { cwd: root, encoding: 'utf8', stdio: ['ignore', 'pipe', 'pipe'] }).trim();

function blobId(content) {
  const bytes = Buffer.from(content);
  return createHash('sha1').update(`blob ${bytes.length}\0`).update(bytes).digest('hex');
}

function put(root, path, data) {
  const f = join(root, path);
  mkdirSync(dirname(f), { recursive: true });
  writeFileSync(f, data);
}

function init(root, url) {
  mkdirSync(root, { recursive: true });
  git(root, 'init', '-q');
  git(root, 'config', 'user.name', 'Fixture');
  git(root, 'config', 'user.email', 'fixture@example.test');
  git(root, 'remote', 'add', 'origin', url);
}

function commit(root, message) {
  git(root, 'add', '.');
  git(root, 'commit', '-qm', message);
  return git(root, 'rev-parse', 'HEAD');
}

export async function withConfluenceFixture(run) {
  const root = mkdtempSync(join(tmpdir(), 'confluence-fixture-'));
  try {
    const divisionRoot = join(root, 'division');
    const policies = join(root, 'policies');
    const operations = join(root, 'operations');

    // --- fixture/policies ---
    init(policies, 'https://github.com/fixture/policies.git');

    // Pages
    put(policies, 'pages/expense-policy.md',
      '---\ntitle: Expense policy\nconfluence_url: https://confluence.example.test/pages/1001\n---\n# Expense policy\nClaims over 500 need a manager\'s approval.\nThe approval process records every claim.\n');
    put(policies, 'pages/approval-process.md',
      '---\ntitle: Approval process\n---\n# Approval process\nManagers approve claims in the finance system.\n');
    put(policies, 'pages/legacy-notes.md',
      '# Legacy notes\nThe old ledger still exports claims nightly.\n');
    put(policies, 'pages/injected.md',
      '---\ntitle: "<script>alert(1)</script> | # Heading"\nconfluence_url: https://confluence.example.test/pages/1004\n---\nLine with <b>markup</b> and | pipes\n# Not a heading\n');

    // Ontology config
    put(policies, 'ontology.config.json',
      JSON.stringify({ sourceGraph: { enabled: true, root: 'graph', evidence: { 'selected-document': ['pages/**'] } } }));

    // Graph
    const w = new Writer();
    const add = (s, p, o) => w.addQuad(quad(namedNode('https://example.test/policies/graph#' + s), namedNode(p), o));
    const iri = x => namedNode('https://ontology-kit.dev/schema#' + x);
    const lit = x => literal(String(x));
    const type = (s, t) => add(s, RDF + 'type', iri(t));

    // Manifest node (required by graph parser)
    type('graph', 'SourceKnowledgeGraph');
    add('graph', K + 'schemaVersion', literal(2));

    // Nodes
    const nodes = [
      ['expense-policy', 'Expense policy', 'policy'],
      ['approval', 'Claim approval', 'process'],
      ['ledger', 'Legacy ledger', 'system'],
      ['injected', 'Injected page', 'policy'],
      ['orphan', 'Expense archive', 'record']
    ];
    for (const [n, label, kind] of nodes) {
      type(n, 'GraphNode');
      add(n, K + 'kind', iri(kind));
      add(n, RDFS + 'label', lit(label));
    }

    // Evidence
    const evidence = [
      ['ev-policy', 'pages/expense-policy.md', 6, 7, 'selected-document'],
      ['ev-approval', 'pages/approval-process.md', 5, 5, 'selected-document'],
      ['ev-legacy', 'pages/legacy-notes.md', 2, 2, 'selected-document'],
      ['ev-injected', 'pages/injected.md', 5, 6, 'selected-document']
    ];
    for (const [eiri, path, sl, el, role] of evidence) {
      type(eiri, 'GraphEvidence');
      add(eiri, K + 'path', lit(path));
      add(eiri, K + 'startLine', lit(sl));
      add(eiri, K + 'endLine', lit(el));
      // Read actual file content to compute correct blobId
      const fullPath = join(policies, path);
      const fileContent = readFileSync(fullPath, 'utf8');
      add(eiri, K + 'blobId', lit(blobId(fileContent)));
      add(eiri, K + 'role', iri('role-' + role));
    }

    // Assertions - each references its own evidence node
    const assertions = [
      ['assert-expense', 'expense-policy', 'approval', 'governs', 'ev-policy'],
      ['assert-approval', 'approval', 'ledger', 'uses', 'ev-approval'],
      ['assert-ledger', 'ledger', 'approval', 'exports', 'ev-legacy'],
      ['assert-injected', 'injected', 'approval', 'governs', 'ev-injected']
    ];
    for (const [name, subj, obj, rel, evIri] of assertions) {
      type(name, 'GraphAssertion');
      add(name, K + 'subject', namedNode('https://example.test/policies/graph#' + subj));
      add(name, K + 'object', namedNode('https://example.test/policies/graph#' + obj));
      add(name, K + 'relationship', namedNode('https://example.test/rel#' + rel));
      add(name, K + 'basis', iri('direct'));
      add(name, K + 'evidence', namedNode('https://example.test/policies/graph#' + evIri));
    }

    // Coverage
    type('coverage', 'GraphCoverage');
    add('coverage', K + 'pathSelector', lit('pages/**'));
    add('coverage', K + 'priority', lit(1));
    add('coverage', K + 'status', iri('covered'));

    let graphResult;
    w.end((e, text) => { if (e) throw e; graphResult = text });
    put(policies, 'graph/manifest.ttl', graphResult);

    const policiesCommit = commit(policies, 'initial');

    // --- fixture/operations ---
    init(operations, 'https://github.com/fixture/operations.git');

    put(operations, 'pages/runbook.md',
      '---\ntitle: Finance runbook\nconfluence_url: https://confluence.example.test/pages/2001\n---\nThe finance team runs the claims job.\n');
    put(operations, 'ontology.config.json',
      JSON.stringify({ sourceGraph: { enabled: true, root: 'graph', evidence: { 'selected-document': ['pages/**'] } } }));

    const w2 = new Writer();
    const add2 = (s, p, o) => w2.addQuad(quad(namedNode('https://example.test/operations/graph#' + s), namedNode(p), o));
    const type2 = (n, t) => add2(n, RDF + 'type', iri(t));

    // Manifest node (required by graph parser)
    type2('graph', 'SourceKnowledgeGraph');
    add2('graph', K + 'schemaVersion', literal(2));

    type2('claims-job', 'GraphNode');
    add2('claims-job', K + 'kind', iri('process'));
    add2('claims-job', RDFS + 'label', lit('Claims job'));

    type2('team', 'GraphNode');
    add2('team', K + 'kind', iri('team'));
    add2('team', RDFS + 'label', lit('Finance team'));

    // Evidence for the assertion (line 5)
    type2('ev-job', 'GraphEvidence');
    add2('ev-job', K + 'path', lit('pages/runbook.md'));
    add2('ev-job', K + 'startLine', lit(5));
    add2('ev-job', K + 'endLine', lit(5));
    const runbookContent = readFileSync(join(operations, 'pages/runbook.md'), 'utf8');
    add2('ev-job', K + 'blobId', lit(blobId(runbookContent)));
    add2('ev-job', K + 'role', iri('role-selected-document'));

    // Assertion: team runs claims-job
    type2('assert-team', 'GraphAssertion');
    add2('assert-team', K + 'subject', namedNode('https://example.test/operations/graph#team'));
    add2('assert-team', K + 'object', namedNode('https://example.test/operations/graph#claims-job'));
    add2('assert-team', K + 'relationship', namedNode('https://example.test/rel#runs'));
    add2('assert-team', K + 'basis', iri('direct'));
    add2('assert-team', K + 'evidence', namedNode('https://example.test/operations/graph#ev-job'));

    // Coverage
    type2('coverage', 'GraphCoverage');
    add2('coverage', K + 'pathSelector', lit('pages/**'));
    add2('coverage', K + 'priority', lit(1));
    add2('coverage', K + 'status', iri('covered'));

    let graphResult2;
    w2.end((e, text) => { if (e) throw e; graphResult2 = text });
    put(operations, 'graph/manifest.ttl', graphResult2);

    const operationsCommit = commit(operations, 'initial');

    // --- Division ---
    init(divisionRoot, 'https://github.com/fixture/division.git');
    put(divisionRoot, 'sources.json', JSON.stringify({
      sources: [
        { id: 'fixture/policies', url: 'https://github.com/fixture/policies.git' },
        { id: 'fixture/operations', url: 'https://github.com/fixture/operations.git' }
      ]
    }));
    put(divisionRoot, 'dist/catalog.json', JSON.stringify({
      schemaVersion: 1,
      sources: [
        { id: 'fixture/policies', commit: policiesCommit },
        { id: 'fixture/operations', commit: operationsCommit }
      ],
      concepts: [],
      relations: [],
      links: []
    }));
    put(divisionRoot, 'c4/mappings.json', JSON.stringify({ schemaVersion: 1, mappings: [] }));
    put(divisionRoot, 'c4/bindings.json', JSON.stringify({ schemaVersion: 1, bindings: [] }));

    const catalogCommit = commit(divisionRoot, 'catalog');

    const keys = {
      policy: { sourceId: 'fixture/policies', nodeIri: 'https://example.test/policies/graph#expense-policy' },
      approval: { sourceId: 'fixture/policies', nodeIri: 'https://example.test/policies/graph#approval' },
      ledger: { sourceId: 'fixture/policies', nodeIri: 'https://example.test/policies/graph#ledger' },
      injected: { sourceId: 'fixture/policies', nodeIri: 'https://example.test/policies/graph#injected' },
      orphan: { sourceId: 'fixture/policies', nodeIri: 'https://example.test/policies/graph#orphan' },
      job: { sourceId: 'fixture/operations', nodeIri: 'https://example.test/operations/graph#claims-job' },
      team: { sourceId: 'fixture/operations', nodeIri: 'https://example.test/operations/graph#team' }
    };

    const common = f => ['--catalog-commit', f.catalogCommit, '--registry-commit', f.registryCommit,
      ...[...f.checkouts].flatMap(([id, path]) => ['--checkout', `${id}=${path}`])];
    const at = key => `${key.sourceId}=${key.nodeIri}`;

    // Pass fixture data to the scenario runner (which is the recorder function)
    await run({
      divisionRoot,
      catalogCommit,
      registryCommit: catalogCommit,
      checkouts: new Map([['fixture/policies', policies], ['fixture/operations', operations]]),
      keys,
      outputDir: join(root, 'output'),
      common,
      at
    });
  } finally {
    rmSync(root, { recursive: true, force: true });
  }
}
