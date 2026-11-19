# 0014 — Serve the spec and Swagger UI anonymously from the API under /api

- **Status:** accepted
- **Date:** 2026-11-19
- **Feature:** #105 (docs/features/workspace-api-openapi-docs/)

## Context

The spec and a Swagger-style UI must be reachable at one path in the local, home-lab and AWS deployments, and in division copies (B15, B16), and must be open without sign-in (B18). Only `/api/*` reaches the API today, through Caddy, CloudFront and API Gateway. The home lab may have no internet access, and the AWS API runs in Lambda.

## Decision

Map `GET /api/openapi/v1.json` and Swagger UI at `/api/docs` in `ApiProgram.Configure`, outside the authorised `/api` group, with anonymous access. Use `Swashbuckle.AspNetCore.SwaggerUI` for the UI only. Its assets are embedded in the assembly.

## Consequences

- No change to routing, CDN or API Gateway in any deployment, and copies inherit it unchanged.
- The UI works offline and in Lambda with no CDN.
- Every data route stays inside the authorised group, so opening the docs never opens data (B19).
- Future work must keep these two endpoints, and only these, anonymous.

## Alternatives considered

- **Scalar:** a modern UI with comparable sign-in support, but it was not chosen. The user preferred Swagger UI, and serving Scalar's assets locally rather than from a CDN needs extra checking.
- **Docs as static files in the web app at `/docs`:** needs routing changes in three deployments, and the static page could not carry deployment sign-in details without its own configuration.
- **A separate docs host:** more to deploy and secure, for no behavioural gain.
