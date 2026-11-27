import { readFileSync, writeFileSync } from 'node:fs';
import { hostname } from 'node:os';
import { join, resolve } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { runC4Cli } from '../../src/c4/cli.mjs';
import { withPilotFixture } from '../fixtures/c4/fixture-repos.mjs';
import { teardown } from '../fixtures/representative/setup.mjs';
import { acceptanceFixture, accounting, api, bank, git, key, order, shop } from './c4-fixture.mjs';

export const requestId = 'CR-ORDER-01';
export const description = 'Change Order processing to send a new order identifier to Warehouse fulfilment and Accounting.';
export const impactReason = 'Accounting reconciles invoices using the order identifier.';
export const rejectionReason = 'The change request does not alter bank payment instructions.';
export const question = 'Do downstream consumers retain historical order identifiers?';
const read = file => JSON.parse(readFileSync(file, 'utf8'));
const write = (file, data) => writeFileSync(file, JSON.stringify(data, null, 2) + '\n');
const start = focus => focus.sourceId + '=' + focus.nodeIri;

async function c4(fixture, folder, command, args, catalog = fixture.baseline, run = runC4Cli) {
  const output = join(fixture.root, 'reports', folder);
  const status = await run([
    command, ...args, '--catalog-commit', catalog, '--registry-commit', fixture.baseline,
    ...fixture.flags, '--output', output
  ], { cwd: fixture.divisionRoot, stdout: { write() {} } });
  if (status !== 0) throw new Error('C4 command failed for ' + folder);
  return output;
}

export async function generateWalkthrough(root, { run = runC4Cli } = {}) {
  const f = await acceptanceFixture(root);
  try {
    const show = (folder, level, focus, catalog) =>
      c4(f, folder, 'view', ['--level', level, '--focus', start(focus)], catalog, run);
    await c4(f, 'B01-description', 'search', ['--query', description]);
    await c4(f, 'B01-unmatched', 'search', ['--query', 'no-match-qzx-901']);
    await c4(f, 'B01-exact', 'search', ['--exact-iri', order.nodeIri]);
    const pathsDir = await c4(f, 'B02-paths', 'paths', ['--start', start(shop)]);
    const path = read(join(pathsDir, 'paths.json')).paths.find(p =>
      p.steps.at(-1)?.to?.nodeIri === accounting.nodeIri);
    if (!path) throw new Error('Accounting path needed for the sample assessment');

    const request = join(root, 'request.txt');
    writeFileSync(request, description + '\n');
    const assessmentDir = await c4(f, 'B03-assessment', 'assessment-init', [
      '--request-id', requestId, '--description-file', request
    ]);
    const assessmentPath = join(assessmentDir, 'assessment.json');
    const assessment = read(assessmentPath);
    Object.assign(assessment, {
      architect: 'Sample architect',
      confirmedLeads: [shop],
      rejectedLeads: [{ key: bank, reason: rejectionReason }],
      likelyImpacts: [{
        key: accounting, reason: impactReason, pathIds: [path.id],
        evidenceIris: path.steps.flatMap(step => step.evidenceIris ?? [])
      }],
      unresolved: [{ code: 'historical-orders', reason: question }]
    });
    write(assessmentPath, assessment);
    await c4(f, 'B03-assessment', 'assessment-render', ['--input', assessmentPath]);
    const digest = readFileSync(join(assessmentDir, 'assessment.md'), 'utf8')
      .match(/^Assessment digest: ([a-f0-9]{64})$/m)?.[1];
    const feedbackPath = join(root, 'feedback.json');
    write(feedbackPath, {
      schemaVersion: 1, assessmentDigest: digest,
      architect: 'Sample architect', reviewer: 'Sample reviewer',
      perceivedAccuracy: 'The invoicing dependency matches the scenario.',
      perceivedCompleteness: 'Historical order handling is still unknown.',
      confidence: 'Moderate',
      usefulDiscoveries: ['Accounting receives order-derived shipment information.'],
      suspectedMisses: ['Historical-order consumers.'],
      falseLeads: ['External bank.'],
      examples: ['Warehouse sends shipment completion to Accounting.'],
      openQuestions: [question]
    });
    await c4(f, 'B04-feedback', 'feedback-render', [
      '--input', feedbackPath, '--assessment-file', assessmentPath
    ]);

    await show('B05-context', 'context', shop);
    await show('B06-container', 'container', shop);
    await show('B07-component', 'component', api);
    await show('B08-code-gap', 'code', order);
    await withPilotFixture(async alpha => {
      const output = join(root, 'reports', 'B08-code');
      const flags = [
        '--catalog-commit', alpha.catalogCommit, '--registry-commit', alpha.registryCommit,
        ...[...alpha.checkouts].flatMap(([id, path]) => ['--checkout', id + '=' + path]),
        '--output', output, '--level', 'code', '--focus', start(alpha.keys.alphaComponent)
      ];
      const options = { cwd: alpha.divisionRoot, stdout: { write() {} } };
      if (await run(['view', ...flags], options)) throw new Error('Graph-backed code view failed');
      const generated = join(output, 'view-code.json');
      writeFileSync(join(output, 'graph-code.json'), readFileSync(generated));
      const evidence = alpha.codeSupplement();
      const supplement = join(root, 'code-supplement.json');
      write(supplement, evidence);
      if (await run(['view', ...flags, '--supplement', supplement], options)) {
        throw new Error('Inspected code view failed');
      }
      const commit = alpha.sourceCommits.alpha;
      const repo = alpha.checkouts.get(evidence.sourceId);
      write(join(output, 'pinned-code.json'), {
        commit, path: evidence.path, blobId: git(repo, 'rev-parse', commit + ':' + evidence.path),
        content: git(repo, 'show', commit + ':' + evidence.path)
      });
    });

    await c4(f, 'B11-order-labels', 'search', ['--query', 'Order']);
    await show('B12-repeat', 'container', shop);
    await show('B12-newer', 'container', shop, f.newer);
    await c4(f, 'B13-empty-path', 'paths', ['--start', start(key('warehouse', 'component'))]);
    await show('B14-bank', 'context', accounting);
    const inventory = JSON.parse(git(f.divisionRoot, 'show', f.baseline + ':dist/catalog.json'));
    write(join(root, 'reports', 'catalog.json'), inventory);
    write(join(root, 'review.json'), { baseline: f.baseline, newer: f.newer });
    writeReviewIndex(f);
    return f;
  } catch (error) {
    teardown(root);
    throw error;
  }
}

