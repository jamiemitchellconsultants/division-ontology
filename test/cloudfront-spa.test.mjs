import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync, readdirSync } from 'node:fs';
import { join } from 'node:path';

const pagesDir = 'runtime/RuntimeKnowledge.Web/Pages';

function cloudFrontHandler() {
  const tf = readFileSync('deploy/workspace/aws/cdn.tf', 'utf8');
  const code = tf.match(/resource "aws_cloudfront_function" "spa"[\s\S]*?<<-JS\n([\s\S]*?)\n\s*JS\n/)[1];
  return new Function(`${code}\nreturn handler;`)();
}

function pageRoutes() {
  return readdirSync(pagesDir).filter(f => f.endsWith('.razor'))
    .flatMap(f => [...readFileSync(join(pagesDir, f), 'utf8').matchAll(/^@page "([^"]+)"/gm)].map(m => m[1]));
}

const sample = route => route.replace(/\{[^}:]+:guid\}/g, '0f8fad5b-d9cb-469f-a165-70867728950e').replace(/\{[^}]+\}/g, 'x');

test('every Blazor page route opened directly is served the app shell by CloudFront', () => {
  const handler = cloudFrontHandler();
  const routes = pageRoutes().filter(r => r !== '/');
  assert.ok(routes.includes('/assessments/{Id:guid}') && routes.includes('/impact'));
  for (const route of routes) {
    const uri = sample(route);
    assert.equal(handler({ request: { uri } }).uri, '/index.html', `${route} (${uri})`);
  }
});

test('the CloudFront rewrite leaves static assets and the API alone', () => {
  const handler = cloudFrontHandler();
  for (const uri of ['/index.html', '/_framework/blazor.webassembly.js', '/css/app.css', '/api/impact/search', '/impactful.js'])
    assert.equal(handler({ request: { uri } }).uri, uri);
});
