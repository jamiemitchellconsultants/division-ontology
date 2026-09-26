import { createHash } from 'node:crypto';
import { pathToFileURL } from 'node:url';

const marker = /^<!-- division-policy-reviews:[0-9a-f]{12} -->\n?/m;

export function updateReviewMarker(body, reviews) {
  if (!reviews.length && !marker.test(body)) return body;
  const state = reviews.map((review) => [review.id, review.state, review.commit_id, review.submitted_at]).sort((a, b) => a[0] - b[0]);
  const digest = createHash('sha256').update(JSON.stringify(state)).digest('hex').slice(0, 12);
  const tag = `<!-- division-policy-reviews:${digest} -->\n`;
  if (body.includes(tag)) return body;
  const withoutOld = body.replace(marker, '');
  const fingerprint = /^AI-Fingerprint: sha256:[0-9a-f]{12}$/m;
  if (fingerprint.test(withoutOld)) return withoutOld.replace(fingerprint, `${tag}$&`);
  return `${withoutOld.trimEnd()}\n\n${tag}`;
}

async function api(path, options = {}) {
  const response = await fetch(`https://api.github.com${path}`, {
    ...options,
    headers: { Authorization: `Bearer ${process.env.GH_TOKEN}`, Accept: 'application/vnd.github+json', 'X-GitHub-Api-Version': '2022-11-28', ...(options.headers ?? {}) },
  });
  if (!response.ok) throw new Error(`GitHub API ${path}: HTTP ${response.status}`);
  return response.json();
}

async function pages(path) {
  const result = [];
  for (let page = 1; ; page++) {
    const items = await api(`${path}${path.includes('?') ? '&' : '?'}per_page=100&page=${page}`);
    result.push(...items);
    if (items.length < 100) return result;
  }
}

export async function recheckReviews(repository) {
  if (!/^[\w.-]+\/[\w.-]+$/.test(repository ?? '')) throw new Error('GITHUB_REPOSITORY must be owner/name');
  const prs = await pages(`/repos/${repository}/pulls?state=open&base=main`);
  let changed = 0;
  for (const pr of prs) {
    const reviews = await pages(`/repos/${repository}/pulls/${pr.number}/reviews`);
    const currentBody = pr.body ?? '';
    const nextBody = updateReviewMarker(currentBody, reviews);
    if (nextBody === currentBody) continue;
    await api(`/repos/${repository}/pulls/${pr.number}`, { method: 'PATCH', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ body: nextBody }) });
    changed++;
  }
  console.log(`rechecked ${prs.length} open PR(s); updated ${changed} review marker(s)`);
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) await recheckReviews(process.env.GITHUB_REPOSITORY);
