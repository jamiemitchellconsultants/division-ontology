# Workspace API OpenAPI docs implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task by task. Steps use checkbox (`- [ ]`) syntax for tracking. Read the detailed companion plan before each task.

> **Status: draft, blocked by pushback.** Two pushbacks were raised on #105 while this plan was written. One went to the architect: the served `servers` entry is wrong in AWS and the home lab. The other went to the analyst: B2's response bodies for 401, 403 and 400. Tasks 2, 3 and 8 carry working assumptions, each marked **⚑ pending**. When the amendments land, regenerate those parts of this plan and of the detailed plan before the plan PR opens. Everything else stands.

**Goal:** Describe the workspace API in an OpenAPI 3.1 document generated from its code. Commit that document and keep it in step through CI. Serve it, with a Swagger UI that signs users in through the deployment's Keycloak or Entra, in every deployment, without opening any data.

**Architecture:** `Microsoft.AspNetCore.OpenApi` builds the `v1` document from route metadata. Workspace transformers add roles, security, problem responses and stable ordering. `Microsoft.Extensions.ApiDescription.Server` writes the deployment-neutral copy to `runtime/openapi/workspace-api.json` on every build of the API. At request time, a sign-in transformer adds a `workspaceSignIn` OAuth2 scheme from the identity provider's discovery document. The spec at `/api/openapi/v1.json` and Swagger UI at `/api/docs` are served anonymously from `ApiProgram.Configure`. A Docker-free `OpenApi` test category, an `ApiFixture` observation hook and an `api-contract` CI job enforce the spec.

**Tech Stack:** .NET 10 (SDK pinned by `runtime/global.json`), `Microsoft.AspNetCore.OpenApi` 10.0.13, `Microsoft.Extensions.ApiDescription.Server` 10.0.13, `Swashbuckle.AspNetCore.SwaggerUI` 10.3.1, `Microsoft.OpenApi` 2.12.2 (transitive), xUnit with `WebApplicationFactory`, Node's test runner with `yaml` for workflow and configuration tests, GitHub Actions `actions/setup-dotnet`.

**Spec:** [`docs/features/workspace-api-openapi-docs/feature.md`](../../../features/workspace-api-openapi-docs/feature.md) (vision, behaviours B1–B21 with U1–U14, architecture and traceability) and ADRs [0013](../../../adr/0013-generate-workspace-api-spec-at-build-time.md), [0014](../../../adr/0014-serve-spec-and-swagger-ui-from-the-api.md), [0015](../../../adr/0015-docs-ui-sign-in-reuses-web-client.md) and [0016](../../../adr/0016-check-spec-completeness-statically-and-by-observation.md). Exact signatures, tables, fixtures and test code are in [`docs/detailed-implementations/workspace-api-openapi-docs/plan.md`](../../../detailed-implementations/workspace-api-openapi-docs/plan.md).

## Global constraints

- Describe only what the API already does. No endpoint changes its behaviour, path or payload. The health check's anonymous object becomes a `HealthStatus` record that serialises to the same `{"status":"ready"}` JSON.
- The committed spec is deployment-neutral. It has no `servers` entry and no `workspaceSignIn`, issuer, tenant, client ID or secret, and it is generated with no database, identity provider, embedding provider or secret (B8).
- Output is deterministic. Paths, component schemas and each operation's responses are in ordinal key order. `info` is exactly `{"title": "Runtime knowledge workspace API", "version": "v1"}`, with no timestamp, build number or commit.
- The spec and the docs UI are the only anonymous additions. Every data route stays inside the authorised `/api` group (B19, ADR 0014).
- Status numbers for workspace error codes come only from `ProblemResponses.StatusFor`. Routes declare codes, never statuses (ADR 0016).
- Startup stays lazy. `Build` and `ConfigureServices` resolve no database, secret, embedding provider or identity metadata. `ValidateOnStart` on `JwtBearerOptions` is skipped only when `BuildTimeGeneration.IsActive` is true (ADR 0013).
- Package versions are pinned in `runtime/Directory.Packages.props`, and lock files are regenerated and committed. `dotnet restore --locked-mode` must pass.
- The spec changes in the same PR as the code that changes it. It is never left to a generated PR (B7).
- Every task is written test first and ends in one focused commit and push, after `git diff --cached` and `git diff --cached --check`. Run `npm test`, and `dotnet test runtime/RuntimeKnowledge.Tests --filter Category=OpenApi`, after each task. Run the Docker-backed suites wherever `WORKSPACE_TEST_POSTGRES` is available. Never stage `dist/`.
- Examples use only invented values: the `example-` source IDs and `https://example.test/` IRIs in `OpenApiExamples`.