function writeReviewIndex(f) {
  const reports = join(f.root, 'reports');
  const json = file => read(join(reports, file));
  const context = json('B05-context/view-context.json');
  const containers = json('B06-container/view-container.json');
  const components = json('B07-component/view-component.json');
  const code = json('B08-code/view-code.json');
  const missing = json('B08-code-gap/view-code.json');
  const newer = json('B12-newer/view-container.json');
  const inventory = json('catalog.json');
  const orderLabels = json('B11-order-labels/candidates.json').leads.filter(l => l.matchedText.startsWith('Order — '));
  const empty = json('B13-empty-path/paths.json');
  const bankView = json('B14-bank/view-context.json');
  const items = view => view.items.map(i => i.label + (i.insideBoundary ? ' (inside)' : ' (outside)')).join(', ');
  const edges = view => view.edges.map(e => e.from.nodeIri.split('#').at(-1) + ' → ' + e.to.nodeIri.split('#').at(-1) + ' (' + e.basis + ')').join(', ');
  const codes = result => [...new Set(result.gaps.map(g => g.code))].join(', ');
  const path = json('B02-paths/paths.json').paths.find(p => p.steps.at(-1)?.to?.nodeIri === accounting.nodeIri);
  const descriptionResult = json('B01-description/candidates.json');
  const unmatched = json('B01-unmatched/candidates.json');
  const exact = json('B01-exact/candidates.json');
  const sameBytes = extension => readFileSync(join(reports, 'B06-container/view-container.' + extension))
    .equals(readFileSync(join(reports, 'B12-repeat/view-container.' + extension)));
  const codeSource = json('B08-code/pinned-code.json');
  const lines = [
    '# C4 acceptance review',
    '', 'Use 05-acceptance.md for the expected results and step numbers. This index shows observed values.',
    '', '- Machine: ' + hostname() + ' · Node ' + process.version,
    '- Division revision: `' + git(fileURLToPath(new URL('../../', import.meta.url)), 'rev-parse', 'HEAD') + '`',
    '', '- Baseline catalog and registry: `' + f.baseline + '`',
    '- Newer catalog: `' + f.newer + '`',
    '- Ecommerce source: `' + f.oldSource + '` → `' + f.newSource + '`',
    '', '## B1–B4 Search, paths and assessment',
    '', '- Description leads: ' + descriptionResult.leads.filter(l => l.supported).map(l => l.matchedText + ' (' + l.key.sourceId + ')').join(', '),
    '- Unmatched leads: ' + unmatched.leads.length + '. Gaps: ' + codes(unmatched),
    '- Exact leads: ' + exact.leads.map(l => l.key.nodeIri).join(', '),
    '- Path to Accounting: ' + path.steps.map(s => s.type === 'binding'
      ? 'binding ' + s.from.sourceId + ' → ' + s.to.sourceId
      : s.direction + ' assertion ' + s.assertionIri + ' (' + s.evidence.map(e => e.path).join(', ') + ')').join(' | '),
    '- [B3 assessment](B03-assessment/assessment.md)',
    '- [B4 feedback](B04-feedback/feedback.md)',
    '', '## B5–B10 Views and evidence',
    '', '- Context: ' + items(context) + '. Edges: ' + edges(context),
    '- Containers: ' + items(containers) + '. Edges: ' + edges(containers),
    '- Components: ' + items(components) + '. Edges: ' + edges(components),
    '- Code: ' + items(code) + '. Source: `' + codeSource.commit + ':' + codeSource.path + '`',
    '- Missing code: ' + missing.items.length + ' items. Gaps: ' + codes(missing),
    '- Component view gaps: ' + codes(components),
    '- [Component evidence and diagram](B07-component/view-component.md)',
    '- [Code evidence and diagram](B08-code/view-code.md)',
    '', '## B11–B15 Identity, revision and inventory',
    '', ...orderLabels.map(l => '- Order: ' + l.key.sourceId + ' · `' + l.key.nodeIri + '` · `' + l.sourceCommit + '`'),
    '- Baseline and repeat identical: JSON ' + sameBytes('json') + ', Markdown ' + sameBytes('md'),
    '- Newer containers: ' + items(newer),
    '- Empty path: ' + empty.paths.length + ' paths. Gaps: ' + codes(empty),
    '- Accounting context: ' + items(bankView),
    '- Catalog sources: ' + inventory.sources.map(s => s.id + ' (' + s.conceptCount + ' concepts)').join(', '),
    ''
  ];
  writeFileSync(join(reports, 'README.md'), lines.join('\n'));
}

async function renderReviewFeedback(root) {
  const fixture = read(join(root, 'fixture.json'));
  const { baseline } = read(join(root, 'review.json'));
  const flags = Object.entries(fixture.checkouts).flatMap(([id, path]) => ['--checkout', id + '=' + path]);
  await c4({ root, divisionRoot: fixture.divisionRoot, baseline, flags }, 'B04-feedback', 'feedback-render', [
    '--input', join(root, 'feedback.json'),
    '--assessment-file', join(root, 'reports/B03-assessment/assessment.json')
  ]);
  console.log(join(root, 'reports/B04-feedback/feedback.md'));
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  const [action, directory] = process.argv.slice(2);
  if (!directory || !['prepare', 'feedback', 'teardown'].includes(action)) {
    throw new Error('Usage: node test/acceptance/c4-walkthrough.mjs prepare|feedback|teardown /absolute/path');
  }
  const root = resolve(directory);
  if (action === 'prepare') {
    await generateWalkthrough(root);
    console.log('Open ' + join(root, 'reports/README.md'));
  } else if (action === 'feedback') {
    await renderReviewFeedback(root);
  } else {
    teardown(root);
  }
}
