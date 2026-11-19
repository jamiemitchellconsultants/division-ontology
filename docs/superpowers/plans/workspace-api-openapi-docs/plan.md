# Workspace API OpenAPI docs implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task by task. Steps use checkbox (`- [ ]`) syntax for tracking. Read the detailed companion plan before each task.

**Goal:** Describe the workspace API in an OpenAPI 3.1 document generated from its code. Commit that document and keep it in step through CI. Serve it, with a Swagger UI that signs users in through the deployment's Keycloak or Entra, in every deployment, without opening any data. Give the API's 400, 401 and 403 responses the same problem-details shape as every other error.

**Architecture:**
- **Generation.** `Microsoft.AspNetCore.OpenApi` builds the `v1` document from route metadata. Workspace transformers add roles, security, problem responses and stable ordering. `Microsoft.Extensions.ApiDescription.Server` writes the deployment-neutral copy to `runtime/openapi/workspace-api.json` on every build of the API.
- **Refusals.** A `UseStatusCodePages` hook gives bodiless 400, 401 and 403 responses their fixed problem-details bodies from `ProblemResponses.Refusals` (ADR 0018).
- **Serving.** At request time, a sign-in transformer removes the request-derived `servers` entry (ADR 0017). It adds a `workspaceSignIn` OAuth2 scheme from the identity provider's discovery document. The spec at `/api/openapi/v1.json` and Swagger UI at `/api/docs` are served anonymously from `ApiProgram.Configure`.
- **Enforcement.** A Docker-free `OpenApi` test category, an `ApiFixture` observation hook and an `api-contract` CI job enforce the spec.

**Tech Stack:** .NET 10 (SDK pinned by `runtime/global.json`), `Microsoft.AspNetCore.OpenApi` 10.0.13, `Microsoft.Extensions.ApiDescription.Server` 10.0.13, `Swashbuckle.AspNetCore.SwaggerUI` 10.3.1, `Microsoft.OpenApi` 2.12.2 (transitive), xUnit with `WebApplicationFactory` and `TestServer`, Node's test runner with `yaml` for workflow and configuration tests, GitHub Actions `actions/setup-dotnet`.

**Spec:** [`docs/features/workspace-api-openapi-docs/feature.md`](../../../features/workspace-api-openapi-docs/feature.md) (vision, behaviours B1–B22 with U1–U17, architecture and traceability). ADRs [0013](../../../adr/0013-generate-workspace-api-spec-at-build-time.md), [0014](../../../adr/0014-serve-spec-and-swagger-ui-from-the-api.md), [0015](../../../adr/0015-docs-ui-sign-in-reuses-web-client.md), [0016](../../../adr/0016-check-spec-completeness-statically-and-by-observation.md), [0017](../../../adr/0017-serve-the-spec-without-a-server-address.md) and [0018](../../../adr/0018-give-refusals-fixed-problem-details-bodies.md). Exact signatures, tables, fixtures and test code are in [`docs/detailed-implementations/workspace-api-openapi-docs/plan.md`](../../../detailed-implementations/workspace-api-openapi-docs/plan.md).

## Global constraints

- **Behaviour.**
  - Describe only what the API already does. No endpoint changes its path or payload, and no request changes its status.
  - The one behaviour change is the vision's exception: bodiless 400, 401 and 403 responses gain fixed problem-details bodies (B22). Their codes are `unreadableRequest`, `authenticationRequired` and `roleRequired`.
  - A 401 keeps `WWW-Authenticate: Bearer`, and its body is the same whatever the token failure.
  - The health check's anonymous object becomes a `HealthStatus` record that serialises to the same `{"status":"ready"}` JSON.
