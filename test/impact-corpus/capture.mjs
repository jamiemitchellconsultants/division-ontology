import { mkdtempSync, rmSync, readFileSync, writeFileSync, mkdirSync, copyFileSync, existsSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join, resolve, basename } from 'node:path';
import { fileURLToPath } from 'node:url';
import { pinGitEnvironment } from '../fixtures/deterministic-git.mjs';
import { scenarios } from './scenarios.mjs';
import { runC4Cli } from '../../src/c4/cli.mjs';

const corpus = fileURLToPath(new URL('.', import.meta.url));
const inputFlags = new Set(['--description-file', '--input', '--assessment-file', '--view-input', '--supplement']);
const commitFlags = new Set(['--catalog-commit', '--registry-commit']);
const pad = n => String(n).padStart(3, '0');

export function outputsFor(argv) {
  const level = argv[argv.indexOf('--level') + 1];
  return { search: ['candidates.json'], paths: ['paths.json'], view: [`view-${level}.json`, `view-${level}.md`],
    'assessment-init': ['assessment.json'], 'assessment-render': ['assessment.md'], 'feedback-render': ['feedback.md'] }[argv[0]] ?? [];
}

export async function recordScenario(id, { onStep, copyInputs = true } = {}) {
  pinGitEnvironment();
  const root = mkdtempSync(join(tmpdir(), 'impact-corpus-'));
  const steps = [];
  const run = async (argv, options) => {
    const index = steps.length, step = { argv: [], stderr: '' };
    const output = argv[argv.indexOf('--output') + 1];
    step.argv = argv.map((value, i) => {
      const flag = argv[i - 1];
      if (flag === '--output') return '$output';
      if (flag === '--checkout') return `$checkout:${value.slice(0, value.indexOf('='))}`;
      if (commitFlags.has(flag)) return '$commit';
      if (inputFlags.has(flag)) {
        const target = join(corpus, id, 'inputs', pad(index), basename(value));
        if (copyInputs) { mkdirSync(join(target, '..'), { recursive: true }); copyFileSync(value, target); }
        return `$input/${pad(index)}/${basename(value)}`;
      }
      return value;
    });
    step.exit = await runC4Cli(argv, { ...options, stdout: null, stderr: { write: s => { step.stderr += s; } } });
    step.files = step.exit === 0 ? outputsFor(argv).filter(name => existsSync(join(output, name))).sort() : [];
    step.outputs = Object.fromEntries(step.files.map(name => [name, readFileSync(join(output, name))]));
    if (onStep) await onStep(step, argv, options);
    steps.push(step);
    return step.exit;
  };
  try { await scenarios[id](run, root); } finally { rmSync(root, { recursive: true, force: true }); }
  return steps;
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  for (const id of process.argv.slice(2).length ? process.argv.slice(2) : Object.keys(scenarios)) {
    rmSync(join(corpus, id, 'inputs'), { recursive: true, force: true });
    const steps = await recordScenario(id);
    const dir = join(corpus, id);
    rmSync(join(dir, 'expected'), { recursive: true, force: true });
    steps.forEach((step, index) => {
      const target = join(dir, 'expected', pad(index));
      mkdirSync(target, { recursive: true });
      for (const [name, bytes] of Object.entries(step.outputs)) writeFileSync(join(target, name), bytes);
    });
    const previous = existsSync(join(dir, 'steps.json')) ? JSON.parse(readFileSync(join(dir, 'steps.json'), 'utf8')) : [];
    writeFileSync(join(dir, 'steps.json'), JSON.stringify(steps.map(({ argv, exit, stderr, files }, index) =>
      ({ argv, exit, stderr, files, ...(previous[index]?.manifest ? { manifest: previous[index].manifest } : {}) })), null, 2) + '\n');
    console.log(`${id}: ${steps.length} steps`);
  }
}
