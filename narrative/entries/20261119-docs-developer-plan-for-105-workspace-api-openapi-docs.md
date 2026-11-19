---
date: 2026-11-19
slug: docs-developer-plan-for-105-workspace-api-openapi-docs
title: "docs: developer plan for #105 workspace API OpenAPI docs"
summary: "- **Refusal responses come first (Task 2).** - `ProblemResponses.Refusals` holds the B22 codes and titles."
kind: product
status: accepted
sequence: 2026-11-19T14:29:44.000Z
evidence: "https://github.com/jamiemitchellconsultants/division-ontology/pull/110; merge commit 694ee670c7a00a1d5dda3e711429334b7ec38bbc"
---

## Context

The accepted design (#106, amended by #108) left the developer some open questions. A throwaway copy of `runtime/` answered each one, using SDK 10.0.302 and the pinned packages:

- **The build-time writer.** `Microsoft.Extensions.ApiDescription.Server` runs `ApiProgram.Main` inside `GetDocument.Insider` with no configuration. `ValidateOnStart` on `JwtBearerOptions` then fails the build. Nothing else resolves eagerly.
- **Determinism.** Two builds, and a build with deployment-shaped `Workspace__*` variables, produced byte-identical files.
- **Discovery.** An issuer that never replied still let the spec serve in 2.0–2.2 s, without sign-in.
- **Image publish.** `-p:OpenApiGenerateDocuments=false` turns generation off in `dotnet publish`.
- **Packages.** `Swashbuckle.AspNetCore.SwaggerUI` 10.3.1 targets net10.0 with no dependencies, and locked restore passes under Central Package Management.
- **Swagger UI sign-in.** Its PKCE redirect resolves from `window.location`, and its `/api/docs` redirect is relative.

The same work found the two problems that #108 then fixed in the design: the request-derived `servers` entry, and the empty 400, 401 and 403 bodies.

## Decision

- **Refusal responses come first (Task 2).**
  - `ProblemResponses.Refusals` holds the B22 codes and titles.
  - `UseRefusalResponses()`, a `UseStatusCodePages` hook placed straight after `UseExceptionHandler`, writes them for bodiless 400, 401 and 403 responses.
  - Doing this before the spec describes those responses means no commit describes a body that does not exist.
- **Error copy lives in one place.**
  - The spec's `unauthorized`, `forbidden` and `unreadableRequest` components are `WorkspaceProblem` responses, built from `Refusals`.
  - `ProducesWorkspaceErrors` rejects refusal codes, because `StatusFor` would describe an unknown code as 422.
- **Only `DocsSignIn` touches the served spec.** It removes `servers` unconditionally (ADR 0017), and then adds `workspaceSignIn` when it is configured. The served-spec check strips sign-in only, so any `servers` entry fails it.
- **Pre-operation statuses are left alone.**
  - The tests pin 415, 405 and 404 as empty.
  - The conformance recorder ignores 405, 413 and 415. 413 is raised while a selected endpoint reads its body, so B2 needs this to hold.
- **The tests run as Production.** `OpenApiHost` runs the API with `UseEnvironment("Production")`, as every deployment does. In Development, minimal APIs turn binding failures into 500s. API code is unchanged.
- **The architect's challenged assumption holds.** `UseStatusCodePages` only acts on a response that has not started and has no body or content type. The docs UI never returns 400, 401 or 403, and Task 8 pins that.

## Consequences

- No existing test asserts an empty 400, 401 or 403 body.
  - Task 2 extends `IdentityTests` (Keycloak and Entra), `AwsSimulationTests` (Lambda) and `WorkspaceUiTests` (U15) to check the new codes.
  - The web UI needs no code change.
- `verify-docs.sh` checks, in each deployment, that the served spec has no `servers` entry and that an anonymous data call gets `authenticationRequired`.
- The live Entra sign-in cannot be tested in this repository. It stays in the user's AWS acceptance walkthrough.
- The build starts on `feature/workspace-api-openapi-docs/build` only after this PR merges.

---

AI-Fingerprint: sha256:8f6fb653ee89

🤖 Generated with [Claude Code](https://claude.com/claude-code)