## File map and interfaces

| File | Responsibility |
| --- | --- |
| `runtime/Directory.Packages.props`, `runtime/RuntimeKnowledge.Api/RuntimeKnowledge.Api.csproj`, `*/packages.lock.json` | Package pins, build-time writer settings (`OpenApiDocumentsDirectory`, `--file-name workspace-api`). |
| `runtime/RuntimeKnowledge.Api/BuildTimeGeneration.cs` | `BuildTimeGeneration.IsActive`: the entry assembly is `GetDocument.Insider`. |
| `runtime/RuntimeKnowledge.Api/Identity.cs` (modify) | Skip `ValidateOnStart` under build-time generation only. |
| `runtime/RuntimeKnowledge.Api/WorkspaceOpenApi.cs` | `AddWorkspaceOpenApi`, operation and document transformers, `RoleFor`, `StableOrder`. |
| `runtime/RuntimeKnowledge.Api/ProblemResponses.cs` (modify) | `StatusFor(code)`, the single code-to-status map, used by `WriteAsync` and by the spec. |
| `runtime/RuntimeKnowledge.Api/WorkspaceErrors.cs` | `WorkspaceErrorCodes` metadata, `ProducesWorkspaceErrors(params string[] codes)`, `RequestExample`. |
| `runtime/RuntimeKnowledge.Api/OpenApiExamples.cs` | Invented example values (B4). |
| `runtime/RuntimeKnowledge.Api/WorkspaceRoutes.cs`, `Program.cs` (modify) | Summaries, tags, success types, declared codes, examples; spec and docs mapping. |
| `runtime/RuntimeKnowledge.Api/DocsUi.cs` | `UseWorkspaceDocsUi`: Swagger UI at `api/docs` with PKCE from `Workspace:Docs`. |
| `runtime/RuntimeKnowledge.Api/DocsSignIn.cs` | Request-time `workspaceSignIn` transformer with bounded discovery. |
| `runtime/openapi/workspace-api.json` | The committed spec, written by the build. |
| `runtime/RuntimeKnowledge.Tests/OpenApi/*.cs` | `RepositoryPaths`, `OpenApiHost`, `SpecCompleteness`, `ResponseConformance` and the `OpenApi` category tests. |
| `runtime/RuntimeKnowledge.Tests/ApiFixture.cs` (modify) | Observation hook that fails a scenario on an undeclared status. |
| `.github/workflows/ci.yml`, `docs/automation-setup.md` | `api-contract` job and required check. |
| `deploy/workspace/app/Dockerfile`, `deploy/workspace/aws/package.sh` | Publish with `-p:OpenApiGenerateDocuments=false`. |
| `deploy/workspace/local/realm.json`, `home-lab/realm-template.json`, `local/compose.yml`, `home-lab/compose.yml`, `aws/identity.tf`, `aws/outputs.tf` | Docs redirect URI and `Workspace:Docs` settings. |
| `deploy/workspace/verify-docs.sh`, `local/smoke.sh`, `home-lab/verify-config.sh`, `aws/tests/workspace.tftest.hcl` | Deployment checks for the docs. |
| `deploy/workspace/aws/simulation.md`, `home-lab/deployment-contract.md`, `README.md` | Entra and Keycloak redirect documentation and the regeneration command. |
| `test/api-contract.test.mjs`, `test/workspace-docs-config.test.mjs`, `test/init-division.test.mjs` (modify) | Node checks of the workflow, publish switches, identity configuration, documentation and division copies. |

## Review focus

