import { InfrastructureError } from './errors.mjs';
import { IMPORT_CONFIG_PATH } from './import-config.mjs';

const byCodepoint = (a, b) => a < b ? -1 : a > b ? 1 : 0;
const idPattern = /^[\w.-]+\/[\w.-]+$/;
const CONCURRENCY = 8;

async function mapLimited(items, limit, task) {
  const results = new Array(items.length);
  let next = 0;
  await Promise.all(Array.from({ length: Math.min(limit, items.length) }, async () => {
    while (next < items.length) { const index = next++; results[index] = await task(items[index]); }
  }));
  return results;
}

export async function discoverGrants(provider, { registeredIds, excludeRepository }) {
  const listing = await provider.listInstallation();
  if (!['all', 'selected'].includes(listing?.selection)) throw new InfrastructureError('installation listing has an unknown repository selection');
  const seen = new Set();
  for (const repository of listing.repositories) {
    if (typeof repository.id !== 'string' || !idPattern.test(repository.id)) throw new InfrastructureError('installation listing has a malformed repository name');
    if (typeof repository.defaultBranch !== 'string' || !repository.defaultBranch) throw new InfrastructureError(`${repository.id}: installation listing has no default branch`);
    if (seen.has(repository.id.toLowerCase())) throw new InfrastructureError(`installation listing has a duplicate repository ${repository.id}`);
    seen.add(repository.id.toLowerCase());
  }
  const registered = new Set([...registeredIds].map((id) => id.toLowerCase()));
  const division = excludeRepository?.toLowerCase();
  const candidates = listing.repositories
    .filter((repository) => !registered.has(repository.id.toLowerCase()) && repository.id.toLowerCase() !== division)
    .sort((a, b) => byCodepoint(a.id, b.id));
  const flags = await mapLimited(candidates, CONCURRENCY, (repository) => provider.hasImportConfig(repository.id, repository.defaultBranch));
  return { selection: listing.selection, repositories: candidates.map(({ id, defaultBranch }, index) => ({ id, defaultBranch, hasConfig: flags[index] === true })) };
}

export function githubGrantProvider({ token, fetchImpl = globalThis.fetch, apiBase = 'https://api.github.com' }) {
  if (!token) throw new Error('a source-read token is required');
  const headers = { Authorization: `Bearer ${token}`, Accept: 'application/vnd.github+json', 'X-GitHub-Api-Version': '2022-11-28' };
  async function request(path) {
    let response;
    try { response = await fetchImpl(`${apiBase}${path}`, { headers }); }
    catch { throw new InfrastructureError(`GitHub API ${path}: request failed`); }
    return response;
  }
  const failed = (path, response) => new InfrastructureError(`GitHub API ${path}: HTTP ${response.status}`);
  return {
    async listInstallation() {
      const repositories = [];
      let selection;
      let total = Infinity;
      for (let page = 1; repositories.length < total; page++) {
        const path = `/installation/repositories?per_page=100&page=${page}`;
        const response = await request(path);
        if (!response.ok) throw failed(path, response);
        const body = await response.json();
        if (page === 1) { selection = body.repository_selection; total = body.total_count; }
        if (!Array.isArray(body.repositories) || (body.repositories.length === 0 && repositories.length < total)) throw new InfrastructureError('installation listing is incomplete');
        repositories.push(...body.repositories.map((item) => ({ id: item.full_name, defaultBranch: item.default_branch })));
      }
      if (repositories.length !== total) throw new InfrastructureError('installation listing is incomplete');
      return { selection, repositories };
    },
    async hasImportConfig(id, defaultBranch) {
      const path = `/repos/${id}/contents/${IMPORT_CONFIG_PATH}?ref=${encodeURIComponent(defaultBranch)}`;
      const response = await request(path);
      if (response.status === 200) return true;
      if (response.status === 404) return false;
      throw failed(path, response);
    },
  };
}
