---
date: 2026-11-19
slug: docs-amend-105-design-for-refusal-bodies-and-no-server-address
title: "docs: amend #105 design for refusal bodies and no server address"
summary: "- **The vision allows one behaviour change.** The API's 400, 401 and 403 responses become problem details with fixed codes and titles: `unreadableRequest`, `authenticationRequired` and `roleRequired`."
kind: product
status: accepted
sequence: 2026-11-19T13:05:04.000Z
evidence: "https://github.com/jamiemitchellconsultants/division-ontology/pull/108; merge commit 1968f8d81b476c19ae7d1792a0a3da2e23f65ac9"
---

## Context

While planning #105, the developer ran the workspace API from a scratch copy and found two things the accepted design (#106) got wrong.

- **Server address.** The served spec was to use the request's origin as its server address, and ASP.NET Core already does this from the Host header. In AWS, CloudFront's `AllViewerExceptHostHeader` policy gives the API the API Gateway host. In the home lab, TLS ends at Caddy, so the API sees `http://`. Swagger UI's calls would then be blocked, so readers and contributors could not try the API outside the local stack.
- **Error responses.** B2 described every error as problem details with a `code`. In fact 401, 403 and the binding failure's 400 have empty bodies, and 400 was not listed at all.

Swagger UI sends the signed-in user's token to whatever server the spec names. The API's only trust decision is JWT validation: it checks no Host, Origin or IP, and has no CORS.

## Decision

- **The vision allows one behaviour change.** The API's 400, 401 and 403 responses become problem details with fixed codes and titles: `unreadableRequest`, `authenticationRequired` and `roleRequired`. Every error therefore has one documented shape. No request changes status, and a 401 never says why a token was rejected (B22).
- **No server address in the served spec** ([ADR 0017](https://github.com/jamiemitchellconsultants/division-ontology/blob/feature/workspace-api-openapi-docs/design-amend/docs/adr/0017-serve-the-spec-without-a-server-address.md), amending ADR 0015). The framework's request-derived `servers` entry is removed, so OpenAPI's default `/` makes Swagger UI call the origin that served the docs. A spike in the browser confirmed it. Two alternatives were rejected:
  - Trusting forwarded headers would let a caller name their own host, and put users' tokens at risk if a cache or CORS rule were ever added.
  - A configured public origin is one more value that every deployment and division copy must get right.
- **One status-code hook writes the refusal bodies** ([ADR 0018](https://github.com/jamiemitchellconsultants/division-ontology/blob/feature/workspace-api-openapi-docs/design-amend/docs/adr/0018-give-refusals-fixed-problem-details-bodies.md), amending ADR 0016). A `UseStatusCodePages` hook in `ApiProgram.Configure` gives bodiless 400, 401 and 403 responses their fixed bodies, in both the container host and Lambda, with the codes kept in `ProblemResponses`. Per-event JwtBearer hooks plus `ThrowOnBadRequest` were rejected, because they would need three mechanisms for one rule. Responses given before any operation is chosen are not operation statuses, and the spec states each operation's method, path and media type instead: 404 for an unknown path, 405, 413 and 415.
- **Behaviours follow.** B2 and B3 add 400. B12, B13 and B19 name the codes. B14 and B20 drop the server address. B22 is new, and U15–U17 record the decisions.

## Consequences

- The docs UI works in all three deployments with no routing, Terraform, proxy or new setting, and nothing in a request can change where it sends a token.
- Integrators get one error shape. The Blazor web UI needs no code change, though for 403 and 400 it now shows the API's title instead of "Workspace request failed (403)." (U15).
- The served spec is the committed contract plus `workspaceSignIn` only. Offline readers of the committed file see no server address.
- A missing, invalid or expired token gets the same 401 body; only the per-request `traceId` differs.
- The draft developer plan on `feature/workspace-api-openapi-docs/plan` has sections marked pending these decisions. They are regenerated after this PR merges, before the plan PR opens.

---

AI-Fingerprint: sha256:d5eb6ca5f6fa

🤖 Generated with [Claude Code](https://claude.com/claude-code)
