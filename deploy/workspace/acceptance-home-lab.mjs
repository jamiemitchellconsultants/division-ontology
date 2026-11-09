import { execFile } from 'node:child_process';
import { lookup } from 'node:dns/promises';
import { mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, join, resolve } from 'node:path';
import { setTimeout as delay } from 'node:timers/promises';
import { fileURLToPath } from 'node:url';
import { parseArgs, promisify } from 'node:util';
import { describeFixture, verifyReplay } from './verify-replay.mjs';

const exec = promisify(execFile);
const digest = /^[a-f0-9]{64}$/;
const imageServices = { api: 'API', indexer: 'WORKER', web: 'WEB', db: 'DB', minio: 'MINIO' };
const credentialKeys = ['MANAGER_USERNAME', 'MANAGER_PASSWORD', 'READER_USERNAME', 'READER_PASSWORD']
  .map(key => 'WORKSPACE_VERIFY_' + key);
const usage = 'Usage: node deploy/workspace/acceptance-home-lab.mjs --deployment FILE --snapshot DIGEST --evidence NEW_DIRECTORY --operator NAME [--project NAME] [--compose FILE] [--env-file FILE]';

function requireCondition(condition, message) {
  if (!condition) throw new Error(message);
}

function readDeployment(path, snapshot) {
  let deployment;
  try {
    deployment = JSON.parse(readFileSync(path, 'utf8').replace(/^\uFEFF/, ''));
  } catch {
    throw new Error('Read a valid deployment-evidence.json file from the fixture installer.');
  }
  requireCondition(deployment.activeManifest === snapshot, 'The installer evidence must name the expected synthetic snapshot.');
  requireCondition(/^[a-f0-9]{40}$/.test(deployment.commit), 'The installer evidence needs the deployed commit.');
  requireCondition(typeof deployment.publicHost === 'string' && /^[a-z0-9.-]+$/i.test(deployment.publicHost),
    'The installer evidence needs a valid HTTPS hostname.');
  for (const key of Object.values(imageServices)) {
    requireCondition(/@sha256:[a-f0-9]{64}$/.test(deployment.images?.[key]), `Missing pinned image for ${key}.`);
  }
  return deployment;
}

function parseServices(output) {
  const text = output.trim();
  if (!text) return [];
  return text.startsWith('[') ? JSON.parse(text) : text.split(/\r?\n/).map(line => JSON.parse(line));
}

