import { readFileSync } from 'node:fs';
import { join } from 'node:path';

const githubLogin = /^[A-Za-z0-9](?:[A-Za-z0-9-]{0,38})$/;
const keys = new Set(['namespace', 'stewards']);

export function validateDivision(value) {
  if (!value || typeof value !== 'object' || Array.isArray(value)) throw new Error('division.json must be a JSON object');
  for (const key of Object.keys(value)) if (!keys.has(key)) throw new Error(`division.json has unknown key ${key}`);
  const { namespace, stewards } = value;
  let url;
  try { url = new URL(namespace); } catch { throw new Error('division.json namespace must be an absolute https IRI ending in # or /'); }
  if (url.protocol !== 'https:' || !/[#/]$/.test(namespace)) throw new Error('division.json namespace must be an absolute https IRI ending in # or /');
  if (!Array.isArray(stewards) || stewards.length === 0 || !stewards.every((login) => typeof login === 'string' && githubLogin.test(login))) {
    throw new Error('division.json stewards must be a non-empty list of GitHub logins');
  }
  if (new Set(stewards.map((login) => login.toLowerCase())).size !== stewards.length) throw new Error('division.json stewards must be distinct');
  return { namespace, stewards: [...stewards] };
}

export function loadDivision(root) {
  return validateDivision(JSON.parse(readFileSync(join(root, 'division.json'), 'utf8')));
}
