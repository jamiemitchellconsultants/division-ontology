# 0013 — Generate the workspace API spec from code at build time and commit it

- **Status:** accepted
- **Date:** 2026-11-19
- **Feature:** #105 (docs/features/workspace-api-openapi-docs/)

## Context

Integrators and reviewers need a contract that always matches the code (B1, B5–B8). The API is an ASP.NET Core 10 minimal API with no spec today, and no CI job that builds .NET. The committed spec must be the same in every deployment, and must be produced without a database or identity provider.

## Decision

Generate the OpenAPI 3.1 document `v1` with `Microsoft.AspNetCore.OpenApi`, and write it to `runtime/openapi/workspace-api.json` on every build with `Microsoft.Extensions.ApiDescription.Server`. Commit that file, and change it in the same pull request as the code that changes it. A new `api-contract` CI job builds the API and fails if `git diff --exit-code runtime/openapi/` shows a difference, naming the regeneration command.

## Consequences

- A contract change shows up as a reviewable diff, and an unregenerated change cannot pass CI.
- Application startup must stay lazy. `Build` and `ConfigureServices` may not resolve the database, secrets, embeddings or identity metadata, because the build-time writer runs the entry point with no configuration.
- Document output must be deterministic, with no timestamps or build numbers in `info`.
- The image publish turns generation off, so images do not depend on it.
- Unlike `dist/`, this generated file is never left to a separate pull request.

## Alternatives considered

- **Serve only at runtime, with no committed file:** no review signal and no drift check, so it fails vision outcome 2.
- **Generate from a test (fetch the spec from a test host and write it out):** works, but "build the API" is the natural regeneration step, and a test that writes files is surprising.
- **Swashbuckle's generator:** a third-party generator, when the framework now ships one. Swashbuckle is used for the UI only (ADR 0014).
- **Hand-written spec:** drifts from the code by design.