- **Pre-operation statuses.** 404 for an unknown path, 405, 413 and 415 are left empty, and they are not operation statuses (B2, ADR 0018).
- **The committed spec is deployment-neutral.** It has no `servers` entry and no `workspaceSignIn`, issuer, tenant, client ID or secret. It is generated with no database, identity provider, embedding provider or secret (B8).
- **The served spec** is the committed contract plus `workspaceSignIn` only. It never has a `servers` entry, whatever the request's `Host` or forwarded headers say (B14, ADR 0017).
- **Output is deterministic.** Paths, component schemas, component responses and each operation's responses are in ordinal key order. `info` is exactly `{"title": "Runtime knowledge workspace API", "version": "v1"}`, with no timestamp, build number or commit.
- **Anonymous access.** The spec and the docs UI are the only anonymous additions. Every data route stays inside the authorised `/api` group (B19, ADR 0014).
- **Error copy.** Status numbers for workspace error codes come only from `ProblemResponses.StatusFor`. Refusal codes and titles come only from `ProblemResponses.Refusals`. Routes declare workspace codes, never statuses and never refusal codes (ADR 0016, ADR 0018).
- **Startup stays lazy.** `Build` and `ConfigureServices` resolve no database, secret, embedding provider or identity metadata. `ValidateOnStart` on `JwtBearerOptions` is skipped only when `BuildTimeGeneration.IsActive` is true (ADR 0013).
- **Packages.** Versions are pinned in `runtime/Directory.Packages.props`, and lock files are regenerated and committed. `dotnet restore --locked-mode` must pass.
- **The spec changes with its code.** It goes in the same PR as the code that changes it, and is never left to a generated PR (B7).
- **Each task.**
  - Write it test first, and end it with one focused commit and push, after `git diff --cached` and `git diff --cached --check`.
  - After each task, run `npm test` and `dotnet test runtime/RuntimeKnowledge.Tests --filter Category=OpenApi`.
  - Run the Docker-backed suites wherever `WORKSPACE_TEST_POSTGRES` is available.
  - Never stage `dist/`.
- **Examples** use only invented values: the `example-` source IDs and `https://example.test/` IRIs in `OpenApiExamples`.

## File map and interfaces

| File | Responsibility |
| --- | --- |
| `runtime/Directory.Packages.props`, `runtime/RuntimeKnowledge.Api/RuntimeKnowledge.Api.csproj`, `*/packages.lock.json` | Package pins and the build-time writer settings (`OpenApiDocumentsDirectory`, `--file-name workspace-api`). |
| `runtime/RuntimeKnowledge.Api/BuildTimeGeneration.cs` | `BuildTimeGeneration.IsActive`: true when the entry assembly is `GetDocument.Insider`. |
| `runtime/RuntimeKnowledge.Api/Identity.cs` (modify) | Skip `ValidateOnStart` under build-time generation only. |
| `runtime/RuntimeKnowledge.Api/ProblemResponses.cs` (modify) | `StatusFor(code)`, the single code-to-status map; `Refusals`, the B22 codes and titles; `UseRefusalResponses()`, the status-code hook. |
| `runtime/RuntimeKnowledge.Api/WorkspaceOpenApi.cs` | `AddWorkspaceOpenApi`, the operation and document transformers, `RoleFor` and `StableOrder`. |
| `runtime/RuntimeKnowledge.Api/WorkspaceErrors.cs` | `WorkspaceErrorCodes` metadata, `ProducesWorkspaceErrors(params string[] codes)` and `RequestExample`. |
| `runtime/RuntimeKnowledge.Api/OpenApiExamples.cs` | Invented example values (B4). |
| `runtime/RuntimeKnowledge.Api/WorkspaceRoutes.cs`, `Program.cs` (modify) | Summaries, tags, success types, declared codes and examples; the refusal hook, the spec and the docs mapping. |
| `runtime/RuntimeKnowledge.Api/DocsUi.cs` | `UseWorkspaceDocsUi`: Swagger UI at `api/docs`, with PKCE from `Workspace:Docs`. |
| `runtime/RuntimeKnowledge.Api/DocsSignIn.cs` | Request-time transformer: removes `servers` and adds `workspaceSignIn`, with bounded discovery. |
| `runtime/openapi/workspace-api.json` | The committed spec, written by the build. |
| `runtime/RuntimeKnowledge.Tests/OpenApi/*.cs` | `RepositoryPaths`, `OpenApiHost`, `SpecCompleteness`, `ResponseConformance` and the `OpenApi` category tests. |
| `runtime/RuntimeKnowledge.Tests/ApiFixture.cs` (modify) | The observation hook, which fails a scenario on an undeclared status. |
| `runtime/RuntimeKnowledge.Tests/{IdentityTests,AwsSimulationTests,WorkspaceUiTests}.cs` (modify) | Refusal codes for both providers and through Lambda; the web UI showing the API's title (U15). |
| `.github/workflows/ci.yml`, `docs/automation-setup.md` | The `api-contract` job and its required check. |
| `deploy/workspace/app/Dockerfile`, `deploy/workspace/aws/package.sh` | Publish with `-p:OpenApiGenerateDocuments=false`. |
| `deploy/workspace/local/realm.json`, `home-lab/realm-template.json`, `local/compose.yml`, `home-lab/compose.yml`, `aws/identity.tf`, `aws/outputs.tf` | The docs redirect URI and the `Workspace:Docs` settings. |
| `deploy/workspace/verify-docs.sh`, `local/smoke.sh`, `home-lab/verify-config.sh`, `aws/tests/workspace.tftest.hcl` | Deployment checks for the docs. |
| `deploy/workspace/aws/simulation.md`, `home-lab/deployment-contract.md`, `README.md` | The Entra and Keycloak redirect documentation and the regeneration command. |
| `test/api-contract.test.mjs`, `test/workspace-docs-config.test.mjs`, `test/init-division.test.mjs` (modify) | Node checks of the workflow, publish switches, identity configuration, documentation and division copies. |