1. **The identity provider is slow, down or returns a discovery document without endpoints.** The spec is still served within about 2 seconds, without `workspaceSignIn`, and never fails with a 500. Task 8 tests a discovery call that never completes, one that throws and one with no endpoints.
2. **A developer's shell has `Workspace__*` variables or `ASPNETCORE_ENVIRONMENT=Development` set while they build.** The committed spec must still be byte-identical and neutral. Task 1 runs a build with deployment-shaped variables set, and Task 9 makes CI repeat that build on every PR.
3. **A new route is mapped with no description, or a handler raises a code its route never declared.** The static check names the operation. The observation hook names the method, route and status. Tasks 5 and 6 test both.
4. **A new spec file is generated but not committed** (the file is untracked, so `git diff` stays quiet). The drift check must still fail. Task 9 tests that `git status --porcelain` is used.
5. **The docs UI is reached through CloudFront and API Gateway, at `/api/docs` with no trailing slash.** The redirect and the OAuth callback must stay relative to the browser's origin. Task 7 tests the relative `Location` header through the Lambda host.

---

### Task 1: Build-time writer and committed spec

**Files:** Modify `runtime/Directory.Packages.props`, `runtime/RuntimeKnowledge.Api/RuntimeKnowledge.Api.csproj`, `runtime/RuntimeKnowledge.Api/Identity.cs`, `runtime/RuntimeKnowledge.Api/Program.cs` and the lock files. Create `runtime/RuntimeKnowledge.Api/BuildTimeGeneration.cs`, `runtime/RuntimeKnowledge.Api/WorkspaceOpenApi.cs` (registration only), `runtime/openapi/workspace-api.json`, `runtime/RuntimeKnowledge.Tests/OpenApi/RepositoryPaths.cs`, `runtime/RuntimeKnowledge.Tests/OpenApi/OpenApiHost.cs` and `runtime/RuntimeKnowledge.Tests/OpenApi/CommittedSpecTests.cs`.

**Interfaces:** Produce `BuildTimeGeneration.IsActive : bool`, `WorkspaceOpenApi.AddWorkspaceOpenApi(this IServiceCollection)`, `WorkspaceOpenApi.DocumentName = "v1"`, the spec endpoint `GET /api/openapi/v1.json`, `RepositoryPaths.Root`/`.Spec`, and `OpenApiHost.StartAsync(settings?, discovery?)` with `.Client`, `.SpecAsync()` and `.Endpoints()`.

**Behaviours:** B5, B8.

Commit: `feat(api): generate the workspace OpenAPI spec at build time`

### Task 2: Document and operation descriptions

**Files:** Modify `runtime/RuntimeKnowledge.Api/WorkspaceOpenApi.cs` and `runtime/openapi/workspace-api.json`. Create `runtime/RuntimeKnowledge.Tests/OpenApi/DocumentDescriptionTests.cs`.

**Interfaces:** Consume `OpenApiHost`. Produce `WorkspaceOpenApi.RoleExtension = "x-workspace-role"`, `WorkspaceOpenApi.Bearer = "bearer"`, `WorkspaceOpenApi.RoleFor(IList<object>) : string?`, component responses `unauthorized`, `forbidden` and `internalError`, and ordinal ordering of paths, schemas and responses.

**Behaviours:** B1, B2 (401, 403, 500; **⚑ pending** analyst on body shape), B8.

Commit: `feat(api): describe roles, security and stable order in the spec`

### Task 3: Workspace error codes through one map

**Files:** Modify `runtime/RuntimeKnowledge.Api/ProblemResponses.cs` and `runtime/RuntimeKnowledge.Api/WorkspaceOpenApi.cs`. Create `runtime/RuntimeKnowledge.Api/WorkspaceErrors.cs`, `runtime/RuntimeKnowledge.Tests/OpenApi/ProblemResponseTests.cs` and `runtime/RuntimeKnowledge.Tests/OpenApi/WorkspaceErrorsTests.cs`.

