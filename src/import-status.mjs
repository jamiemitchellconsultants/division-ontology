export const STATUSES = ['current', 'invalid', 'not configured', 'access removed'];
export const REASON_CODES = ['config-missing', 'config-invalid', 'ontology-unreadable', 'ontology-invalid-turtle',
  'concept-outside-namespace', 'no-concepts', 'namespace-conflict', 'access-removed'];

const MAX_DETAIL = 200;
const secrets = [
  /\bgh[pousr]_\w{16,}/g,
  /\bgithub_pat_\w{16,}/g,
  /x-access-token/gi,
];

export function safeDetail(value) {
  let text = String(value ?? '').replace(/[\u0000-\u001f\u007f]/g, ' ');
  text = text.replace(/\/\/[^/\s:@]+:[^/\s@]+@/g, '//[redacted]@');
  for (const pattern of secrets) text = text.replace(pattern, '[redacted]');
  text = text.trim();
  return text.length > MAX_DETAIL ? `${text.slice(0, MAX_DETAIL - 1)}…` : text;
}

export function reason(code, detail = '') {
  if (!REASON_CODES.includes(code)) throw new Error(`unknown reason code ${code}`);
  return { code, detail: safeDetail(detail) };
}
