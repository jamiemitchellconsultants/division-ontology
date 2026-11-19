---
date: 2026-11-19
slug: docs-design-for-workspace-api-openapi-docs-105
title: "docs: design for workspace API OpenAPI docs (#105)"
summary: "- Generate an OpenAPI 3.1 spec from the code with `Microsoft.AspNetCore.OpenApi`."
kind: product
status: accepted
sequence: 2026-11-19T08:07:38.000Z
evidence: "https://github.com/jamiemitchellconsultants/division-ontology/pull/106; merge commit 06acf475e685d8110e896f073a63c0cee18a378d"
---

## Context

A deployed runtime knowledge workspace exposes a JSON API under `/api`, but nothing describes it except the C# routes and contract types. Integrators of other division systems, the template's own developers, and reviewers of API changes all need an accurate, always current contract, and a page where they can browse it and try calls with their own roles. Research showed that .NET 10 can generate the spec at build time, that Swagger UI supports PKCE sign-in with Keycloak and Entra, and that all three deployments already route `/api/*` to the API.

## Decision

- Generate an OpenAPI 3.1 spec from the code with `Microsoft.AspNetCore.OpenApi`. Write a deployment-neutral copy to `runtime/openapi/workspace-api.json` on every build, commit it in the same PR as the code that changes it, and fail a new `api-contract` CI job when it is out of date ([ADR 0013](https://github.com/jamiemitchellconsultants/division-ontology/blob/feature/workspace-api-openapi-docs/design/docs/adr/0013-generate-workspace-api-spec-at-build-time.md)).
- Serve the spec at `/api/openapi/v1.json` and Swagger UI at `/api/docs` from the API, both open to anonymous visitors. Every data route keeps its existing authorisation, and no routing changes are needed ([ADR 0014](https://github.com/jamiemitchellconsultants/division-ontology/blob/feature/workspace-api-openapi-docs/design/docs/adr/0014-serve-spec-and-swagger-ui-from-the-api.md)). Scalar was considered and not chosen.
- Sign in from the docs UI with PKCE, reusing the web UI's public client in Keycloak and Entra plus one extra redirect URI. Add sign-in endpoints from the provider's discovery document only to the served spec ([ADR 0015](https://github.com/jamiemitchellconsultants/division-ontology/blob/feature/workspace-api-openapi-docs/design/docs/adr/0015-docs-ui-sign-in-reuses-web-client.md)). A separate docs client was rejected for now: it would give separate revocation and logging, but the docs UI grants nothing beyond the user's roles, and switching later needs configuration only.
- Enforce completeness with a Docker-free static test in CI, plus a hook in the existing scenario tests that fails on any observed status the spec does not declare ([ADR 0016](https://github.com/jamiemitchellconsultants/division-ontology/blob/feature/workspace-api-openapi-docs/design/docs/adr/0016-check-spec-completeness-statically-and-by-observation.md)).

## Consequences

- API contract changes become reviewable diffs, and an unregenerated change cannot pass CI.
- API startup must stay lazy, because the build-time writer runs the entry point with no database or identity configuration.
- Each identity provider needs one extra redirect URI on the existing SPA client. The API gains the `Workspace:Docs:ClientId` and `Workspace:Docs:Scopes` settings.
- The observation check only runs where the Docker-backed scenario suites run, and CI does not run those yet.
- Deliberately open: service clients for other systems, an MCP server, client SDKs, an API versioning policy, and a switch to hide the docs per deployment.

---

AI-Fingerprint: sha256:ffb5f3bd949b

🤖 Generated with [Claude Code](https://claude.com/claude-code)