## Review focus

1. **The identity provider is slow, down, or returns a discovery document without endpoints.** The spec is still served within about 2 seconds, without `workspaceSignIn` and without `servers`, and never fails with a 500. Task 9 tests a discovery call that never completes, one that throws and one with no endpoints.
2. **A client sends a body that is not JSON, the wrong method or an unknown path.** The API still returns the framework's empty 415, 405 or 404, the refusal hook leaves it alone, and the conformance hook does not report it. Task 2 tests each through the host and through Lambda. Task 7 tests that the recorder ignores them.
3. **A developer's shell has `Workspace__*` variables or `ASPNETCORE_ENVIRONMENT=Development` set while they build.** The committed spec must still be byte-identical and neutral. Task 1 runs a build with deployment-shaped variables set, and Task 10 makes CI repeat that build on every PR.
4. **A new route is mapped with no description, or a handler raises a code its route never declared.** The static check names the operation, and the observation hook names the method, route and status. Tasks 6 and 7 test both. Task 4 stops a refusal code being declared as a workspace code.
5. **The docs UI is reached through CloudFront and API Gateway at `/api/docs`, with no trailing slash, or with a spoofed `Host` header.** The redirect and the OAuth callback stay relative to the browser's origin, and the spec names no server. Task 8 tests the relative `Location` through the Lambda host. Task 9 tests a request with a hostile `Host` and forwarded headers.

---

### Task 1: Build-time writer and committed spec

**Files:**
- Modify `runtime/Directory.Packages.props`, `runtime/RuntimeKnowledge.Api/RuntimeKnowledge.Api.csproj`, `runtime/RuntimeKnowledge.Api/Identity.cs`, `runtime/RuntimeKnowledge.Api/Program.cs` and the lock files.
- Create `runtime/RuntimeKnowledge.Api/BuildTimeGeneration.cs`, `runtime/RuntimeKnowledge.Api/WorkspaceOpenApi.cs` (registration only), `runtime/openapi/workspace-api.json`, `runtime/RuntimeKnowledge.Tests/OpenApi/RepositoryPaths.cs`, `runtime/RuntimeKnowledge.Tests/OpenApi/OpenApiHost.cs` and `runtime/RuntimeKnowledge.Tests/OpenApi/CommittedSpecTests.cs`.

