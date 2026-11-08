import { isAbsolute } from 'node:path';
import { buildBundle, writeBundle } from './bundle.mjs';

function parse(argv) {
  const checkouts = new Map();
  const args = { divisionRoot: process.cwd(), checkouts };
  const flags = new Map([['--catalog-commit', 'catalogCommit'], ['--registry-commit', 'registryCommit'],
    ['--output', 'output']]);
  for (let i = 0; i < argv.length; i += 2) {
    const flag = argv[i];
    const value = argv[i + 1];
    if (!value || value.startsWith('--')) throw new Error(`missing value for ${flag}`);
    if (flag === '--checkout') {
      const delimiter = value.indexOf('=');
      const sourceId = value.slice(0, delimiter);
      const directory = value.slice(delimiter + 1);
      if (delimiter < 1 || !isAbsolute(directory) || checkouts.has(sourceId)) {
        throw new Error('--checkout requires a unique owner/repo=/absolute/path');
      }
      checkouts.set(sourceId, directory);
    } else if (flags.has(flag) && !Object.hasOwn(args, flags.get(flag))) {
      args[flags.get(flag)] = value;
    } else {
      throw new Error(`unknown or repeated argument ${flag}`);
    }
  }
  if (!args.catalogCommit || !args.output) throw new Error('--catalog-commit and --output are required');
  return args;
}

try {
  const args = parse(process.argv.slice(2));
  const bundle = buildBundle(args);
  writeBundle(bundle, args.output);
  process.stdout.write(`${bundle.id}\n`);
} catch (error) {
  process.stderr.write(`workspace bundle: ${error.message}\n`);
  process.exitCode = 1;
}
