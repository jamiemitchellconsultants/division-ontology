import { resolve } from 'node:path';
import { setup } from '../../test/fixtures/representative/setup.mjs';
import { buildBundle, writeBundle } from './bundle.mjs';

const [directory, output] = process.argv.slice(2);
if (!directory || !output) throw new Error('Provide the synthetic checkout and bundle output paths.');
const { divisionRoot, commit, checkouts } = await setup(resolve(directory));
const bundle = buildBundle({ divisionRoot, catalogCommit: commit, registryCommit: commit, checkouts });
writeBundle(bundle, resolve(output));
process.stdout.write(bundle.id + '\n');