**Interfaces:** Produce:
- `BuildTimeGeneration.IsActive : bool`
- `WorkspaceOpenApi.AddWorkspaceOpenApi(this IServiceCollection)` and `WorkspaceOpenApi.DocumentName = "v1"`
- the spec endpoint `GET /api/openapi/v1.json`
- `RepositoryPaths.Root` and `.Spec`
- `OpenApiHost.Start(settings?, discovery?)`, which runs in `Production`, with `.Client`, `.SpecAsync()` and `.Endpoints()`

**Behaviours:** B5, B8.

Commit: `feat(api): generate the workspace OpenAPI spec at build time`

### Task 2: Refusal responses

**Files:**
- Modify `runtime/RuntimeKnowledge.Api/ProblemResponses.cs`, `runtime/RuntimeKnowledge.Api/Program.cs`, `runtime/RuntimeKnowledge.Tests/OpenApi/OpenApiHost.cs`, `runtime/RuntimeKnowledge.Tests/AwsSimulationTests.cs`, `runtime/RuntimeKnowledge.Tests/IdentityTests.cs` and `runtime/RuntimeKnowledge.Tests/WorkspaceUiTests.cs`.
- Create `runtime/RuntimeKnowledge.Tests/OpenApi/RefusalResponseTests.cs`.

**Interfaces:** Produce:
- `ProblemResponses.Refusal(string Code, string Title)` and `ProblemResponses.Refusals : IReadOnlyDictionary<int, Refusal>`, with exactly the B22 entries for 400, 401 and 403
- `IApplicationBuilder.UseRefusalResponses()`, called straight after `UseExceptionHandler`
- `OpenApiHost.ClientWithToken(string[] roles, bool expired = false, bool wrongSignature = false)`
- `AwsSimulationTests.Request(method, path, contentType?, body?)`

**Behaviours:** B2, B12, B13, B19, B22, U15.

Commit: `feat(api): give 400, 401 and 403 fixed problem-details bodies`

### Task 3: Document and operation descriptions

**Files:** Modify `runtime/RuntimeKnowledge.Api/WorkspaceOpenApi.cs` and `runtime/openapi/workspace-api.json`. Create `runtime/RuntimeKnowledge.Tests/OpenApi/DocumentDescriptionTests.cs`.

**Interfaces:**
- **Consume:** `OpenApiHost` and `ProblemResponses.Refusals`.
- **Produce:**
  - `WorkspaceOpenApi.RoleExtension = "x-workspace-role"`, `WorkspaceOpenApi.Bearer = "bearer"` and `WorkspaceOpenApi.RoleFor(IList<object>) : string?`
  - schemas `Problem` and `WorkspaceProblem`
  - component responses `unauthorized` and `forbidden`, as `WorkspaceProblem` with `x-workspace-codes`, and `internalError`
  - ordinal ordering of paths, schemas and responses

**Behaviours:** B1, B2 (401, 403 and 500), B8, B22.

Commit: `feat(api): describe roles, security, refusals and stable order in the spec`

### Task 4: Workspace error codes through one map

**Files:** Modify `runtime/RuntimeKnowledge.Api/ProblemResponses.cs` and `runtime/RuntimeKnowledge.Api/WorkspaceOpenApi.cs`. Create `runtime/RuntimeKnowledge.Api/WorkspaceErrors.cs`, `runtime/RuntimeKnowledge.Tests/OpenApi/ProblemResponseTests.cs` and `runtime/RuntimeKnowledge.Tests/OpenApi/WorkspaceErrorsTests.cs`.

**Interfaces:** Produce:
- `ProblemResponses.StatusFor(string code) : int`
- `WorkspaceErrorCodes(IReadOnlyList<string> Codes)`
- `RouteHandlerBuilder.ProducesWorkspaceErrors(params string[] codes)`, which rejects refusal codes
- schema `WorkspaceConflictProblem`
- component response `unreadableRequest`, as `WorkspaceProblem` with code `unreadableRequest`

**Behaviours:** B2, B22.

Commit: `feat(api): derive problem responses from declared workspace error codes`

### Task 5: Describe every route

