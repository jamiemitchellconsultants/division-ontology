import { reason } from './import-status.mjs';
import { safeRelativePath } from './sources.mjs';

export const IMPORT_CONFIG_PATH = '.division-ontology.json';
export const IMPORT_CONFIG_MAX_BYTES = 16384;
const keys = new Set(['schemaVersion', 'ontologyPath', 'namespace']);

const invalid = (detail) => ({ ok: false, reason: reason('config-invalid', detail) });

export function parseImportConfiguration(text) {
  if (Buffer.byteLength(text) > IMPORT_CONFIG_MAX_BYTES) return invalid('file is larger than 16 KiB');
  let value;
  try { value = JSON.parse(text); } catch { return invalid('file is not valid JSON'); }
  if (!value || typeof value !== 'object' || Array.isArray(value)) return invalid('file must be a JSON object');
  for (const key of Object.keys(value)) if (!keys.has(key)) return invalid(`unknown key ${key}`);
  if (value.schemaVersion !== 1) return invalid('schemaVersion must be 1');
  if (!safeRelativePath(value.ontologyPath)) return invalid('ontologyPath must stay inside the repository');
  if (typeof value.namespace !== 'string' || !/^https?:\/\//.test(value.namespace)) return invalid('namespace must be an HTTP IRI');
  return { ok: true, config: { ontologyPath: value.ontologyPath, namespace: value.namespace } };
}