**Interfaces:** Produce `ProblemResponses.StatusFor(string code) : int`, `WorkspaceErrorCodes(IReadOnlyList<string> Codes)`, `RouteHandlerBuilder.ProducesWorkspaceErrors(params string[] codes)`, schemas `WorkspaceProblem` and `WorkspaceConflictProblem`, and component response `unreadableRequest` (**⚑ pending** analyst on 400).

**Behaviours:** B2.

Commit: `feat(api): derive problem responses from declared workspace error codes`

### Task 4: Describe every route

**Files:** Modify `runtime/RuntimeKnowledge.Api/WorkspaceRoutes.cs`, `runtime/RuntimeKnowledge.Api/Program.cs`, `runtime/RuntimeKnowledge.Api/WorkspaceErrors.cs` (add `RequestExample`) and `runtime/openapi/workspace-api.json`. Create `runtime/RuntimeKnowledge.Api/OpenApiExamples.cs`, `runtime/RuntimeKnowledge.Tests/OpenApi/EndpointDescriptionTests.cs` and `runtime/RuntimeKnowledge.Tests/OpenApi/ExampleTests.cs`.

**Interfaces:** Consume `ProducesWorkspaceErrors`. Produce `HealthStatus(string Status)`, `RequestExample(object Value)`, `OpenApiExamples.All`, and the route table in the detailed plan (summary, tag, role, success status and type, request type, codes).

**Behaviours:** B1, B2, B4.

Commit: `feat(api): describe every workspace operation`

### Task 5: Static completeness check

**Files:** Create `runtime/RuntimeKnowledge.Tests/OpenApi/SpecCompleteness.cs` and `runtime/RuntimeKnowledge.Tests/OpenApi/CompletenessTests.cs`.

**Interfaces:** Consume `OpenApiHost.Endpoints()` and `.SpecAsync()`. Produce `SpecCompleteness.Find(IEnumerable<RouteEndpoint>, JsonNode) : IReadOnlyList<string>`, which returns gaps formatted as `"<METHOD> <path>: <gap>"`, and `SpecCompleteness.Normalise(string pattern) : string`.

**Behaviours:** B3.

Commit: `test(api): fail when a mapped operation is not fully described`

### Task 6: Response conformance hook

**Files:** Create `runtime/RuntimeKnowledge.Tests/OpenApi/ResponseConformance.cs` and `runtime/RuntimeKnowledge.Tests/OpenApi/ResponseConformanceTests.cs`. Modify `runtime/RuntimeKnowledge.Tests/ApiFixture.cs`.

**Interfaces:** Consume `SpecCompleteness.Normalise` and `OpenApiHost.Start(…, services)`. Produce `ObservedResponse(string Method, string Route, int Status)`, `ResponseConformance.Recorder : IStartupFilter`, `ResponseConformance.Undeclared(IEnumerable<ObservedResponse>, JsonNode) : IReadOnlyList<string>`, and an `ApiFixture.DisposeAsync` that throws `UndeclaredResponseException` listing them.

**Behaviours:** B2, B3.

Commit: `test(api): fail scenarios that observe undeclared statuses`

### Task 7: Anonymous spec and Swagger UI

**Files:** Create `runtime/RuntimeKnowledge.Api/DocsUi.cs` and `runtime/RuntimeKnowledge.Tests/OpenApi/DocsEndpointTests.cs`. Modify `runtime/RuntimeKnowledge.Api/Program.cs` and `runtime/RuntimeKnowledge.Tests/AwsSimulationTests.cs` (one Docker-free test).

**Interfaces:** Produce `DocsUi.SpecPath = "/api/openapi/v1.json"`, `DocsUi.RoutePrefix = "api/docs"`, `IApplicationBuilder.UseWorkspaceDocsUi()`, and settings `Workspace:Docs:ClientId` and `Workspace:Docs:Scopes` (space-separated).

**Behaviours:** B9 (UI half), B13, B15, B18, B19.

Commit: `feat(api): serve the spec and Swagger UI anonymously under /api`

### Task 8: Sign-in details in the served spec

