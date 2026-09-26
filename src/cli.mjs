#!/usr/bin/env node
import { readFileSync } from 'node:fs';
import { join, resolve } from 'node:path';
import { pathToFileURL } from 'node:url';
import { buildCatalog, loadLinks } from './catalog.mjs';
import { parseTurtle, readSourceOntology } from './rdf.mjs';
import { loadSources, resolveSource } from './sources.mjs';
import { checkOutputs, renderOutputs, writeOutputs } from './output.mjs';

const CORE_NAMESPACE = 'https://github.com/jamiemitchellconsultants/division-ontology#';

function argumentsFor(argv) {
  const [command, ...args] = argv;
  if (!['build', 'check'].includes(command)) throw new Error('usage: node src/cli.mjs build|check [--checkout owner/name=/absolute/path]...');
  const overrides = new Map();
  for (let i = 0; i < args.length; i++) {
    if (args[i] !== '--checkout' || i + 1 >= args.length) throw new Error(`unexpected argument ${args[i]}`);
    const entry = args[++i];
    const at = entry.indexOf('=');
    if (at < 1 || at === entry.length - 1) throw new Error('--checkout requires owner/name=/absolute/path');
    const id = entry.slice(0, at);
    if (overrides.has(id)) throw new Error(`duplicate --checkout for ${id}`);
    overrides.set(id, entry.slice(at + 1));
  }
  return { command, overrides };
}

export async function runCli(argv, options = {}) {
  const root = resolve(options.root ?? process.cwd());
  const { command, overrides } = argumentsFor(argv);
  const configuredSources = loadSources(join(root, 'sources.json'));
  for (const id of overrides.keys()) if (!configuredSources.some((source) => source.id === id)) throw new Error(`checkout override ${id} is not in sources.json`);
  const coreQuads = parseTurtle(readFileSync(join(root, 'core/ontology.ttl'), 'utf8'), CORE_NAMESPACE);
  const sources = [];
  const links = [];
  for (const source of configuredSources) {
    const resolved = resolveSource(source, overrides);
    try {
      sources.push({ id: source.id, namespace: source.namespace, commit: resolved.commit, quads: readSourceOntology(source, resolved.directory) });
      if (source.linksPath) {
        const sourceLinks = loadLinks(join(root, source.linksPath));
        if (sourceLinks.some((link) => link.source !== source.id)) throw new Error(`${source.linksPath}: source does not match ${source.id}`);
        links.push(...sourceLinks);
      }
    } finally { resolved.cleanup(); }
  }
  const { catalog, quads } = buildCatalog({ coreQuads, sources, links, coreNamespace: CORE_NAMESPACE });
  const outputs = await renderOutputs(catalog, quads);
  if (command === 'check') checkOutputs(root, outputs);
  else writeOutputs(root, outputs);
  return { concepts: catalog.concepts.length, sources: catalog.sources.length, rules: catalog.rules.length, links: catalog.links.length };
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  try {
    const result = await runCli(process.argv.slice(2));
    console.log(`division ontology: ${result.concepts} concepts, ${result.rules} rules, ${result.links} links from ${result.sources} source(s)`);
  } catch (error) {
    console.error(`division ontology: ${error.message}`);
    process.exitCode = 1;
  }
}