**Files:** Modify `runtime/RuntimeKnowledge.Api/WorkspaceRoutes.cs`, `runtime/RuntimeKnowledge.Api/Program.cs`, `runtime/RuntimeKnowledge.Api/WorkspaceErrors.cs` (add `RequestExample`) and `runtime/openapi/workspace-api.json`. Create `runtime/RuntimeKnowledge.Api/OpenApiExamples.cs`, `runtime/RuntimeKnowledge.Tests/OpenApi/EndpointDescriptionTests.cs` and `runtime/RuntimeKnowledge.Tests/OpenApi/ExampleTests.cs`.

**Interfaces:**
- **Consume:** `ProducesWorkspaceErrors`.
- **Produce:**
  - `HealthStatus(string Status)`, `RequestExample(object Value)` and `OpenApiExamples.All`
  - the route table in the detailed plan: summary, tag, role, success status and type, request type, and codes

**Behaviours:** B1, B2, B4.

Commit: `feat(api): describe every workspace operation`

### Task 6: Static completeness check

**Files:** Create `runtime/RuntimeKnowledge.Tests/OpenApi/SpecCompleteness.cs` and `runtime/RuntimeKnowledge.Tests/OpenApi/CompletenessTests.cs`.

**Interfaces:**
- **Consume:** `OpenApiHost.Endpoints()` and `.SpecAsync()`.
- **Produce:**
  - `SpecCompleteness.Find(IEnumerable<RouteEndpoint>, JsonNode) : IReadOnlyList<string>`, which returns gaps formatted as `"<METHOD> <path>: <gap>"`
  - `SpecCompleteness.Normalise(string pattern) : string`

**Behaviours:** B3.

Commit: `test(api): fail when a mapped operation is not fully described`

### Task 7: Response conformance hook

**Files:** Create `runtime/RuntimeKnowledge.Tests/OpenApi/ResponseConformance.cs` and `runtime/RuntimeKnowledge.Tests/OpenApi/ResponseConformanceTests.cs`. Modify `runtime/RuntimeKnowledge.Tests/ApiFixture.cs`.

**Interfaces:**
- **Consume:** `SpecCompleteness.Normalise` and `OpenApiHost.Start(…, services)`.
- **Produce:**
  - `ObservedResponse(string Method, string Route, int Status)`
  - `ResponseConformance.TransportStatuses = [405, 413, 415]`
  - `ResponseConformance.Recorder : IStartupFilter`
  - `ResponseConformance.Undeclared(IEnumerable<ObservedResponse>, JsonNode) : IReadOnlyList<string>`
  - an `ApiFixture.DisposeAsync` that throws `UndeclaredResponseException`, listing the undeclared responses

**Behaviours:** B2, B3.

Commit: `test(api): fail scenarios that observe undeclared statuses`

### Task 8: Anonymous spec and Swagger UI

**Files:** Create `runtime/RuntimeKnowledge.Api/DocsUi.cs` and `runtime/RuntimeKnowledge.Tests/OpenApi/DocsEndpointTests.cs`. Modify `runtime/RuntimeKnowledge.Api/Program.cs` and `runtime/RuntimeKnowledge.Tests/AwsSimulationTests.cs` (one Docker-free test).

**Interfaces:** Produce:
- `DocsUi.SpecPath = "/api/openapi/v1.json"` and `DocsUi.RoutePrefix = "api/docs"`
- `IApplicationBuilder.UseWorkspaceDocsUi()`, called after `UseRefusalResponses`
- the settings `Workspace:Docs:ClientId` and `Workspace:Docs:Scopes` (space-separated)

**Behaviours:** B9 (UI half), B13, B15, B18, B19.

Commit: `feat(api): serve the spec and Swagger UI anonymously under /api`

### Task 9: No server address, and sign-in details in the served spec

**Files:** Create `runtime/RuntimeKnowledge.Api/DocsSignIn.cs`, `runtime/RuntimeKnowledge.Tests/OpenApi/DocsSignInTests.cs` and `runtime/RuntimeKnowledge.Tests/OpenApi/ServedSpecTests.cs`. Modify `runtime/RuntimeKnowledge.Api/WorkspaceOpenApi.cs` and `runtime/RuntimeKnowledge.Tests/OpenApi/CommittedSpecTests.cs`.