**Files:** Create `runtime/RuntimeKnowledge.Api/DocsSignIn.cs`, `runtime/RuntimeKnowledge.Tests/OpenApi/DocsSignInTests.cs` and `runtime/RuntimeKnowledge.Tests/OpenApi/ServedSpecTests.cs`. Modify `runtime/RuntimeKnowledge.Api/WorkspaceOpenApi.cs`.

**Interfaces:** Consume `IOptionsMonitor<JwtBearerOptions>` and `Workspace:Docs:*`. Produce `DocsSignIn.Scheme = "workspaceSignIn"`, `DocsSignIn.DiscoveryWait` (2 s), and `ServedSpec.StripDeploymentAdditions(JsonNode) : JsonNode` in the tests.

**Behaviours:** B9, B14 (**⚑ pending** architect on `servers`), B20, B21.

Commit: `feat(api): add deployment sign-in details to the served spec`

### Task 9: CI drift check and publish switches

**Files:** Modify `.github/workflows/ci.yml`, `docs/automation-setup.md`, `deploy/workspace/app/Dockerfile`, `deploy/workspace/aws/package.sh` and `README.md`. Create `test/api-contract.test.mjs`.

**Interfaces:** Produce the `api-contract` job, the drift message `The committed API spec is out of date. Run: dotnet build runtime/RuntimeKnowledge.Api, then commit runtime/openapi/.`, and `-p:OpenApiGenerateDocuments=false` on both API publishes.

**Behaviours:** B3, B5, B6, B7, B8.

Commit: `ci: add the api-contract drift and completeness check`

### Task 10: Identity-provider configuration and deployment checks

**Files:** Modify `deploy/workspace/local/realm.json`, `deploy/workspace/home-lab/realm-template.json`, `deploy/workspace/local/compose.yml`, `deploy/workspace/home-lab/compose.yml`, `deploy/workspace/home-lab/verify-config.sh`, `deploy/workspace/aws/identity.tf`, `deploy/workspace/aws/outputs.tf`, `deploy/workspace/aws/tests/workspace.tftest.hcl`, `deploy/workspace/local/smoke.sh`, `deploy/workspace/aws/simulation.md`, `deploy/workspace/home-lab/deployment-contract.md` and `test/init-division.test.mjs`. Create `deploy/workspace/verify-docs.sh` and `test/workspace-docs-config.test.mjs`.

**Interfaces:** Produce the redirect URI `<origin>/api/docs/oauth2-redirect.html`, environment variables `Workspace__Docs__ClientId` and `Workspace__Docs__Scopes`, the output `entra_docs_redirect_uri`, and `verify-docs.sh <origin> [--expect-sign-in]`.

**Behaviours:** B9, B15, B16, B17, B18, B19, B21.

Commit: `feat(deploy): register the docs UI callback and settings in every deployment`

## Behaviour coverage

| Behaviour | Tasks |
| --- | --- |
| B1 | 2, 4, 5 |
| B2 | 2, 3, 4, 6 |
| B3 | 5, 6, 9 |
| B4 | 4 |
| B5 | 1, 9 |
| B6, B7 | 9 |
| B8 | 1, 2, 8, 9 |
| B9 | 7, 8, 10 |
| B10–B12 | The existing identity tests, unchanged, plus the user's walkthrough. Tasks 7 and 10 prove the docs UI reaches the same routes and tokens. |
| B13 | 7 |
| B14 | 8 |
| B15 | 7, 10 |
| B16 | 1, 9, 10 |
| B17 | 10 |
| B18, B19 | 7, 10 |
| B20 | 8 |
| B21 | 8, 10 |

## Task execution

Follow the [detailed plan](../../../detailed-implementations/workspace-api-openapi-docs/plan.md) for exact interfaces, tables, test code and commands. For every task:

- [ ] Add its named tests. Run the focused command and see it fail for the missing behaviour.
- [ ] Implement the task's interface. Run its focused tests and see them pass.
- [ ] Run `npm test`, `dotnet test runtime/RuntimeKnowledge.Tests --filter Category=OpenApi`, and the Docker-backed suites where available. Rebuild the API and stage `runtime/openapi/workspace-api.json` whenever it changes.
- [ ] Review `git diff --cached` and `git diff --cached --check`, then commit with the task's message and push.
