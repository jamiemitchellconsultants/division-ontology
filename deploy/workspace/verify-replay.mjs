import { execFile } from 'node:child_process';
import { pathToFileURL } from 'node:url';
import { promisify } from 'node:util';

const exec = promisify(execFile);
const digest = /^[a-f0-9]{64}$/;
const generation = /^[a-f0-9]{8}(?:-[a-f0-9]{4}){3}-[a-f0-9]{12}$/;

export async function describeFixture(run, snapshot) {
  if (!digest.test(snapshot)) throw new Error('Supply the expected fixture snapshot digest.');
  const report = JSON.parse(await run('describe-current'));
  if (report.snapshotId !== snapshot) throw new Error('The active fixture snapshot does not match the expected digest.');
  if (!generation.test(report.generation) || !digest.test(report.stateDigest) ||
      !Number.isSafeInteger(report.throughPosition) || report.throughPosition < 1) {
    throw new Error('The worker returned an invalid projection report.');
  }
  return report;
}

function sameState(a, b) {
  return a.snapshotId === b.snapshotId && a.stateDigest === b.stateDigest && a.throughPosition === b.throughPosition;
}

export async function verifyReplay(run, snapshot, record = () => {}) {
  await describeFixture(run, snapshot);
  let failure;
  try {
    await run('writes', 'pause');
    const live = await describeFixture(run, snapshot);
    record('live', live);
    const position = String(live.throughPosition);
    const rebuilt = JSON.parse(await run('replay', '--through', position));
    record('rebuilt', rebuilt);
    if (!generation.test(rebuilt.generation) || live.generation === rebuilt.generation || !sameState(live, rebuilt)) {
      throw new Error('Replay comparison failed. The rebuilt projection was not activated.');
    }
    await run('activate-replay', '--generation', rebuilt.generation, '--expect-position', position);
    const active = await describeFixture(run, snapshot);
    record('active', active);
    if (active.generation !== rebuilt.generation || !sameState(active, rebuilt)) {
      throw new Error('The active projection does not match the replay cutover report.');
    }
    return { live, rebuilt, active };
  } catch (error) {
    failure = error;
    throw error;
  } finally {
    try {
      await run('writes', 'resume');
    } catch (error) {
      throw new AggregateError([failure, error].filter(Boolean),
        `Failed to confirm writes resumed. Run the fixture replay worker with writes resume.${failure ? ` Earlier failure: ${failure.message}` : ''}`);
    }
  }
}

if (process.argv[1] && pathToFileURL(process.argv[1]).href === import.meta.url) {
  const [snapshot, ...compose] = process.argv.slice(2);
  try {
    if (compose[0] !== 'compose') throw new Error('Supply a snapshot digest followed by Docker Compose arguments.');
    const run = async (...args) => (await exec('docker', [...compose, 'run', '--rm', '--no-deps', '-T', 'replay', ...args],
      { timeout: 300_000 })).stdout;
    console.log(JSON.stringify(await verifyReplay(run, snapshot)));
  } catch (error) {
    console.error(error.message);
    process.exitCode = 1;
  }
}
