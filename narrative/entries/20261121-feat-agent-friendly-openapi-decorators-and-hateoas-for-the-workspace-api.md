---
date: 2026-11-21
slug: feat-agent-friendly-openapi-decorators-and-hateoas-for-the-workspace-api
title: "feat: agent-friendly OpenAPI decorators and HATEOAS for the workspace API"
summary: "Decorate every operation in the generator with a stable `operationId`, `x-agent-tool-name`, `x-agent-idempotent`, `x-agent-retryable`, `x-agent-side-effects` and input/output schema names; publish HATEOAS as OpenAPI `links` on every…"
kind: product
status: accepted
sequence: 2026-11-21T05:49:23.000Z
evidence: "https://github.com/jamiemitchellconsultants/division-ontology/pull/117; merge commit 27fc100af622bf8ac36c6f9bc1be44639348baeb"
---

## Context

The workspace API's generated OpenAPI spec described routes, roles (`x-workspace-role`) and error codes (`x-workspace-codes`), but gave an agent or tool-calling client no stable tool names, no indication of which operations are safe to retry, and no discoverable path from one operation to the next. Clients had to construct follow-on URLs themselves and guess at idempotency — the POST-shaped read queries (search, traverse, entity/relationship detail) are particularly ambiguous. The HTTP `QUERY` method (RFC 10008) was evaluated as a more semantic alternative for those reads.

## Decision

Decorate every operation in the generator with a stable `operationId`, `x-agent-tool-name`, `x-agent-idempotent`, `x-agent-retryable`, `x-agent-side-effects` and input/output schema names; publish HATEOAS as OpenAPI `links` on every success response and as RFC 8288 `Link` headers at runtime, so the spec and the live service tell the same story. The completeness checker now enforces these decorators, so a future undecorated operation fails CI. The `QUERY` verb was rejected for the read queries: although .NET 10 supports it end-to-end (verified locally), API Gateway offers no native `QUERY` method, its `ANY` route is not a reliable pass-through, and this deployment's CloudFront `allowed_methods` list would reject `QUERY` before the gateway.

## Consequences

Agents can enumerate the API as 13 stable tools, decide retries from the declared flags, and follow links instead of constructing URLs; the Blazor UI is unaffected because routes, bodies and status codes did not change. The trade-off is a larger spec and a generator table that must be kept in step with new routes — mitigated by the completeness check failing undecorated operations. Deliberately open: adopting `QUERY` if API Gateway and CloudFront add native support, and any client (including the UI) choosing to consume `Link` headers instead of constructing URLs.

Re-review fixes (third commit): the `removeContribution` description now separates 409 `versionConflict` (inspect `current.removed` and review the current record, which may be another caller's edit to a still-active contribution) from 404 `contributionNotFound` (do not retry); workspace link relations moved from the unregistered URN namespace to absolute HTTPS identifiers under the project-controlled `https://github.com/jamiemitchellconsultants/division-ontology#link-relations/` namespace across runtime headers, `x-agent-discovery.linkRelations`, and the tests (the deployment-neutrality test now allows that one project namespace).

---

AI-Fingerprint: sha256:340cc23a776b
