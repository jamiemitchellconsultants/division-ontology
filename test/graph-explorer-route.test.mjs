import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import test from 'node:test';
import { runInNewContext } from 'node:vm';

test('CloudFront rewrites graph deep links while serving graph assets directly', () => {
  const terraform = readFileSync(new URL('../deploy/workspace/aws/cdn.tf', import.meta.url), 'utf8');
  const script = terraform.match(/code\s*=\s*<<-JS\n([\s\S]+?)^\s*JS$/m)?.[1];
  assert.ok(script);
  const handler = runInNewContext(`${script}\nhandler`);
  const rewrite = uri => handler({ request: { uri } }).uri;
  assert.equal(rewrite('/graph/abc_123'), '/index.html');
  assert.equal(rewrite('/entity/abc_123'), '/index.html');
  assert.equal(rewrite('/graph-diagram.mjs'), '/graph-diagram.mjs');
  assert.equal(rewrite('/vendor/cytoscape/3.35.0/cytoscape.esm.min.mjs'), '/vendor/cytoscape/3.35.0/cytoscape.esm.min.mjs');
});
