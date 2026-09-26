import { appendFileSync, readFileSync, writeFileSync } from 'node:fs';
import { resolve } from 'node:path';
import { pathToFileURL } from 'node:url';
import { verifyRefresh } from './verify-refresh.mjs';

const generated = new Set(['dist/catalog.json', 'dist/catalog.md', 'dist/combined.nq']);

export function evaluateMergePolicy({ changedPaths, classification, reviews, headSha, stewardLogin }) {
  if (!changedPaths.length) return { allowed: false, reason: 'pull request has no changed files' };
  const generatedChange = changedPaths.some((path) => generated.has(path));
  const otherChange = changedPaths.some((path) => !generated.has(path));
  if (generatedChange && otherChange) return { allowed: false, reason: 'generated catalog and authored files must use separate pull requests' };
  if (generatedChange && classification?.mode === 'automatic') return { allowed: true, reason: 'current generated catalog verified with no linked-concept drift' };
  const stewardReviews = reviews.filter((review) => review.user?.login?.toLowerCase() === stewardLogin.toLowerCase()).sort((a, b) => a.id - b.id);
  const latest = stewardReviews.at(-1);
  if (latest?.state === 'APPROVED' && latest.commit_id === headSha) return { allowed: true, reason: 'current head approved by division steward' };
  return { allowed: false, reason: 'current head needs division steward approval' };
}

async function api(path) {
  const response = await fetch(`https://api.github.com${path}`, {
    headers: { Authorization: `Bearer ${process.env.GH_TOKEN}`, Accept: 'application/vnd.github+json', 'X-GitHub-Api-Version': '2022-11-28' },
  });
  if (!response.ok) throw new Error(`GitHub API ${path}: HTTP ${response.status}`);
  return response.json();
}

async function pages(path) {
  const result = [];
  for (let page = 1; ; page++) {
    const items = await api(`${path}?per_page=100&page=${page}`);
    result.push(...items);
    if (items.length < 100) return result;
  }
}

async function currentPr() {
  const repo = process.env.GITHUB_REPOSITORY;
  const number = Number(process.env.PR_NUMBER);
  if (!/^[\w.-]+\/[\w.-]+$/.test(repo ?? '') || !Number.isSafeInteger(number) || number < 1) throw new Error('repository and PR number are required');
  const pr = await api(`/repos/${repo}/pulls/${number}`);
  if (pr.head.sha !== process.env.PR_HEAD_SHA || pr.base.sha !== process.env.PR_BASE_SHA) throw new Error('PR head or base moved; wait for the next policy run');
  return { repo, number };
}

export async function inspectPr(path) {
  const { repo, number } = await currentPr();
  const files = await pages(`/repos/${repo}/pulls/${number}/files`);
  const changedPaths = files.map((file) => file.filename);
  writeFileSync(path, JSON.stringify({ changedPaths }));
  const generatedOnly = changedPaths.length > 0 && changedPaths.every((file) => generated.has(file));
  appendFileSync(process.env.GITHUB_OUTPUT, `generated_only=${generatedOnly}\n`);
}

export async function checkPr(path) {
  const { repo, number } = await currentPr();
  const { changedPaths } = JSON.parse(readFileSync(path, 'utf8'));
  const generatedOnly = changedPaths.length > 0 && changedPaths.every((file) => generated.has(file));
  const classification = generatedOnly
    ? await verifyRefresh({ baseRoot: resolve('.'), candidateDist: resolve('_candidate/dist'), changedPaths })
    : { mode: 'review', reasons: ['authored file change'] };
  const reviews = await pages(`/repos/${repo}/pulls/${number}/reviews`);
  const decision = evaluateMergePolicy({ changedPaths, classification, reviews, headSha: process.env.PR_HEAD_SHA, stewardLogin: 'jamiemitchellconsultants' });
  console.log(decision.reason);
  if (!decision.allowed) process.exitCode = 1;
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  const [command, path] = process.argv.slice(2);
  if (!path) throw new Error('usage: merge-policy.mjs inspect|check output-path');
  if (command === 'inspect') await inspectPr(path);
  else if (command === 'check') await checkPr(path);
  else throw new Error('usage: merge-policy.mjs inspect|check output-path');
}
