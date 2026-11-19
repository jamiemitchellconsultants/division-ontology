# 0017 — Serve the spec without a server address

- **Status:** accepted
- **Date:** 2026-11-19
- **Feature:** #105 (docs/features/workspace-api-openapi-docs/)
- **Amends:** [ADR 0015](0015-docs-ui-sign-in-reuses-web-client.md), which said the request-time transformer adds the request origin as the server address

## Context

The docs UI must call the API at the origin the browser is using, in the local, home-lab and AWS deployments (B10–B12, B15). ADR 0015 had the request-time transformer add the request's origin as the server address. `Microsoft.AspNetCore.OpenApi` already does that by itself, from the Host header and the scheme, whenever it serves a document for a request. In two deployments that origin is wrong:

- **AWS:** CloudFront's `AllViewerExceptHostHeader` policy gives the API the API Gateway host.
- **Home lab:** TLS ends at Caddy, so the API sees `http://`.

The docs UI's calls would then go cross-origin with no CORS, or be blocked as mixed content. The developer found this while planning (pushback on #105), and a run of the API confirmed it.

Swagger UI sends the signed-in user's bearer token to whatever server the spec names, so the source of that address matters for security as well as for routing. The API's only trust decision is JWT validation. It checks no Host, Origin, IP or cookie, and it has no CORS.

## Decision

The served spec has no `servers` entry. A document transformer removes the one the framework derives from the request, and the build-time document never has one. OpenAPI 3.1 then defaults the server to `/`, which clients resolve against the address they loaded the spec from. In a browser, that is the origin serving `/api/docs`.

## Consequences

- The docs UI calls the right origin in every deployment, with no routing, Terraform, proxy or configuration change. A spike confirmed it: Swagger UI's request URL was the page's own origin.
- Nothing in a request can change where Swagger UI sends a token. Tokens can only go back to the origin that served the docs, which is also the web UI's origin.
- The served spec is the committed contract plus `workspaceSignIn`, and nothing else. B14 and B20 need rewording to drop "server address", and that goes back to the analyst.
- The served-spec check strips only `workspaceSignIn`.
- An integrator who reads the spec file outside a deployment sees no address. The deployment's documented URL supplies it.

## Alternatives considered

- **Trust forwarded headers.** This means a CloudFront function forwarding the viewer host in AWS, and Caddy's headers in the home lab. The served address would then come from request headers. Lambda cannot limit trust to a known proxy network, and execute-api can be reached directly. Anyone could get a spec that names their own host, so a future cache or CORS change could send users' tokens to an attacker. It also makes `RemoteIpAddress` and the scheme spoofable across the API, and it changes Terraform and the proxy contract.
- **A configured public origin** (`Workspace:Docs:PublicOrigin`). Safe from spoofing, but it is another value to set in three deployments and in every division copy. A wrong or lapsed domain would send tokens to the wrong host.
- **Keep the request origin** (ADR 0015 as written). It is broken in AWS and the home lab.