**Interfaces:**
- **Consume:** `IOptionsMonitor<JwtBearerOptions>` and `Workspace:Docs:*`.
- **Produce:**
  - `DocsSignIn.Scheme = "workspaceSignIn"` and `DocsSignIn.DiscoveryWait` (2 s)
  - in the tests, `ServedSpec.StripDeploymentAdditions(JsonNode) : JsonNode`, which strips sign-in only

**Behaviours:** B9, B14, B15, B20, B21.

Commit: `feat(api): serve the spec without a server address and with sign-in details`

### Task 10: CI drift check and publish switches

**Files:** Modify `.github/workflows/ci.yml`, `docs/automation-setup.md`, `deploy/workspace/app/Dockerfile`, `deploy/workspace/aws/package.sh` and `README.md`. Create `test/api-contract.test.mjs`.

**Interfaces:** Produce:
- the `api-contract` job
- the drift message `The committed API spec is out of date. Run: dotnet build runtime/RuntimeKnowledge.Api, then commit runtime/openapi/.`
- `-p:OpenApiGenerateDocuments=false` on both API publishes

**Behaviours:** B3, B5, B6, B7, B8.

Commit: `ci: add the api-contract drift and completeness check`

### Task 11: Identity-provider configuration and deployment checks

**Files:**
- Modify:
  - in `deploy/workspace/local/`: `realm.json`, `compose.yml` and `smoke.sh`
  - in `deploy/workspace/home-lab/`: `realm-template.json`, `compose.yml`, `verify-config.sh` and `deployment-contract.md`
  - in `deploy/workspace/aws/`: `identity.tf`, `outputs.tf`, `tests/workspace.tftest.hcl` and `simulation.md`
  - `test/init-division.test.mjs`
- Create `deploy/workspace/verify-docs.sh` and `test/workspace-docs-config.test.mjs`.

**Interfaces:** Produce:
- the redirect URI `<origin>/api/docs/oauth2-redirect.html`
- the environment variables `Workspace__Docs__ClientId` and `Workspace__Docs__Scopes`
- the output `entra_docs_redirect_uri`
- `verify-docs.sh <origin> [--expect-sign-in]`, which also checks that the served spec has no `servers` entry and that an anonymous data call gets `authenticationRequired`

**Behaviours:** B9, B14, B15, B16, B17, B18, B19, B21.

Commit: `feat(deploy): register the docs UI callback and settings in every deployment`

## Behaviour coverage

| Behaviour | Tasks |
| --- | --- |
| B1 | 3, 5, 6 |
| B2 | 2, 3, 4, 5, 7 |
| B3 | 6, 7, 10 |
| B4 | 5 |
| B5 | 1, 10 |
| B6, B7 | 10 |
| B8 | 1, 3, 9, 10 |
| B9 | 8, 9, 11 |
| B10, B11 | The existing identity tests, unchanged, plus the user's walkthrough. Tasks 8 and 11 prove the docs UI reaches the same routes and tokens. |
| B12, B13 | 2, plus 8 for the docs UI's re-authorisation and sign-out |
| B14 | 9, 11 |
| B15 | 8, 9, 11 |
| B16 | 1, 10, 11 |
| B17 | 11 |
| B18 | 8, 11 |
| B19 | 2, 8, 11 |
| B20 | 9 |
| B21 | 9, 11 |
| B22 | 2, 3, 4 |

## Task execution

Follow the [detailed plan](../../../detailed-implementations/workspace-api-openapi-docs/plan.md) for exact interfaces, tables, test code and commands. For every task:

- [ ] Add its named tests. Run the focused command and see it fail for the missing behaviour.
- [ ] Implement the task's interface. Run its focused tests and see them pass.
- [ ] Run `npm test`, `dotnet test runtime/RuntimeKnowledge.Tests --filter Category=OpenApi`, and the Docker-backed suites where available. Rebuild the API, and stage `runtime/openapi/workspace-api.json` whenever it changes.
- [ ] Review `git diff --cached` and `git diff --cached --check`, then commit with the task's message and push.
