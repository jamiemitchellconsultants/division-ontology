import { writeFileSync, readFileSync } from 'node:fs';
import { join } from 'node:path';
import { withPilotFixture } from '../fixtures/c4/fixture-repos.mjs';
import { generateWalkthrough } from '../acceptance/c4-walkthrough.mjs';

const common = f => ['--catalog-commit', f.catalogCommit, '--registry-commit', f.registryCommit,
  ...[...f.checkouts].flatMap(([id, path]) => ['--checkout', `${id}=${path}`])];
const at = key => `${key.sourceId}=${key.nodeIri}`;

export const scenarios = {
  async pilot(run) {
    await withPilotFixture(async f => {
      const c = [...common(f), '--output', f.outputDir], o = { cwd: f.divisionRoot };
      const temp = name => join(f.divisionRoot, '..', name);
      await run(['search', ...c, '--query', 'Alpha API'], o);                                    // 0
      await run(['search', ...c, '--exact-iri', f.keys.alphaSystem.nodeIri], o);                // 1
      await run(['search', ...c, '--query', 'unmatched phrase'], o);                             // 2
      await run(['paths', ...c, '--start', at(f.keys.alphaSystem)], o);                          // 3
      const traced = JSON.parse(readFileSync(join(f.outputDir, 'paths.json'), 'utf8'));
      await run(['paths', ...c, '--start', at(f.keys.alphaSystem), '--max-depth', '1'], o);      // 4
      await run(['paths', ...c, '--start', at(f.keys.betaSystem)], o);                           // 5
      for (const [level, focus] of [['context', f.keys.alphaSystem], ['container', f.keys.alphaSystem],
        ['component', f.keys.alphaContainer], ['code', f.keys.alphaComponent], ['container', f.keys.betaSystem]])
        await run(['view', ...c, '--level', level, '--focus', at(focus)], o);                    // 6-10
      const supplement = temp('supplement.json');
      writeFileSync(supplement, JSON.stringify(f.codeSupplement()));
      await run(['view', ...c, '--level', 'code', '--focus', at(f.keys.alphaComponent), '--supplement', supplement], o); // 11
      const description = temp('description.txt');
      writeFileSync(description, 'Change Alpha API\n');
      await run(['assessment-init', ...c, '--request-id', 'CR-1', '--description-file', description], o);              // 12
      const record = JSON.parse(readFileSync(join(f.outputDir, 'assessment.json'), 'utf8'));
      const path = traced.paths.find(p => p.steps.length === 2);
      Object.assign(record, { architect: 'Architect A', confirmedLeads: [f.keys.alphaSystem],
        rejectedLeads: [{ key: f.keys.externalBeta, reason: 'Out | <b>scope</b>\nnext' }],
        likelyImpacts: [{ key: path.steps[1].object, reason: 'Call path', pathIds: [path.id],
          evidenceIris: [path.steps[0].evidenceIris[0]] }],
        unresolved: [{ code: 'partial-code', reason: 'No graph code coverage' }] });
      const input = temp('assessment-input.json');
      writeFileSync(input, JSON.stringify(record));
      await run(['assessment-render', ...c, '--input', input], o);                               // 13
      const digest = readFileSync(join(f.outputDir, 'assessment.md'), 'utf8').match(/Assessment digest: ([a-f0-9]{64})/)[1];
      const feedback = temp('feedback-input.json');
      writeFileSync(feedback, JSON.stringify({ schemaVersion: 1, assessmentDigest: digest,
        architect: 'Architect A', reviewer: 'Reviewer B', perceivedAccuracy: 'mostly accurate',
        perceivedCompleteness: 'partial', confidence: 'medium', usefulDiscoveries: ['Beta dependency'],
        suspectedMisses: ['data store'], falseLeads: [], examples: ['CR-1 path'], openQuestions: ['Who owns it?'] }));
      await run(['feedback-render', ...c, '--input', feedback, '--assessment-file', input], o);  // 14
    });
  },
  async walkthrough(run, root) {
    await generateWalkthrough(join(root, 'walkthrough'), { run });
  }
};