async function main() {
  const { values: options } = parseArgs({ options: {
    deployment: { type: 'string' }, snapshot: { type: 'string' }, evidence: { type: 'string' },
    operator: { type: 'string' }, project: { type: 'string', default: 'runtime-knowledge-workspace' },
    compose: { type: 'string' }, 'env-file': { type: 'string' }, help: { type: 'boolean' }
  } });
  if (options.help) {
    console.log(usage);
    return;
  }
  const snapshot = options.snapshot;
  requireCondition(options.deployment && options.evidence && options.operator?.trim() && digest.test(snapshot), usage);
  const deployment = readDeployment(options.deployment, snapshot);
  const missing = credentialKeys.filter(key => !process.env[key]?.trim());
  requireCondition(missing.length === 0, `Set the fixture credentials in the process environment. Missing ${missing.join(', ')}.`);
  const install = dirname(resolve(options.deployment));
  const compose = ['compose', '--project-name', options.project,
    '-f', resolve(options.compose ?? join(install, 'src', 'deploy', 'workspace', 'home-lab', 'compose.yml')),
    '--env-file', resolve(options['env-file'] ?? join(install, 'workspace.env')), '--profile', 'operator'];
  const evidence = resolve(options.evidence);
  mkdirSync(evidence, { mode: 0o700 });
  const save = (name, data) => writeFileSync(join(evidence, name), data, { mode: 0o600 });
  const origin = `https://${deployment.publicHost}`;
  const report = { started: new Date().toISOString(), operator: options.operator, project: options.project,
    origin, applicationRevision: deployment.commit, images: deployment.images,
    embeddingModel: deployment.embeddingModel, embeddingRevision: deployment.embeddingRevision,
    snapshot, steps: [], humanReview: ['B14 independent reviewer'], status: 'running' };
  const saveReport = () => save('run.json', JSON.stringify(report, null, 2) + '\n');
  const secrets = new Set(credentialKeys.filter(key => key.endsWith('_PASSWORD')).map(key => process.env[key]));
  const redact = text => [...secrets].sort((a, b) => b.length - a.length)
    .reduce((safe, value) => safe.replaceAll(value, '[redacted]'),
      text.replace(/Bearer [A-Za-z0-9._~-]+/gi, 'Bearer [redacted]'));
  save('snapshot', snapshot + '\n');
  saveReport();
  async function run(command, args, { env = process.env, log, showBrowserError = false, timeout = 300_000 } = {}) {
    try {
      const { stdout, stderr } = await exec(command, args, { env, timeout, maxBuffer: 4 * 1024 * 1024, windowsHide: true });
      if (log) save(log, redact(stdout + stderr));
      return stdout;
    } catch (error) {
      const output = redact((error.stdout ?? '') + (error.stderr ?? ''));
      if (log) save(log, output);
      const browserError = showBrowserError && output.split(/\r?\n/).find(line =>
        /^(?:\w*Error(?: \[[\w_]+\])?|\w+\.\w+): .+/.test(line));
      const message = browserError
        ? browserError.replace(/(https?:\/\/[^\s?#]+)[?#][^\s]*/g, '$1').slice(0, 360).replace(/[.\s]+$/, '')
        : `${command} failed (exit ${error.code ?? error.signal ?? 'unknown'})`;
      throw new Error(log ? `${message}. See ${log}.` : `${message}.`);
    }
  }
  const docker = (...args) => run('docker', [...compose, ...args]);
  const replay = (...args) => run('docker', [...compose, 'run', '--rm', '--no-deps', '-T', 'replay', ...args],
    { log: `replay-${args.join('-')}.log` });
  const env = { ...process.env, WORKSPACE_VERIFY_ORIGIN: origin, WORKSPACE_SCENARIO_DIR: evidence,
    WORKSPACE_VERIFY_APP_VERSION: deployment.commit, WORKSPACE_VERIFY_MODEL_REVISION: deployment.embeddingRevision || 'configured provider' };
  const browser = (log, ...flags) => run(process.execPath,
    [fileURLToPath(new URL('verify-browser.mjs', import.meta.url)), 'home-lab', ...flags], { env, log, showBrowserError: true });

  async function step(name, check) {
    try {
      await check();
      report.steps.push({ name, status: 'pass' });
    } catch (error) {
      report.steps.push({ name, status: 'fail', message: redact(error.message) });
      throw new Error(`${name}: ${redact(error.message)}`);
    } finally {
      saveReport();
    }
  }

  async function waitHealthy() {
    for (let attempt = 0; attempt < 40; attempt++) {
      const services = parseServices(await docker('ps', '--all', '--format', 'json'));
      const current = Object.keys(imageServices).map(service => services.find(row => row.Service === service));
      if (current.every(row => row?.State === 'running' && row.Health === 'healthy')) return current;
      await delay(3000);
    }
    throw new Error('The fixture API, web, database, indexer and MinIO did not all become healthy.');
  }

  try {
    await step('fixture preflight', async () => {
      // The resolved Compose configuration contains credentials. Keep it in memory.
      const resolved = await docker('config', '--format', 'json');
      let config;
      try {
        config = JSON.parse(resolved);
      } catch {
        throw new Error('Docker Compose did not return a valid configuration.');
      }
      for (const service of Object.values(config.services)) {
        for (const [key, value] of Object.entries(service.environment ?? {})) {
          if (typeof value === 'string' && value && /password|secret|connectionstring|accesskey/i.test(key)) {
            secrets.add(value);
            const password = /(?:^|;)Password=([^;]+)/i.exec(value)?.[1];
            if (password) secrets.add(password);
          }
        }
      }
      for (const [service, key] of Object.entries({ ...imageServices, replay: 'WORKER' })) {
        requireCondition(config.services[service]?.image === deployment.images[key], `Compose image mismatch for ${service}.`);
      }
      const api = config.services.api.environment;
      requireCondition(config.services.web.environment.WORKSPACE_PUBLIC_HOST === deployment.publicHost &&
        api.Workspace__Identity__Issuer === deployment.issuer, 'Compose host or identity configuration differs from installer evidence.');
      for (const row of await waitHealthy()) {
        const actual = (await run('docker', ['inspect', '--format', '{{.Config.Image}}', row.ID])).trim();
        requireCondition(actual === deployment.images[imageServices[row.Service]], `Running image mismatch for ${row.Service}.`);
      }
      await describeFixture(replay, snapshot);
    });
    await step('browser DNS', async () => {
      for (const host of new Set([new URL(origin).hostname, new URL(deployment.issuer).hostname])) {
        try {
          await lookup(host);
        } catch (error) {
          throw new Error(`Cannot resolve ${host} from the runner (${error.code ?? 'DNS lookup failed'}). Check DNS on the browser host.`);
        }
      }
    });
    await step('initial browser', () => browser('browser-initial.log'));
    await step('restart and health', async () => {
      await describeFixture(replay, snapshot);
      await run('docker', [...compose, 'restart', '--no-deps', 'db', 'api'], { log: 'restart.log' });
      await waitHealthy();
      await describeFixture(replay, snapshot);
    });
    await step('persistence after restart', () => browser('browser-restarted.log', '--check-persistence'));
    await step('guarded replay', () => verifyReplay(replay, snapshot,
      (name, state) => save(`projection-${name}.json`, JSON.stringify(state, null, 2) + '\n')));
    await step('persistence after replay', () => browser('browser-replayed.log', '--check-persistence'));
    report.status = 'passed scripted checks';
    console.error('REVIEW B14. Record the independent reviewer’s explanation.');
  } catch (error) {
    report.status = 'failed';
    throw error;
  } finally {
    report.finished = new Date().toISOString();
    saveReport();
    console.log(`Evidence: ${evidence}`);
  }
}

try {
  await main();
} catch (error) {
  console.error(`Home-lab acceptance: ${error.message}`);
  process.exitCode = 1;
}
