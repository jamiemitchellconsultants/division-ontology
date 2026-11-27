// Exports impact bundles for every scenario's engine steps into test/impact-corpus/objects/.
import { mkdirSync, readFileSync, writeFileSync, existsSync } from 'node:fs';
import { join, resolve, basename } from 'node:path';
import { fileURLToPath } from 'node:url';
import { recordScenario } from './capture.mjs';
import { buildImpactBundle, writeImpactBundle } from '../../src/c4/impact-bundle.mjs';
import { readAtCommit } from '../../src/c4/git.mjs';
import { loadSnapshot } from '../../src/c4/snapshot.mjs';
import { loadRegistries } from '../../src/c4/registry.mjs';
import { scenarios } from './scenarios.mjs';

const corpus = fileURLToPath(new URL('.', import.meta.url));
const engineCommands = new Set(['search', 'paths', 'view', 'assessment-init', 'assessment-render', 'feedback-render']);

function loadFromRealArgs(argv, options) {
  const catalogCommit = argv[argv.indexOf('--catalog-commit') + 1];
  const registryCommit = argv[argv.indexOf('--registry-commit') + 1];
  const checkouts = new Map();
  for (let i = 0; i < argv.length; i++) {
    if (argv[i] === '--checkout' && i + 1 < argv.length) {
      const [id, path] = argv[i + 1].split('=');
      checkouts.set(id, path);
    }
  }
  const snapshot = loadSnapshot({ divisionRoot: options.cwd, catalogCommit, checkouts });
  const registry = loadRegistries({ divisionRoot: options.cwd, registryCommit, snapshot });
  return { snapshot, registry };
}

function collectExtraFiles(argv) {
  const extraFiles = [];
  const supplementIndex = argv.indexOf('--supplement');
  if (supplementIndex !== -1 && supplementIndex + 1 < argv.length) {
    const supplementPath = argv[supplementIndex + 1];
    const supplementData = JSON.parse(readFileSync(supplementPath, 'utf8'));
    if (supplementData.sourceId && supplementData.path) {
      extraFiles.push({ sourceId: supplementData.sourceId, path: supplementData.path });
    }
  }
  return extraFiles;
}

export async function exportBundles() {
  const objectsDir = join(corpus, 'objects');
  const blobsDir = join(objectsDir, 'blobs');
  const manifestsDir = join(objectsDir, 'manifests');
  const allBlobs = new Map();
  const allManifests = new Map();
  let stepCounter = 0;

  for (const id of Object.keys(scenarios)) {
    const steps = await recordScenario(id, {
      onStep: async (step, argv, options) => {
        if (!engineCommands.has(argv[0])) return;
        if (step.exit !== 0) return;
        try {
          const { snapshot, registry } = loadFromRealArgs(argv, options);
          const extraFiles = collectExtraFiles(argv);
          const catalogCommit = argv[argv.indexOf('--catalog-commit') + 1];
          const catalogBytes = readAtCommit(options.cwd, catalogCommit, 'dist/catalog.json');
          const bundle = buildImpactBundle({ snapshot, registry, catalogBytes, extraFiles });
          writeImpactBundle(bundle, join(objectsDir, `temp-${stepCounter++}`));
          for (const file of bundle.manifest.files) {
            const blob = bundle.blobs.get(file.sha256);
            allBlobs.set(file.sha256, blob);
          }
          allManifests.set(bundle.id, bundle.manifestBytes);
          step.manifest = bundle.id;
        } catch (e) {
          console.error(`Failed to build bundle for step ${argv[0]}:`, e.message);
        }
      },
      copyInputs: false
    });
    // Update steps.json with manifest ids
    const stepsFile = join(corpus, id, 'steps.json');
    const stepsData = JSON.parse(readFileSync(stepsFile, 'utf8'));
    for (let i = 0; i < steps.length; i++) {
      if (steps[i].manifest) {
        stepsData[i].manifest = steps[i].manifest;
      }
    }
    writeFileSync(stepsFile, JSON.stringify(stepsData, null, 2) + '\n');
  }

  // Write merged objects
  if (!existsSync(objectsDir)) mkdirSync(objectsDir, { recursive: true });
  if (!existsSync(blobsDir)) mkdirSync(blobsDir, { recursive: true });
  if (!existsSync(manifestsDir)) mkdirSync(manifestsDir, { recursive: true });

  for (const [sha256, bytes] of allBlobs) {
    writeFileSync(join(blobsDir, sha256), bytes);
  }
  for (const [id, bytes] of allManifests) {
    writeFileSync(join(manifestsDir, `${id}.json`), bytes);
  }

  console.log(`Exported ${allBlobs.size} blobs and ${allManifests.size} manifests`);
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
  await exportBundles();
}
