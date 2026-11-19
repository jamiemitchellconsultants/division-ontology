# Workspace API OpenAPI docs — detailed implementation plan

**Inputs:** [feature.md](../../features/workspace-api-openapi-docs/feature.md) (vision, behaviours B1–B22 with U1–U17, architecture and traceability), ADRs [0013](../../adr/0013-generate-workspace-api-spec-at-build-time.md), [0014](../../adr/0014-serve-spec-and-swagger-ui-from-the-api.md), [0015](../../adr/0015-docs-ui-sign-in-reuses-web-client.md), [0016](../../adr/0016-check-spec-completeness-statically-and-by-observation.md), [0017](../../adr/0017-serve-the-spec-without-a-server-address.md) and [0018](../../adr/0018-give-refusals-fixed-problem-details-bodies.md), and the [task plan](../../superpowers/plans/workspace-api-openapi-docs/plan.md).

**Build rule:** Start on `feature/workspace-api-openapi-docs/build` only after the plan PR is reviewed and merged. Execute Tasks 1–11 in order, test first:

1. Add the named test file.
2. Run the focused command and see it fail for the stated reason.
3. Implement the stated interface.
4. Run the focused tests, `dotnet test runtime/RuntimeKnowledge.Tests --filter Category=OpenApi` and `npm test`.
5. Rebuild the API. If `runtime/openapi/workspace-api.json` changed, stage it in the same commit.
6. Review `git diff --cached` and `git diff --cached --check`.
7. Commit with the message in the task plan, and push.

Run the Docker-backed suites where `WORKSPACE_TEST_POSTGRES` is set, and they must keep passing. The local SDK must satisfy `runtime/global.json` (10.0.402, `latestPatch`). Never stage `dist/`.

## Decisions this plan settles

These are the questions the architect passed to the developer. Each answer was checked against a throwaway copy of `runtime/` on 19 November 2026, using SDK 10.0.302 and the packages pinned below.

- **The build-time writer and startup.** `Microsoft.Extensions.ApiDescription.Server` runs `ApiProgram.Main` inside `GetDocument.Insider` and starts the host with a no-op server. The current code fails the build with `Section 'Workspace:Identity' not found in configuration`. The cause is `ValidateOnStart()` on `JwtBearerOptions`, which runs `Identity.Configure` in `Host.StartAsync`.
  - **Fix:** register `ValidateOnStart()` only when `BuildTimeGeneration.IsActive` is false. `IsActive` checks that `Assembly.GetEntryAssembly()?.GetName().Name == "GetDocument.Insider"`, which is the detection Microsoft documents.
  - **Result:** with this fix, the build writes the document. Real hosts, `WebApplicationFactory` and `LambdaHosting` still fail fast when identity is misconfigured, and Task 1 adds a test for that.
  - **Nothing else resolves eagerly.** `NpgsqlDataSource`, `Db`, `SearchStore`, `IEmbeddingProvider` and the graph stores are singletons that are created on first use. Minimal-API parameter inference uses `IServiceProviderIsService` and never constructs them. The API registers no hosted services.
- **Declaring error codes.** `ProblemResponses.StatusFor(string code)` becomes the single code-to-status map. Its body is the existing `switch`, moved out of `WriteAsync`, which now calls it.
  - A route declares codes with `ProducesWorkspaceErrors(params string[] codes)`. The method adds `WorkspaceErrorCodes` metadata and calls `ProducesProblem(StatusFor(code))` once per distinct status, so ApiExplorer, the spec and the completeness check all derive statuses from the same map.
  - No route names a status number.
- **Deterministic output.**
  - `info` is fixed: title `Runtime knowledge workspace API`, version `v1`.
  - A last document transformer, `StableOrder`, sorts paths, component schemas, component responses and every operation's responses by ordinal key.
  - The build writes no `servers` entry: the framework adds one only for a live request, or in Development when the server reports addresses, and the build-time server reports none. At request time, `DocsSignIn` removes the request-derived entry (Task 9, ADR 0017).
  - Two builds, and a build with deployment-shaped `Workspace__*` variables and `ASPNETCORE_ENVIRONMENT=Development`, produced byte-identical files. CI repeats that check (Task 10).
- **Image publish.** `-p:OpenApiGenerateDocuments=false` on `dotnet publish` skips generation. The spike confirmed it: publish succeeded and wrote no file. It is added to the API publish in `deploy/workspace/app/Dockerfile`, and also in `deploy/workspace/aws/package.sh`, which publishes the same project for Lambda, so no packaged artefact depends on generation.
- **Required check.** `docs/automation-setup.md` adds `api-contract` to the required status checks sentence (currently line 39). The job has no path filter, so it runs on every PR, as that document requires for required checks.
- **Discovery must not block.** The transformer calls `GetConfigurationAsync(CancellationToken.None).WaitAsync(DocsSignIn.DiscoveryWait, requestAborted)` on the `JwtBearer` configuration manager, with `DiscoveryWait` set to 2 seconds.
  - **Slow or unreachable provider:** a timeout or any exception, other than the caller's own cancellation, serves the spec without `workspaceSignIn`. The fetch carries on in the background, and `ConfigurationManager` caches it for later requests.
  - **Spike result:** with an issuer that accepts connections and never replies, `GET /api/openapi/v1.json` returned 200 in 2.0–2.2 s, without the scheme.
  - **No settings:** the transformer reads no options when `Workspace:Docs:ClientId` or `Workspace:Identity` is missing, or during build-time generation. That means it can never trigger the identity validation error.
- **Swagger UI PKCE against Entra's SPA redirect.**
  - **Configuration:** Swagger UI 10.3.1 (`Swashbuckle.AspNetCore.SwaggerUI`) serves `usePkceWithAuthorizationCodeGrant: true` with only the client ID and scopes, and no secret. Its `oauth2RedirectUrl` resolves from `window.location`, so it is `<origin>/api/docs/oauth2-redirect.html` in every deployment. The `/api/docs` redirect is relative (`Location: docs/index.html`), so it survives CloudFront.
  - **Token redemption:** the redirect page passes the code back to the opening window, which redeems it with a browser `fetch`. That request carries `Origin`, which Entra requires for codes issued to a *Single-page application* redirect URI. Swagger UI drops empty form fields, so no `client_secret` is sent, and a public client accepts that.
  - **Configuration to register:** the docs callback goes on the existing SPA registration as a Single-page application redirect URI, and the scope is `entra_api_scope`.
  - **Not verifiable here:** there is no Entra tenant in this repository's tests. The plan proves the configuration (Tasks 8 and 11), and the live sign-in stays in the user's AWS walkthrough (vision outcome 3).
- **Package versions.**
  - `Microsoft.AspNetCore.OpenApi` and `Microsoft.Extensions.ApiDescription.Server` are 10.0.13, which matches the `aspnet:10.0.13` runtime image and the Blazor packages.
  - `Swashbuckle.AspNetCore.SwaggerUI` is 10.3.1, the latest 10.x. It targets `net8.0`, `net9.0` and `net10.0` and has no package dependencies. 11.x moves to .NET 11.
  - With Central Package Management, `dotnet restore` updated the Api and Tests lock files, and `dotnet restore RuntimeKnowledge.slnx --locked-mode` then passed. `Microsoft.OpenApi` resolves to 2.12.2, transitively.
  - Use namespace `Microsoft.OpenApi` (v2): `JsonNodeExtension`, `OpenApiSecuritySchemeReference(id, document)`, `OpenApiSchemaReference(id, document)` and `OpenApiResponseReference(id, document)`. In 2.x, `IOpenApiPathItem.Operations` is a `Dictionary<HttpMethod, OpenApiOperation>`.

- **Refusal bodies and the docs UI (the architect's assumption to challenge).** `UseStatusCodePages` acts only on a response that has not started, has a status from 400 to 599, and has no `Content-Length` and no `Content-Type`. The hook then writes only for 400, 401 and 403. The spec endpoint and Swagger UI return 200, 301 or 304 with a body, or 404 for a missing asset, so the hook never touches them. Task 2 places it straight after `UseExceptionHandler`, and Task 8 adds the docs UI after it. Task 2 also tests that a 403 that already has a body is left alone, and Task 8 tests that the docs paths are unchanged.
- **Test environment.** No deployment sets `ASPNETCORE_ENVIRONMENT`, so every deployment runs as `Production`. `WebApplicationFactory` defaults to `Development`, where `RouteHandlerOptions.ThrowOnBadRequest` is true. A binding failure would then become an exception and a 500, which no deployment returns. `OpenApiHost` therefore runs the API with `UseEnvironment("Production")`, so its 400 tests see what deployments return. No API code changes for this.
- **Existing tests and the web UI.** No existing test asserts an empty 400, 401 or 403 body. `IdentityTests`, `WorkspaceScenarioTests`, `AwsSimulationTests` and `WorkspaceUiTests` assert statuses only, and they keep passing. The text "Workspace request failed (403)." is only the default message of `WorkspaceApiException` when there is no problem body. No test asserts it. Task 2 adds a `WorkspaceClient` test showing that a 403 now carries the API's title and code (U15), and that a 401 body still leads to "Sign in again".
- **Statuses given before an operation runs.** 404 for an unknown path, 405, 413 and 415 are not operation statuses (B2, ADR 0018). The hook leaves them empty, which Task 2 tests. A 413 is raised while a selected endpoint binds its body, so the conformance recorder also ignores 405, 413 and 415 by status (Task 7).

## Contracts

```csharp
// RuntimeKnowledge.Api (internal unless stated; add <InternalsVisibleTo Include="RuntimeKnowledge.Tests" /> in Task 1)
static class BuildTimeGeneration { bool IsActive { get; } }
static class WorkspaceOpenApi {
    const string DocumentName = "v1", RoleExtension = "x-workspace-role", CodesExtension = "x-workspace-codes", Bearer = "bearer";
    void AddWorkspaceOpenApi(this IServiceCollection services);
    string? RoleFor(IList<object> endpointMetadata);          // null = anonymous
    string ProblemSchemaFor(IEnumerable<string> codes);       // "WorkspaceConflictProblem" if codes has versionConflict, else "WorkspaceProblem"
}
static class ProblemResponses {
    sealed record Refusal(string Code, string Title);
    IReadOnlyDictionary<int, Refusal> Refusals { get; }        // 400, 401 and 403 only; values in Task 2
    Task WriteAsync(HttpContext context); int StatusFor(string code);
    IApplicationBuilder UseRefusalResponses(this IApplicationBuilder app);
}
sealed record WorkspaceErrorCodes(IReadOnlyList<string> Codes);
sealed record RequestExample(object Value);
static class WorkspaceErrors { RouteHandlerBuilder ProducesWorkspaceErrors(this RouteHandlerBuilder builder, params string[] codes); }
public sealed record HealthStatus(string Status);
static class OpenApiExamples { /* values below */ IReadOnlyList<object> All { get; } }
static class DocsUi { const string SpecPath = "/api/openapi/v1.json", RoutePrefix = "api/docs"; void UseWorkspaceDocsUi(this IApplicationBuilder app); }
sealed class DocsSignIn(IConfiguration configuration, IOptionsMonitor<JwtBearerOptions> jwt) : IOpenApiDocumentTransformer {
    const string Scheme = "workspaceSignIn"; static TimeSpan DiscoveryWait { get; set; } = TimeSpan.FromSeconds(2);
}
```

Settings: `Workspace:Docs:ClientId` (the web UI's public client) and `Workspace:Docs:Scopes` (space-separated, optional). Paths: spec `GET /api/openapi/v1.json`, UI `/api/docs` (index `/api/docs/index.html`, callback `/api/docs/oauth2-redirect.html`). Committed file: `runtime/openapi/workspace-api.json`, regenerated by `dotnet build runtime/RuntimeKnowledge.Api`.

Component names: security schemes `bearer` (HTTP bearer, JWT) and `workspaceSignIn` (served only). Schemas `Problem`, `WorkspaceProblem` and `WorkspaceConflictProblem`. Responses `unauthorized`, `forbidden`, `unreadableRequest` and `internalError`. The first three are `WorkspaceProblem` responses whose `x-workspace-codes` are `authenticationRequired`, `roleRequired` and `unreadableRequest`. The served spec never has a `servers` entry (ADR 0017).

## Test conventions

New tests live in `runtime/RuntimeKnowledge.Tests/OpenApi/` and carry `[Trait("Category", "OpenApi")]`. They need neither Docker nor a database: nothing in them calls `/health` or a data handler that reaches the store. Run them with `dotnet test runtime/RuntimeKnowledge.Tests --filter Category=OpenApi`, and run one class with `--filter "Category=OpenApi&FullyQualifiedName~ClassName"`.

`OpenApiHost` (Task 1) wraps `WebApplicationFactory<ApiProgram>`. Its default settings are:

| Setting | Value |
| --- | --- |
| `Workspace:Identity:Provider` | `Keycloak` |
| `Workspace:Identity:Issuer` | `https://issuer.example.test/realms/workspace` |
| `Workspace:Identity:Audience` | `workspace-api` |
| `Workspace:Identity:ClientId` | `workspace-api` |

Callers can merge in extra settings. The host runs with `UseEnvironment("Production")` (see the decision on the test environment). It replaces the `JwtBearer` configuration manager with the supplied one, which defaults to `new StaticConfigurationManager<OpenIdConnectConfiguration>(OpenApiHost.Discovery)`. `Discovery` is that issuer with these endpoints:

- `AuthorizationEndpoint` = `<issuer>/protocol/openid-connect/auth`
- `TokenEndpoint` = `<issuer>/protocol/openid-connect/token`

Task 2 adds an RSA signing key (`KeyId = "openapi-host"`) to `Discovery.SigningKeys`. It also adds `ClientWithToken(string[] roles, bool expired = false, bool wrongSignature = false)`, which signs a token the way `ApiFixture.ClientWithToken` does, for the Keycloak claims (`azp` = `workspace-api`, roles under `resource_access.workspace-api.roles`). No database is needed, because every request it sends is refused or rejected before a handler runs.

## Route table

Task 5 implements this table, Task 5's test pins it, and Tasks 6 and 7 enforce it. The role is the `x-workspace-role` value. Codes are passed to `ProducesWorkspaceErrors`, and statuses come from `StatusFor`. Every protected operation also has 401 and 403, every operation has 500, and every operation with a body, query parameter or route parameter has 400. No operation lists 405, 413 or 415, and none lists 404 except where a code maps to it (B2).

| Method and path | Summary | Tag | Role | Success | Request | Codes |
| --- | --- | --- | --- | --- | --- | --- |
| `GET /health` | Report readiness | Health | none | 200 `HealthStatus` | — | — |
| `GET /api/session` | Show the caller's workspace access | Reading | ReadKnowledgeGraph | 200 `WorkspaceSession` | — | — |
| `GET /api/inventory` | List published entities, relationship types and relationships | Reading | ReadKnowledgeGraph | 200 `InventoryPage` | query `cursor`, `limit` | invalidLimit, invalidCursor, staleCursor, snapshotUnavailable |
| `POST /api/entities/detail` | Get one published entity with its annotations | Reading | ReadKnowledgeGraph | 200 `EntityDetail` | `EntityDetailRequest` | entityNotFound, snapshotUnavailable |
| `POST /api/relationships/detail` | Get one published or runtime relationship | Reading | ReadKnowledgeGraph | 200 `RelationshipDetail` | `RelationshipDetailRequest` | invalidRelationship, relationshipNotFound, snapshotUnavailable |
| `POST /api/search` | Search published entities and annotations by meaning | Reading | ReadKnowledgeGraph | 200 `SearchResult` | `SearchRequest` | invalidSearch, searchUnavailable, snapshotUnavailable |
| `POST /api/traverse` | Traverse relationships from a published entity | Reading | ReadKnowledgeGraph | 200 `TraversalResult` | `TraversalRequest` | invalidTraversal, entityNotFound, snapshotUnavailable |
| `GET /api/history` | List contribution history | Reading | ReadKnowledgeGraph | 200 `HistoryPage` | query `contributionId`, `cursor`, `limit` | invalidLimit, invalidCursor |
| `POST /api/contributions/annotations` | Create an annotation | Contributions | ManageKnowledgeGraph | 201 `ContributionResult` | `CreateAnnotation` | invalidAnnotation, endpointUnavailable, snapshotUnavailable, writesPaused |
| `POST /api/contributions/relationships` | Create a runtime relationship | Contributions | ManageKnowledgeGraph | 201 `ContributionResult` | `CreateRelationship` | endpointUnavailable, invalidMeaning, snapshotUnavailable, writesPaused |
| `PUT /api/contributions/{id}` | Edit an annotation or runtime relationship | Contributions | ManageKnowledgeGraph | 200 `ContributionResult` | `EditContributionRequest` | invalidCommand, invalidAnnotation, endpointUnavailable, invalidMeaning, contributionNotFound, versionConflict, snapshotUnavailable, writesPaused |
| `POST /api/contributions/{id}/remove` | Remove a contribution | Contributions | ManageKnowledgeGraph | 200 `ContributionResult` | `RemoveContributionRequest` | contributionNotFound, versionConflict, snapshotUnavailable, writesPaused |
| `POST /api/contributions/{id}/codifications` | Associate a runtime relationship with its codifying published relationship | Contributions | ManageKnowledgeGraph | 200 `ContributionResult` | `CodificationRequest` | invalidCommand, invalidAssociation, associationExists, contributionNotFound, versionConflict, snapshotUnavailable, writesPaused |

The codes in the table were traced in the store code:

- `GraphQueries`, `SearchStore` and `ContributionStore`, and `WorkspaceRoutes`, which raises `invalidCommand`.
- `ProjectionState.RequireSnapshot`, which raises `snapshotUnavailable`.

Codes that a route cannot reach are left out:

- `actorRequired`: verified claims always supply an actor.
- `invalidEmbedding`: wrapped as `searchUnavailable`.
- The worker-only codes.

The PUT `id` is a `{id:guid}` route constraint, so a malformed ID matches no endpoint and is not one of that operation's statuses.

---

## Task 1 — build-time writer and committed spec

**Files:** `runtime/Directory.Packages.props`, `runtime/RuntimeKnowledge.Api/RuntimeKnowledge.Api.csproj`, `BuildTimeGeneration.cs`, `WorkspaceOpenApi.cs`, `Identity.cs`, `Program.cs`, `runtime/RuntimeKnowledge.Api/packages.lock.json`, `runtime/RuntimeKnowledge.Tests/packages.lock.json`, `runtime/openapi/workspace-api.json`, and in `runtime/RuntimeKnowledge.Tests/OpenApi/`: `RepositoryPaths.cs`, `OpenApiHost.cs` and `CommittedSpecTests.cs`.

**Packages** (`Directory.Packages.props`):

```xml
<PackageVersion Include="Microsoft.AspNetCore.OpenApi" Version="10.0.13" />
<PackageVersion Include="Microsoft.Extensions.ApiDescription.Server" Version="10.0.13" />
<PackageVersion Include="Swashbuckle.AspNetCore.SwaggerUI" Version="10.3.1" />
```

**API project** (`RuntimeKnowledge.Api.csproj`). Add the Swagger UI reference in Task 8, so that no task adds an unused package:

```xml
<PropertyGroup>
  <OpenApiDocumentsDirectory>$(MSBuildProjectDirectory)/../openapi</OpenApiDocumentsDirectory>
  <OpenApiGenerateDocumentsOptions>--file-name workspace-api</OpenApiGenerateDocumentsOptions>
</PropertyGroup>
<ItemGroup>
  <PackageReference Include="Microsoft.AspNetCore.OpenApi" />
  <PackageReference Include="Microsoft.Extensions.ApiDescription.Server" PrivateAssets="all"
                    IncludeAssets="runtime; build; native; contentfiles; analyzers; buildtransitive" />
  <InternalsVisibleTo Include="RuntimeKnowledge.Tests" />
</ItemGroup>
```

**Startup** (`Identity.cs`):

```csharp
var jwt = services.AddOptions<JwtBearerOptions>(bearer).Configure<IConfiguration>(Configure);
if (!BuildTimeGeneration.IsActive) jwt.ValidateOnStart(); // the build-time writer runs Main with no configuration
```

**Mapping** (`Program.cs`):
- `ConfigureServices` calls `services.AddWorkspaceOpenApi()`. In this task it is only `services.AddOpenApi(DocumentName)`.
- `Configure` maps `endpoints.MapOpenApi("/api/openapi/{documentName}.json").AllowAnonymous()` inside `UseEndpoints`, beside `MapWorkspaceRoutes()` and outside the authorised group.

**Test helpers.** `RepositoryPaths.Root` walks up from `AppContext.BaseDirectory` to the folder that contains `runtime/RuntimeKnowledge.slnx`. `RepositoryPaths.Spec` is `runtime/openapi/workspace-api.json` under it. `OpenApiHost` is described under Test conventions, with these members:

| Member | Returns |
| --- | --- |
| `Start(IReadOnlyDictionary<string,string?>? settings = null, IConfigurationManager<OpenIdConnectConfiguration>? discovery = null)` | The host |
| `Client` | `HttpClient` |
| `CreateClient(WebApplicationFactoryClientOptions)` | `HttpClient` |
| `SpecAsync()` | `Task<JsonNode>` |
| `Endpoints()` | Every `RouteEndpoint` from the host's `EndpointDataSource` |
| `CommittedSpec()` (static) | The parsed committed file |

```csharp
[Trait("Category", "OpenApi")]
public sealed class CommittedSpecTests
{
    [Fact]
    public void BuildWritesTheCommittedSpec()
    {
        Assert.True(File.Exists(RepositoryPaths.Spec), "Run dotnet build runtime/RuntimeKnowledge.Api to write the spec.");
        var spec = OpenApiHost.CommittedSpec();
        Assert.StartsWith("3.1", (string)spec["openapi"]!);
        foreach (var path in new[] { "/health", "/api/session", "/api/inventory", "/api/contributions/{id}" })
            Assert.NotNull(spec["paths"]![path]);
    }

    [Fact]
    public void CommittedSpecIsDeploymentNeutral()
    {
        var text = File.ReadAllText(RepositoryPaths.Spec);
        var spec = JsonNode.Parse(text)!;
        Assert.Null(spec["servers"]);
        Assert.DoesNotContain("workspaceSignIn", text);
        Assert.DoesNotContain("\"oauth2\"", text);
        foreach (Match url in Regex.Matches(text, @"https?://[^""\s]+"))
            Assert.Equal("example.test", new Uri(url.Value).Host);
    }

    [Fact]
    public async Task ServedContractWithoutDocsSettingsMatchesTheCommittedFile()
    {
        await using var host = OpenApiHost.Start();
        var served = (JsonObject)await host.SpecAsync();
        served.Remove("servers"); // request-derived until Task 9 removes it; Task 9 deletes this line
        Assert.True(JsonNode.DeepEquals(OpenApiHost.CommittedSpec(), served), "The build and the running API must generate the same contract.");
    }

    [Fact]
    public void ApiStillRefusesToStartWithoutIdentitySettings()
    {
        using var factory = new WebApplicationFactory<ApiProgram>();
        Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
    }
}
```

Run the tests with `dotnet test runtime/RuntimeKnowledge.Tests --filter "Category=OpenApi&FullyQualifiedName~CommittedSpecTests"`. **Before the change**, they fail to compile, because `OpenApiHost` and the spec file do not exist. Once the helpers exist, they fail with a missing file. **After the change**, they pass.

Then check the build itself:

1. Run `dotnet build runtime/RuntimeKnowledge.Api` with no `Workspace__*` variables. It must succeed and write the file.
2. Run this command, which must leave no diff:

```bash
Workspace__Identity__Provider=Keycloak Workspace__Identity__Issuer=https://issuer.example.test/realms/x Workspace__Docs__ClientId=leak ASPNETCORE_ENVIRONMENT=Development dotnet build runtime/RuntimeKnowledge.Api --no-incremental && git diff --exit-code runtime/openapi/
```

3. Run `dotnet restore runtime/RuntimeKnowledge.slnx --locked-mode`. It must pass with the updated locks.

## Task 2 — refusal responses

**Files:** `runtime/RuntimeKnowledge.Api/ProblemResponses.cs`, `Program.cs`. In `runtime/RuntimeKnowledge.Tests/`: `OpenApi/OpenApiHost.cs` (signing key and `ClientWithToken`), `OpenApi/RefusalResponseTests.cs` (new), `AwsSimulationTests.cs`, `IdentityTests.cs`, and `WorkspaceUiTests.cs`.

**`ProblemResponses.Refusals`** holds exactly the B22 values. The titles are fixed copy:

| Status | `Code` | `Title` |
| --- | --- | --- |
| 400 | `unreadableRequest` | `The request body or parameters could not be read.` |
| 401 | `authenticationRequired` | `Sign in to use the workspace API.` |
| 403 | `roleRequired` | `Your account does not have the role this operation needs.` |

**`UseRefusalResponses(app)`** calls `app.UseStatusCodePages(Func<StatusCodeContext, Task>)`:
- If `Refusals` has the response's status, it executes `Results.Problem(statusCode: status, title: refusal.Title, extensions: { ["code"] = refusal.Code })`.
- Otherwise it returns `Task.CompletedTask`, leaving the response empty.

It sets no header, so the challenge's `WWW-Authenticate: Bearer` stays. `Configure` calls `app.UseRefusalResponses()` straight after `UseExceptionHandler` and before `UseRouting`. `JwtBearer`, the policies and the binding rules are unchanged.

```csharp
[Trait("Category", "OpenApi")]
public sealed class RefusalResponseTests
{
    static async Task<(HttpResponseMessage Response, JsonObject? Body)> Send(HttpClient client, HttpMethod method, string path,
        HttpContent? content = null)
    {
        var response = await client.SendAsync(new HttpRequestMessage(method, path) { Content = content });
        var text = await response.Content.ReadAsStringAsync();
        return (response, text.Length == 0 ? null : JsonNode.Parse(text)!.AsObject());
    }
    static void AssertRefusal(HttpResponseMessage response, JsonObject? body, int status)
    {
        var refusal = ProblemResponses.Refusals[status];
        Assert.Equal(status, (int)response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal((status, refusal.Title, refusal.Code), ((int)body!["status"]!, (string)body["title"]!, (string)body["code"]!));
    }
    static StringContent Json(string text) => new(text, Encoding.UTF8, "application/json");

    [Fact] public void RefusalsAreExactlyTheB22Set() =>
        Assert.Equal([(400, "unreadableRequest"), (401, "authenticationRequired"), (403, "roleRequired")],
            ProblemResponses.Refusals.OrderBy(r => r.Key).Select(r => (r.Key, r.Value.Code)));

    [Fact] public async Task EveryTokenFailureGetsTheSame401()
    {
        await using var host = OpenApiHost.Start();
        var bodies = new List<string>();
        foreach (var client in new[] { host.Client, Bearer(host, "not-a-jwt"),
                     host.ClientWithToken(["ReadKnowledgeGraph"], expired: true), host.ClientWithToken(["ReadKnowledgeGraph"], wrongSignature: true) })
        {
            var (response, body) = await Send(client, HttpMethod.Get, "/api/inventory");
            AssertRefusal(response, body, 401);
            Assert.Equal("Bearer", response.Headers.WwwAuthenticate.ToString());
            body!.Remove("traceId");
            bodies.Add(body.ToJsonString());
        }
        Assert.Single(bodies.Distinct());
        static HttpClient Bearer(OpenApiHost host, string token)
        {
            var client = host.CreateClient(new());
            client.DefaultRequestHeaders.Authorization = new("Bearer", token);
            return client;
        }
    }

    [Theory] [InlineData("GET", "/api/inventory", new string[0])] [InlineData("POST", "/api/contributions/annotations", new[] { "ReadKnowledgeGraph" })]
    public async Task AMissingRoleGetsRoleRequired(string method, string path, string[] roles)
    {
        await using var host = OpenApiHost.Start();
        var (response, body) = await Send(host.ClientWithToken(roles), new HttpMethod(method), path,
            method == "POST" ? Json("{}") : null);
        AssertRefusal(response, body, 403);
    }

    [Theory] [InlineData("POST", "/api/search", "{")] [InlineData("GET", "/api/inventory?limit=many", null)]
    public async Task AnUnreadableRequestGetsUnreadableRequest(string method, string path, string? body)
    {
        await using var host = OpenApiHost.Start();
        var (response, problem) = await Send(host.ClientWithToken(["ReadKnowledgeGraph"]), new HttpMethod(method), path,
            body is null ? null : Json(body));
        AssertRefusal(response, problem, 400);
    }

    [Theory]
    [InlineData("POST", "/api/search", "text/plain", 415)]
    [InlineData("GET", "/api/search", null, 405)]
    [InlineData("GET", "/api/no-such-operation", null, 404)]
    public async Task StatusesGivenBeforeAnOperationRunsStayEmpty(string method, string path, string? mediaType, int status)
    {
        await using var host = OpenApiHost.Start();
        var (response, body) = await Send(host.Client, new HttpMethod(method), path,
            mediaType is null ? null : new StringContent("x", Encoding.UTF8, mediaType));
        Assert.Equal(status, (int)response.StatusCode);
        Assert.Null(body);
    }

    [Fact] public async Task AResponseThatAlreadyHasABodyIsLeftAlone()
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        await using var app = builder.Build();
        app.UseRefusalResponses();
        app.MapGet("/own", () => Results.Problem(statusCode: 403, title: "Own title"));
        app.MapGet("/bare", () => Results.StatusCode(403));
        await app.StartAsync();
        var client = app.GetTestClient();
        Assert.Equal("Own title", (string)JsonNode.Parse(await client.GetStringAsync("/own"))!["title"]!);
        Assert.Equal("roleRequired", (string)JsonNode.Parse(await (await client.GetAsync("/bare")).Content.ReadAsStringAsync())!["code"]!);
    }
}
```

**Lambda host.** In `AwsSimulationTests`, add `[Fact, Trait("Category", "OpenApi")] LambdaHostGivesRefusalsTheirBodies`. It builds `LambdaHosting` as `EntraGatewayAndScheduledHandlersShareTheWorkspace` does, with no connection string and the same `StaticConfigurationManager`. Add a private `Request(string method, string path, string? contentType = null, string? body = null)` builder beside `Event`, setting `Version = "2.0"`, `RawPath`, `host`, the `content-type` header and `RequestContext.Http.Method`. Then send these requests through `FunctionHandlerAsync`:

| Request | Expected response |
| --- | --- |
| `GET /api/inventory`, no token | 401, a `www-authenticate` header of `Bearer`, and a body whose `code` is `authenticationRequired` |
| `POST /api/search`, `text/plain` | 415 with an empty body |
| `GET /api/no-such-operation` | 404 with an empty body |

In `EntraGatewayAndScheduledHandlersShareTheWorkspace` (Docker-backed), the existing 401 and 403 assertions also check that the body contains `"authenticationRequired"` and `"roleRequired"`.

**Both providers (Docker-backed).** In `IdentityTests.WorkspaceRolesAreRecheckedOnEveryRequest` and `InvalidTokensAreDeniedBeforeWrites`, which run for Keycloak and Entra, each existing 401 or 403 assertion also reads the body and checks its `code`. For 401, that is `authenticationRequired`, the same for an expired token, the wrong issuer, the wrong audience and a bad signature. For 403, it is `roleRequired`.

**Web UI (U15).** In `WorkspaceUiTests`, add `RefusalsCarryTheApiTitleAndCode`. A stub `HttpMessageHandler` returns each refusal's status with its `Results.Problem`-shaped JSON body. Then:
- For 403, `WorkspaceClient.Session()` throws a `WorkspaceApiException` whose `Message` is the 403 title and whose `Code` is `roleRequired`.
- For 401, the exception's `Status` is `Unauthorized`, so `ApiError` still shows "Sign in again".

No web UI code changes.

**Before the change**, `RefusalResponseTests` fail to compile, because `Refusals`, `UseRefusalResponses` and `ClientWithToken` do not exist. Once they compile, the refusal tests fail on empty bodies. The pre-operation tests already pass, and they guard the change. **After the change**, all of them pass. `dotnet test runtime/RuntimeKnowledge.Tests --filter "FullyQualifiedName~WorkspaceUiTests"` passes. The Docker-backed `IdentityTests` and `AwsSimulationTests` pass where Docker is available.

## Task 3 — document and operation descriptions

**Files:** `runtime/RuntimeKnowledge.Api/WorkspaceOpenApi.cs`, the committed spec, `runtime/RuntimeKnowledge.Tests/OpenApi/DocumentDescriptionTests.cs`.

**Transformers.** `AddWorkspaceOpenApi` registers transformers in this order:

1. Operation transformer `OperationDescriptions`.
2. Document transformer `DocumentDescriptions`.
3. Document transformer `StableOrder`, which must stay last. Task 9 inserts `DocsSignIn` before it.

**`RoleFor(metadata)`:**
- Returns null when `IAllowAnonymous` is present.
- Returns `"ManageKnowledgeGraph"` when an `IAuthorizeData.Policy` is `Identity.ManagePolicy`.
- Returns `"ReadKnowledgeGraph"` when the policy is `Identity.ReadPolicy`.
- Returns null otherwise.

**`OperationDescriptions`:**
- Sets `x-workspace-role` to `new JsonNodeExtension(JsonValue.Create(role ?? "none"))`.
- **Anonymous operations:** sets `Security = []`.
- **Protected operations:** sets `Security = [{ bearer: [] }]`. Adds `"401"` as an `OpenApiResponseReference("unauthorized", context.Document)` and `"403"` as `OpenApiResponseReference("forbidden", …)`.
- **Every operation:** adds `"500"` as `OpenApiResponseReference("internalError", …)`.

**`DocumentDescriptions`:**
- Sets `Info = new OpenApiInfo { Title = "Runtime knowledge workspace API", Version = "v1" }`.
- Adds security scheme `bearer`: `{ Type = Http, Scheme = "bearer", BearerFormat = "JWT", Description = "An access token from the deployment's identity provider, with the workspace API audience." }`.
- Adds schema `Problem`: an object with `type`, `title`, `status`, `detail`, `instance` and `traceId`, and `required` set to `status` and `title`.
- Adds schema `WorkspaceProblem`: `Problem`'s properties plus `code` (string). Required: `status`, `title` and `code`.
- Adds these component responses. A refusal response's description is its `Refusals` title, then `" Code: "` and its code. It carries `x-workspace-codes` as a one-element JSON array:

| Response | Description | Content | Extra |
| --- | --- | --- | --- |
| `unauthorized` | "Sign in to use the workspace API. Code: authenticationRequired" | `application/problem+json` → `WorkspaceProblem` | Header `WWW-Authenticate` (string); `x-workspace-codes` `["authenticationRequired"]` |
| `forbidden` | "Your account does not have the role this operation needs. Code: roleRequired" | `application/problem+json` → `WorkspaceProblem` | `x-workspace-codes` `["roleRequired"]` |
| `internalError` | "The workspace could not complete the request." | `application/problem+json` → `Problem` | — |

Build the refusal components from `ProblemResponses.Refusals`, so that the copy lives in one place.

**`StableOrder`** rebuilds `Paths` (as a new `OpenApiPaths`), `Components.Schemas`, `Components.Responses` and each operation's `Responses` in ordinal key order.

```csharp
[Trait("Category", "OpenApi")]
public sealed class DocumentDescriptionTests
{
    static async Task<JsonNode> Spec() { await using var host = OpenApiHost.Start(); return await host.SpecAsync(); }
    static IEnumerable<(string Path, string Method, JsonNode Op)> Operations(JsonNode spec) =>
        spec["paths"]!.AsObject().SelectMany(p => p.Value!.AsObject().Select(m => (p.Key, m.Key, m.Value!)));

    [Fact] public async Task InfoIsFixed()
    {
        var info = (await Spec())["info"]!;
        Assert.Equal("Runtime knowledge workspace API", (string)info["title"]!);
        Assert.Equal("v1", (string)info["version"]!);
        Assert.Equal(2, info.AsObject().Count);
    }

    [Fact] public async Task ProtectedOperationsNeedABearerTokenAndNameTheirRole()
    {
        foreach (var (path, _, op) in Operations(await Spec()).Where(o => o.Path.StartsWith("/api/")))
        {
            var expected = path.StartsWith("/api/contributions/") ? "ManageKnowledgeGraph" : "ReadKnowledgeGraph";
            Assert.Equal(expected, (string)op["x-workspace-role"]!);
            Assert.Equal("""[{"bearer":[]}]""", op["security"]!.ToJsonString());
            Assert.Equal("#/components/responses/unauthorized", (string)op["responses"]!["401"]!["$ref"]!);
            Assert.Equal("#/components/responses/forbidden", (string)op["responses"]!["403"]!["$ref"]!);
        }
    }

    [Fact] public async Task HealthIsAnonymous()
    {
        var health = (await Spec())["paths"]!["/health"]!["get"]!;
        Assert.Equal("none", (string)health["x-workspace-role"]!);
        Assert.Equal("[]", health["security"]!.ToJsonString());
        Assert.Null(health["responses"]!["401"]);
    }

    [Fact] public async Task EveryOperationDescribesTheGeneric500Once()
    {
        var spec = await Spec();
        Assert.All(Operations(spec), o => Assert.Equal("#/components/responses/internalError", (string)o.Op["responses"]!["500"]!["$ref"]!));
        Assert.Equal("#/components/schemas/Problem",
            (string)spec["components"]!["responses"]!["internalError"]!["content"]!["application/problem+json"]!["schema"]!["$ref"]!);
    }

    [Theory] [InlineData("unauthorized", 401)] [InlineData("forbidden", 403)]
    public async Task RefusalsAreWorkspaceProblemsWithTheirCode(string name, int status)
    {
        var spec = await Spec();
        var response = spec["components"]!["responses"]![name]!;
        var refusal = ProblemResponses.Refusals[status];
        Assert.Equal($"{refusal.Title} Code: {refusal.Code}", (string)response["description"]!);
        Assert.Equal($"[\"{refusal.Code}\"]", response["x-workspace-codes"]!.ToJsonString());
        Assert.Equal("#/components/schemas/WorkspaceProblem",
            (string)response["content"]!["application/problem+json"]!["schema"]!["$ref"]!);
        Assert.Equal(["code", "status", "title"],
            spec["components"]!["schemas"]!["WorkspaceProblem"]!["required"]!.AsArray().Select(n => (string)n!).Order(StringComparer.Ordinal));
    }

    [Fact] public async Task UnauthorizedNamesTheChallengeHeader() =>
        Assert.NotNull((await Spec())["components"]!["responses"]!["unauthorized"]!["headers"]!["WWW-Authenticate"]);

    [Fact] public async Task AServed401BodyFitsTheDescribedSchema()
    {
        await using var host = OpenApiHost.Start();
        var schema = (await host.SpecAsync())["components"]!["schemas"]!["WorkspaceProblem"]!;
        var body = JsonNode.Parse(await (await host.Client.GetAsync("/api/session")).Content.ReadAsStringAsync())!.AsObject();
        var properties = schema["properties"]!.AsObject().Select(p => p.Key).ToHashSet();
        Assert.All(body.Select(p => p.Key), key => Assert.Contains(key, properties));
        Assert.All(schema["required"]!.AsArray(), key => Assert.True(body.ContainsKey((string)key!)));
    }

    [Fact] public async Task BearerSchemeIsAJwt()
    {
        var bearer = (await Spec())["components"]!["securitySchemes"]!["bearer"]!;
        Assert.Equal(("http", "bearer", "JWT"), ((string)bearer["type"]!, (string)bearer["scheme"]!, (string)bearer["bearerFormat"]!));
    }

    [Fact] public async Task KeysAreInOrdinalOrder()
    {
        var spec = await Spec();
        AssertOrdered(spec["paths"]!);
        AssertOrdered(spec["components"]!["schemas"]!);
        AssertOrdered(spec["components"]!["responses"]!);
        foreach (var (_, _, op) in Operations(spec)) AssertOrdered(op["responses"]!);
        static void AssertOrdered(JsonNode node)
        {
            var keys = node.AsObject().Select(p => p.Key).ToArray();
            Assert.Equal(keys.Order(StringComparer.Ordinal), keys);
        }
    }
}
```

**Before the change**, the tests fail because the role, the security entries and the custom info are absent. **After the change**, they pass, and so does `CommittedSpecTests` once the file is rebuilt.

## Task 4 — workspace error codes through one map

**Files:** `runtime/RuntimeKnowledge.Api/ProblemResponses.cs`, `WorkspaceErrors.cs`, `WorkspaceOpenApi.cs`, `WorkspaceRoutes.cs` (the inventory route only), the committed spec, and in `runtime/RuntimeKnowledge.Tests/OpenApi/`: `ProblemResponseTests.cs` and `WorkspaceErrorsTests.cs`.

**`ProblemResponses`:** `StatusFor(code)` is today's `switch` from `WriteAsync`, unchanged, and `WriteAsync` calls it.

**`ProducesWorkspaceErrors(codes)`:**
- Throws `ArgumentException` for any code that is a `Refusals` code. `StatusFor` maps unknown codes to 422, so a refusal code declared here would be described with the wrong status. Refusals are added by the transformer, never declared.
- Adds `new WorkspaceErrorCodes(codes)`.
- Calls `builder.ProducesProblem(status)` for each distinct `StatusFor(code)`, in ascending order.

**`OperationDescriptions`** gains three steps:

1. Group the operation's declared codes by `StatusFor`. For each status, set `Responses[status]` to an `OpenApiResponse` with:
   - **Description:** the reason phrase plus `" Codes: "` and the codes, ordinal-sorted and comma-separated.
   - **Content:** `application/problem+json`, with schema reference `ProblemSchemaFor(group)`.
   - **Extension:** `x-workspace-codes`, a JSON array of the sorted codes.
2. If the operation has a `RequestBody` or any `Parameters`, set `Responses["400"]` to `OpenApiResponseReference("unreadableRequest", …)`.
3. `DocumentDescriptions` adds the following components:

| Component | Definition |
| --- | --- |
| Schema `WorkspaceConflictProblem` | As `WorkspaceProblem` (Task 3), plus `current` referencing `RuntimeContribution`. Same required fields. |
| Response `unreadableRequest` | Built from `Refusals[400]` as Task 3 builds `unauthorized`. Description: "The request body or parameters could not be read. Code: unreadableRequest". Content: `application/problem+json` → `WorkspaceProblem`. `x-workspace-codes`: `["unreadableRequest"]`. |

In this task, the inventory route alone declares `ProducesWorkspaceErrors("invalidLimit", "invalidCursor", "staleCursor", "snapshotUnavailable")`. Task 5 declares the rest.

```csharp
[Trait("Category", "OpenApi")]
public sealed class ProblemResponseTests
{
    [Theory]
    [InlineData("entityNotFound", 404)] [InlineData("relationshipNotFound", 404)] [InlineData("contributionNotFound", 404)]
    [InlineData("versionConflict", 409)] [InlineData("staleCursor", 409)]
    [InlineData("snapshotUnavailable", 503)] [InlineData("writesPaused", 503)] [InlineData("searchUnavailable", 503)]
    [InlineData("invalidLimit", 422)] [InlineData("invalidCommand", 422)] [InlineData("invalidBundle", 422)]
    public void StatusForKeepsTheExistingMap(string code, int status) => Assert.Equal(status, ProblemResponses.StatusFor(code));

    [Theory] [InlineData("entityNotFound")] [InlineData("versionConflict")] [InlineData("writesPaused")] [InlineData("invalidMeaning")]
    public async Task WriteAsyncUsesTheSameMap(string code)
    {
        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddLogging().AddProblemDetails().BuildServiceProvider(),
            Response = { Body = new MemoryStream() }
        };
        context.Features.Set<IExceptionHandlerFeature>(new ExceptionHandlerFeature { Error = new WorkspaceException(code, "Example.") });
        await ProblemResponses.WriteAsync(context);
        Assert.Equal(ProblemResponses.StatusFor(code), context.Response.StatusCode);
        context.Response.Body.Position = 0;
        Assert.Equal(code, (string)JsonNode.Parse(context.Response.Body)!["code"]!);
    }
}

[Trait("Category", "OpenApi")]
public sealed class WorkspaceErrorsTests
{
    [Fact] public void DeclaringCodesAddsOneProblemResponsePerStatus()
    {
        var app = WebApplication.CreateSlimBuilder().Build();
        app.MapGet("/x", () => "x").ProducesWorkspaceErrors("staleCursor", "entityNotFound", "versionConflict");
        var endpoint = ((IEndpointRouteBuilder)app).DataSources.SelectMany(s => s.Endpoints).Single();
        Assert.Equal(["staleCursor", "entityNotFound", "versionConflict"], endpoint.Metadata.GetRequiredMetadata<WorkspaceErrorCodes>().Codes);
        var problems = endpoint.Metadata.OfType<IProducesResponseTypeMetadata>().Where(m => m.ContentTypes.Contains("application/problem+json"));
        Assert.Equal([404, 409], problems.Select(m => m.StatusCode).Order());
    }

    [Fact] public void ConflictsUseTheSchemaWithCurrent()
    {
        Assert.Equal("WorkspaceConflictProblem", WorkspaceOpenApi.ProblemSchemaFor(["versionConflict", "staleCursor"]));
        Assert.Equal("WorkspaceProblem", WorkspaceOpenApi.ProblemSchemaFor(["staleCursor"]));
    }

    [Fact] public async Task InventoryDescribesItsCodesAsProblemDetails()
    {
        await using var host = OpenApiHost.Start();
        var spec = await host.SpecAsync();
        var responses = spec["paths"]!["/api/inventory"]!["get"]!["responses"]!;
        foreach (var (status, codes) in new[] { ("409", "[\"staleCursor\"]"), ("422", "[\"invalidCursor\",\"invalidLimit\"]"), ("503", "[\"snapshotUnavailable\"]") })
        {
            Assert.Equal(codes, responses[status]!["x-workspace-codes"]!.ToJsonString());
            Assert.Equal("#/components/schemas/WorkspaceProblem",
                (string)responses[status]!["content"]!["application/problem+json"]!["schema"]!["$ref"]!);
        }
        Assert.Equal("#/components/responses/unreadableRequest", (string)responses["400"]!["$ref"]!);
        var unreadable = spec["components"]!["responses"]!["unreadableRequest"]!;
        Assert.Equal("[\"unreadableRequest\"]", unreadable["x-workspace-codes"]!.ToJsonString());
        Assert.Equal("#/components/schemas/WorkspaceProblem",
            (string)unreadable["content"]!["application/problem+json"]!["schema"]!["$ref"]!);
        var conflict = spec["components"]!["schemas"]!["WorkspaceConflictProblem"]!;
        Assert.Equal("#/components/schemas/RuntimeContribution", (string)conflict["properties"]!["current"]!["$ref"]!);
        Assert.Contains("code", conflict["required"]!.AsArray().Select(n => (string)n!));
    }

    [Theory] [InlineData("unreadableRequest")] [InlineData("authenticationRequired")] [InlineData("roleRequired")]
    public void RefusalCodesCannotBeDeclared(string code)
    {
        var app = WebApplication.CreateSlimBuilder().Build();
        Assert.Throws<ArgumentException>(() => app.MapGet("/x", () => "x").ProducesWorkspaceErrors(code));
    }
}
```

**Before the change**, the tests fail to compile: `StatusFor`, `ProducesWorkspaceErrors` and `ProblemSchemaFor` do not exist. **After the change**, they pass. The existing Docker-backed problem-details assertions in `ContributionTests` and `WorkspaceScenarioTests` must still pass unchanged.

## Task 5 — describe every route

**Files:** `runtime/RuntimeKnowledge.Api/WorkspaceRoutes.cs`, `Program.cs` (`/health`), `WorkspaceErrors.cs` (`RequestExample`), `OpenApiExamples.cs`, the committed spec, and in `runtime/RuntimeKnowledge.Tests/OpenApi/`: `EndpointDescriptionTests.cs` and `ExampleTests.cs`.

**Routes.** Apply the route table. For each route, chain:

- `.WithSummary(summary)`
- `.WithTags(tag)`
- `.ProducesWorkspaceErrors(codes…)` wherever the table lists codes.

On the two create routes, add `.Produces<ContributionResult>(StatusCodes.Status201Created)`. Their `Results.Created(...)` calls are unchanged.

**Health.** `/health` returns `TypedResults.Ok(new HealthStatus("ready"))`. The JSON is unchanged.

**Examples.** Attach `.WithMetadata(new RequestExample(OpenApiExamples.X))` to these routes:

| Route | Example |
| --- | --- |
| Create annotation | `CreateAnnotation` |
| Create relationship | `CreateRelationship` |
| Search | `Search` |
| Traverse | `Traversal` |

`OperationDescriptions` writes each one as `RequestBody.Content["application/json"].Example = JsonSerializer.SerializeToNode(value, WorkspaceJson.Options)`.

`OpenApiExamples` holds invented values only (B4):

```csharp
public static readonly PublishedKey Order = new("example-sales", "concept", "https://example.test/sales#Order");
public static readonly PublishedKey Invoice = new("example-billing", "concept", "https://example.test/billing#Invoice");
public static readonly CreateAnnotation CreateAnnotation = new(Order, "businessExplanation", "Example: an order is accepted once payment is authorised.");
public static readonly CreateRelationship CreateRelationship = new(Order, Invoice, new RelationshipMeaning(null, "is billed by", "Example: each accepted order is billed by one invoice."));
public static readonly SearchRequest Search = new("example order lifecycle", 10);
public static readonly TraversalRequest Traversal = new(Order, "outgoing", 2);
public static IReadOnlyList<object> All { get; } = [CreateAnnotation, CreateRelationship, Search, Traversal];
```

```csharp
[Trait("Category", "OpenApi")]
public sealed class EndpointDescriptionTests
{
    public static TheoryData<string, string, string, string, string, string?, string[]> Routes => new()
    {
        // method, path, summary, tag, success "status schema", request schema (null if none), codes
        { "get", "/health", "Report readiness", "Health", "200 HealthStatus", null, [] },
        { "get", "/api/session", "Show the caller's workspace access", "Reading", "200 WorkspaceSession", null, [] },
        // … one row per line of the route table, with its exact values …
    };

    [Theory, MemberData(nameof(Routes))]
    public async Task OperationMatchesTheRouteTable(string method, string path, string summary, string tag,
        string success, string? request, string[] codes)
    {
        await using var host = OpenApiHost.Start();
        var op = (await host.SpecAsync())["paths"]![path]![method]!;
        Assert.Equal(summary, (string)op["summary"]!);
        Assert.Equal($"[\"{tag}\"]", op["tags"]!.ToJsonString());
        var (status, schema) = (success.Split(' ')[0], success.Split(' ')[1]);
        Assert.Equal($"#/components/schemas/{schema}", (string)op["responses"]![status]!["content"]!["application/json"]!["schema"]!["$ref"]!);
        Assert.Equal(request is null ? null : $"#/components/schemas/{request}",
            (string?)op["requestBody"]?["content"]?["application/json"]?["schema"]?["$ref"]);
        var expected = codes.Select(c => ProblemResponses.StatusFor(c).ToString())
            .Append(status).Append("500")
            .Concat(path.StartsWith("/api/") ? ["401", "403"] : [])
            .Concat(op["requestBody"] is not null || op["parameters"] is not null ? ["400"] : [])
            .Distinct().Order(StringComparer.Ordinal);
        Assert.Equal(expected, op["responses"]!.AsObject().Select(r => r.Key));
        Assert.Equal(codes.Order(StringComparer.Ordinal),
            op["responses"]!.AsObject().SelectMany(r => r.Value!["x-workspace-codes"]?.AsArray().Select(n => (string)n!) ?? []).Order(StringComparer.Ordinal));
    }

    [Fact] public async Task TheTableCoversEveryOperation()
    {
        await using var host = OpenApiHost.Start();
        var operations = (await host.SpecAsync())["paths"]!.AsObject().Sum(p => p.Value!.AsObject().Count);
        Assert.Equal(Routes.Count, operations);
    }
}

[Trait("Category", "OpenApi")]
public sealed class ExampleTests
{
    [Fact] public async Task EveryExampleIsAnInventedValue()
    {
        var spec = OpenApiHost.CommittedSpec();
        var allowed = OpenApiExamples.All.Select(v => JsonSerializer.SerializeToNode(v, WorkspaceJson.Options)).ToArray();
        var examples = Descendants(spec).Where(p => p.Key is "example").Select(p => p.Value).ToArray();
        Assert.Equal(4, examples.Length);
        Assert.All(examples, e => Assert.Contains(allowed, a => JsonNode.DeepEquals(a, e)));
        Assert.DoesNotContain(Descendants(spec), p => p.Key is "examples");
        foreach (var text in examples.SelectMany(Strings))
        {
            if (Uri.TryCreate(text, UriKind.Absolute, out var uri)) Assert.Equal("example.test", uri.Host);
        }
        Assert.All(examples.SelectMany(e => Descendants(e!)).Where(p => p.Key == "sourceId"), p => Assert.StartsWith("example-", (string)p.Value!));
    }
    // Descendants(JsonNode) yields every (propertyName, value) pair recursively; Strings(JsonNode) yields every string value.
}
```

**Before the change**, `OperationMatchesTheRouteTable` fails on the missing summaries and statuses, and `EveryExampleIsAnInventedValue` finds no examples. **After the change**, both pass. The two create routes now show `201` and no bare `200`.

## Task 6 — static completeness check

**Files:** `runtime/RuntimeKnowledge.Tests/OpenApi/SpecCompleteness.cs`, `CompletenessTests.cs`.

`SpecCompleteness.Normalise(pattern)` strips route constraints, defaults and optional markers, so that `/api/contributions/{id:guid}` becomes `/api/contributions/{id}`. It ensures a leading `/`.

`Find(endpoints, spec)` considers each `RouteEndpoint` with `HttpMethodMetadata`, skipping any whose `IExcludeFromDescriptionMetadata.ExcludeFromDescription` is true (the spec endpoint itself). For each one it reports `"<METHOD> <path>: <gap>"`:

| Gap reported | When |
| --- | --- |
| `not in the spec` | The operation is missing from the spec |
| `no summary` | The operation has no summary |
| `no x-workspace-role` | The role extension is missing |
| `no request schema` | The endpoint has `IAcceptsMetadata` with a `RequestType`, but the spec has no `requestBody` schema |
| `no success response schema` | No 2xx response has a content schema |
| `no 401 response`, `no 403 response` | The endpoint is protected and the response is missing |
| `no 500 response` | The generic 500 is missing |
| `no 400 response` | The endpoint has a body or parameters, and 400 is missing |
| `no <status> response for <code>` | A declared code's status is missing |

It also reports `in the spec but not mapped` for every spec operation that no endpoint produces. The list is sorted ordinally.

```csharp
[Trait("Category", "OpenApi")]
public sealed class CompletenessTests
{
    static RouteEndpoint Endpoint(string method, string pattern, params object[] metadata) =>
        new(_ => Task.CompletedTask, RoutePatternFactory.Parse(pattern), 0,
            new EndpointMetadataCollection([new HttpMethodMetadata([method]), .. metadata]), pattern);
    static JsonNode Spec(string json) => JsonNode.Parse(json)!;
    const string Complete = """
        {"paths":{"/api/x/{id}":{"get":{"summary":"X","x-workspace-role":"ReadKnowledgeGraph",
          "parameters":[{"name":"id","in":"path"}],
          "responses":{"200":{"content":{"application/json":{"schema":{"$ref":"#/components/schemas/X"}}}},
            "400":{},"401":{},"403":{},"404":{},"500":{}}}}}}
        """;
    static readonly object[] Protected = [new AuthorizeAttribute(Identity.ReadPolicy), new WorkspaceErrorCodes(["entityNotFound"])];

    [Fact] public void ACompleteOperationHasNoGaps() =>
        Assert.Empty(SpecCompleteness.Find([Endpoint("GET", "/api/x/{id:guid}", Protected)], Spec(Complete)));

    [Fact] public void AnUnmappedSpecOperationAndAMissingOperationAreNamed()
    {
        var gaps = SpecCompleteness.Find([Endpoint("POST", "/api/y", Protected)], Spec(Complete));
        Assert.Equal(["GET /api/x/{id}: in the spec but not mapped", "POST /api/y: not in the spec"], gaps);
    }

    [Fact] public void MissingSummaryRoleAndDeclaredStatusAreNamed()
    {
        var spec = Spec(Complete);
        var op = spec["paths"]!["/api/x/{id}"]!["get"]!.AsObject();
        op.Remove("summary"); op.Remove("x-workspace-role"); op["responses"]!.AsObject().Remove("404");
        Assert.Equal(["GET /api/x/{id}: no 404 response for entityNotFound", "GET /api/x/{id}: no summary", "GET /api/x/{id}: no x-workspace-role"],
            SpecCompleteness.Find([Endpoint("GET", "/api/x/{id:guid}", Protected)], spec));
    }

    [Fact] public void MissingAuthResponsesAreNamedForProtectedRoutes()
    {
        var spec = Spec(Complete);
        spec["paths"]!["/api/x/{id}"]!["get"]!["responses"]!.AsObject().Remove("403");
        Assert.Equal(["GET /api/x/{id}: no 403 response"], SpecCompleteness.Find([Endpoint("GET", "/api/x/{id:guid}", Protected)], spec));
    }

    [Fact] public async Task TheRunningApiIsFullyDescribed()
    {
        await using var host = OpenApiHost.Start();
        Assert.Empty(SpecCompleteness.Find(host.Endpoints(), await host.SpecAsync()));
    }

    [Fact] public async Task TheCommittedSpecDescribesEveryMappedRoute()
    {
        await using var host = OpenApiHost.Start();
        Assert.Empty(SpecCompleteness.Find(host.Endpoints(), OpenApiHost.CommittedSpec()));
    }
}
```

**Before the change**, the tests fail to compile because `SpecCompleteness` does not exist. **After the change**, they pass. To confirm the check bites, temporarily remove one `.WithSummary` and see `TheRunningApiIsFullyDescribed` name that operation, then restore it. Do not commit that change.

## Task 7 — response conformance hook

**Files:** `runtime/RuntimeKnowledge.Tests/OpenApi/ResponseConformance.cs`, `ResponseConformanceTests.cs`, `runtime/RuntimeKnowledge.Tests/ApiFixture.cs`.

- **`ObservedResponse(string Method, string Route, int Status)`** is a record.
- **`ResponseConformance.TransportStatuses`** is `[405, 413, 415]`. These are the framework's own responses before an operation runs (B2, ADR 0018). A 413 is raised while a selected endpoint reads its body, so the recorder alone cannot tell it apart.
- **`ResponseConformance.Undeclared(observed, spec)`** ignores observations with a `TransportStatuses` status. It returns the distinct, ordinal-sorted lines `"<METHOD> <route> <status>"` for each other observation whose path and method has no response with that status key. An observed route that is missing from the spec is reported as `"<METHOD> <route>: not in the spec"`.
- **`ResponseConformance.Recorder`** is an `IStartupFilter` that wraps the whole pipeline with a middleware:
  1. Await `next`.
  2. Take `context.GetEndpoint() ?? context.Features.Get<IExceptionHandlerFeature>()?.Endpoint`. The exception handler clears the endpoint before it writes the problem response, which is why the fallback is needed.
  3. If that endpoint is a `RouteEndpoint` not excluded from description, add `new ObservedResponse(context.Request.Method, SpecCompleteness.Normalise(e.RoutePattern.RawText!), context.Response.StatusCode)` to a `ConcurrentBag`.
- **`ApiFixture`:**
  - Registers `services.AddSingleton<IStartupFilter>(new ResponseConformance.Recorder(_observed))` in `ConfigureTestServices`.
  - In `DisposeAsync`, before the factory is disposed, fetches `/api/openapi/v1.json` with `CreateClient()` and computes `Undeclared`. It disposes everything in `finally`, then throws `new UndeclaredResponseException(lines)`, whose message is `"Responses not declared in the spec:\n" + string.Join('\n', lines)`, when the list is non-empty.

```csharp
[Trait("Category", "OpenApi")]
public sealed class ResponseConformanceTests
{
    static readonly JsonNode Spec = JsonNode.Parse("""{"paths":{"/api/search":{"post":{"responses":{"200":{},"401":{},"503":{}}}}}}""")!;

    [Fact] public void DeclaredStatusesPass() =>
        Assert.Empty(ResponseConformance.Undeclared([new("POST", "/api/search", 200), new("POST", "/api/search", 503)], Spec));

    [Fact] public void AnUndeclaredStatusIsNamed() =>
        Assert.Equal(["POST /api/search 422"], ResponseConformance.Undeclared([new("POST", "/api/search", 422), new("POST", "/api/search", 422)], Spec));

    [Fact] public void AnUnknownRouteIsNamed() =>
        Assert.Equal(["GET /api/other: not in the spec"], ResponseConformance.Undeclared([new("GET", "/api/other", 200)], Spec));

    [Fact] public void TransportStatusesAreNotOperationStatuses() =>
        Assert.Empty(ResponseConformance.Undeclared([new("POST", "/api/search", 405), new("POST", "/api/search", 413), new("POST", "/api/search", 415)], Spec));

    [Fact] public async Task TheRecorderSeesRefusalsWrittenBeforeTheHandler()
    {
        var observed = new ConcurrentBag<ObservedResponse>();
        await using var host = OpenApiHost.Start(services: s => s.AddSingleton<IStartupFilter>(new ResponseConformance.Recorder(observed)));
        // An anonymous request to a protected route: authentication writes the 401 and Task 2's hook its body. The endpoint is still set.
        await host.Client.GetAsync("/api/inventory?limit=0");
        await host.Client.GetAsync("/api/no-such-operation");
        Assert.Contains(new ObservedResponse("GET", "/api/inventory", 401), observed);
        Assert.DoesNotContain(observed, o => o.Status == 404); // no endpoint was selected
    }
}
```

`OpenApiHost.Start` gains an optional `Action<IServiceCollection>? services` parameter for this test.

The exception-handler path is exercised by the Docker-backed suites: `ContributionTests` with `versionConflict`, and the 404 and 422 cases in `WorkspaceScenarioTests`. Run them with `dotnet test runtime/RuntimeKnowledge.Tests --filter "FullyQualifiedName~ContributionTests|FullyQualifiedName~IdentityTests|FullyQualifiedName~WorkspaceScenarioTests"` where Docker is available.

- **Before the change**, the conformance tests fail to compile.
- **After the change**, they pass.
- **The Docker suites** must still pass. If one fails with `Responses not declared in the spec`, it is a real spec gap: fix the route table and the declaration, not the hook.

## Task 8 — anonymous spec and Swagger UI

**Files:** `runtime/RuntimeKnowledge.Api/RuntimeKnowledge.Api.csproj` (add `<PackageReference Include="Swashbuckle.AspNetCore.SwaggerUI" />`), `DocsUi.cs`, `Program.cs`, `runtime/RuntimeKnowledge.Tests/OpenApi/DocsEndpointTests.cs`, `runtime/RuntimeKnowledge.Tests/AwsSimulationTests.cs`, and both lock files.

**Middleware placement.** `Configure` calls `app.UseWorkspaceDocsUi()` after `UseRefusalResponses` (Task 2) and before `UseRouting`. The UI is middleware and never passes through authorisation. The refusal hook sits outside it, but the UI never returns 400, 401 or 403, and `DocsPathsAreUntouchedByTheRefusalHook` proves the hook leaves its responses alone.

**`UseWorkspaceDocsUi`** calls `app.UseSwaggerUI(options => …)` with:

| Option | Value |
| --- | --- |
| `RoutePrefix` | `"api/docs"` |
| `DocumentTitle` | `"Runtime knowledge workspace API"` |
| `SwaggerEndpoint` | `(SpecPath, "v1")` |

When `Workspace:Docs:ClientId` is non-empty, it also calls:
- `OAuthClientId(clientId)`
- `OAuthUsePkce()`
- `OAuthScopes(scopes)`, where the scopes are split on spaces.

It never calls `OAuthClientSecret`.

```csharp
[Trait("Category", "OpenApi")]
public sealed class DocsEndpointTests
{
    static readonly Dictionary<string, string?> Docs = new() { ["Workspace:Docs:ClientId"] = "workspace-spa", ["Workspace:Docs:Scopes"] = "openid profile" };

    [Fact] public async Task DocsRedirectStaysRelative()
    {
        await using var host = OpenApiHost.Start(Docs);
        var response = await host.CreateClient(new() { AllowAutoRedirect = false }).GetAsync("/api/docs");
        Assert.Equal(HttpStatusCode.MovedPermanently, response.StatusCode);
        Assert.Equal("docs/index.html", response.Headers.Location!.OriginalString);
    }

    [Theory] [InlineData("/api/docs/index.html", "text/html")] [InlineData("/api/docs/oauth2-redirect.html", "text/html")] [InlineData("/api/openapi/v1.json", "application/json")]
    public async Task DocsAndSpecNeedNoSignIn(string path, string mediaType)
    {
        await using var host = OpenApiHost.Start(Docs);
        var response = await host.Client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(mediaType, response.Content.Headers.ContentType!.MediaType);
    }

    [Fact] public async Task UiSignsInWithPkceAndTheWebClient()
    {
        await using var host = OpenApiHost.Start(Docs);
        var script = await host.Client.GetStringAsync("/api/docs/index.js");
        Assert.Contains("\"usePkceWithAuthorizationCodeGrant\":true", script);
        Assert.Contains("\"clientId\":\"workspace-spa\"", script);
        Assert.Contains("\"scopes\":[\"openid\",\"profile\"]", script);
        Assert.Contains("\"url\":\"/api/openapi/v1.json\"", script);
        Assert.DoesNotContain("clientSecret", script);
    }

    [Fact] public async Task WithoutDocsSettingsTheUiHasNoSignIn()
    {
        await using var host = OpenApiHost.Start();
        Assert.DoesNotContain("\"clientId\"", await host.Client.GetStringAsync("/api/docs/index.js"));
    }

    [Theory] [InlineData("GET", "/api/inventory")] [InlineData("GET", "/api/session")] [InlineData("POST", "/api/contributions/annotations")]
    public async Task DataStillNeedsAToken(string method, string path)
    {
        await using var host = OpenApiHost.Start(Docs);
        var response = await host.Client.SendAsync(new HttpRequestMessage(new HttpMethod(method), path) { Content = JsonContent.Create(new { }) });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("authenticationRequired", (string)JsonNode.Parse(await response.Content.ReadAsStringAsync())!["code"]!);
    }

    [Fact] public async Task DocsPathsAreUntouchedByTheRefusalHook()
    {
        await using var host = OpenApiHost.Start(Docs);
        var missing = await host.Client.GetAsync("/api/docs/no-such-asset.js");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Empty(await missing.Content.ReadAsStringAsync());
        Assert.StartsWith("<!", (await host.Client.GetStringAsync("/api/docs/index.html")).TrimStart());
    }
}
```

**Lambda host test.** In `AwsSimulationTests`, add `[Fact, Trait("Category", "OpenApi")] LambdaHostServesDocsAnonymously`. It builds `LambdaHosting` as `EntraGatewayAndScheduledHandlersShareTheWorkspace` does, with no connection string and the same `StaticConfigurationManager`, then sends GETs through `FunctionHandlerAsync`:

| Request | Expected response |
| --- | --- |
| `/api/docs` | 301, with `Location` equal to `docs/index.html` |
| `/api/docs/index.html` | 200 |
| `/api/openapi/v1.json` | 200, with a JSON body whose `openapi` starts with `3.1` |
| `/api/inventory` with no token | 401, with code `authenticationRequired` |

Use the `Request` builder that Task 2 added.

**Before the change**, these tests return 404 for `/api/docs`. **After the change**, they pass.

## Task 9 — no server address, and sign-in details in the served spec

**Files:** `runtime/RuntimeKnowledge.Api/DocsSignIn.cs`, `WorkspaceOpenApi.cs` (register `DocsSignIn` before `StableOrder`), and in `runtime/RuntimeKnowledge.Tests/OpenApi/`: `DocsSignInTests.cs`, `ServedSpecTests.cs` and `CommittedSpecTests.cs` (delete the `served.Remove("servers")` line from Task 1).

**No server address (ADR 0017).** The first thing `DocsSignIn.TransformAsync` does, unconditionally, is set `document.Servers = null`, so the writer omits the key. This removes the entry the framework derives from the request's `Host` and scheme. It runs before any of the early returns below, so every served spec lacks `servers`, whether or not sign-in is configured. The build-time document has none anyway.

**Which document gets sign-in details.** After that, `TransformAsync` returns with no sign-in added when any of these holds:
- `BuildTimeGeneration.IsActive` is true.
- `Workspace:Docs:ClientId` is empty.
- The `Workspace:Identity` section does not exist.

**Discovery.** Otherwise it takes `jwt.Get(JwtBearerDefaults.AuthenticationScheme).ConfigurationManager`, awaits discovery with `WaitAsync(DiscoveryWait, cancellationToken)`, and returns with no sign-in added in any of these cases:
- The configuration manager is null.
- The wait times out.
- Any exception occurs, except the caller's own cancellation.
- `AuthorizationEndpoint` or `TokenEndpoint` is not an absolute URI.

**What it adds.**

1. The security scheme:

```text
workspaceSignIn = { type: oauth2, flows: { authorizationCode: {
    authorizationUrl: <discovery.AuthorizationEndpoint>, tokenUrl: <discovery.TokenEndpoint>,
    scopes: { <each Workspace:Docs:Scopes entry, ordinal-sorted, distinct>: "Workspace API access" } } } }
```

2. On every operation whose `Security` is non-empty, an extra requirement `{ workspaceSignIn: [scopes…] }`. The bearer requirement stays first.

**Test helper.** `ServedSpec.StripDeploymentAdditions(JsonNode served)` returns a deep clone with only the sign-in additions removed. It does not remove `servers`, so a served `servers` entry makes the comparison fail:
- `components.securitySchemes.workspaceSignIn`
- every security requirement object whose only key is `workspaceSignIn`

```csharp
[Trait("Category", "OpenApi")]
public sealed class DocsSignInTests
{
    static readonly Dictionary<string, string?> Docs = new() { ["Workspace:Docs:ClientId"] = "workspace-spa", ["Workspace:Docs:Scopes"] = "openid" };
    static JsonNode? SignIn(JsonNode spec) => spec["components"]!["securitySchemes"]!["workspaceSignIn"];

    [Fact] public async Task AuthorizationCodeEndpointsComeFromDiscovery()
    {
        await using var host = OpenApiHost.Start(Docs);
        var flow = SignIn(await host.SpecAsync())!["flows"]!["authorizationCode"]!;
        Assert.Equal(OpenApiHost.Discovery.AuthorizationEndpoint, (string)flow["authorizationUrl"]!);
        Assert.Equal(OpenApiHost.Discovery.TokenEndpoint, (string)flow["tokenUrl"]!);
        Assert.Equal(["openid"], flow["scopes"]!.AsObject().Select(p => p.Key));
    }

    [Fact] public async Task SignInIsAnAlternativeOnProtectedOperationsOnly()
    {
        await using var host = OpenApiHost.Start(Docs);
        var spec = await host.SpecAsync();
        foreach (var path in spec["paths"]!.AsObject().Where(p => p.Key.StartsWith("/api/")))
            foreach (var op in path.Value!.AsObject())
                Assert.Equal("""[{"bearer":[]},{"workspaceSignIn":["openid"]}]""", op.Value!["security"]!.ToJsonString());
        Assert.Equal("[]", spec["paths"]!["/health"]!["get"]!["security"]!.ToJsonString());
    }

    [Fact] public async Task NoDocsClientMeansNoSignIn()
    {
        await using var host = OpenApiHost.Start();
        Assert.Null(SignIn(await host.SpecAsync()));
    }

    public static TheoryData<string> Failing => ["hangs", "throws", "no endpoints"];
    [Theory, MemberData(nameof(Failing))]
    public async Task ASlowOrBrokenProviderStillServesTheSpecWithoutSignIn(string mode)
    {
        IConfigurationManager<OpenIdConnectConfiguration> discovery = mode switch
        {
            "hangs" => new FakeDiscovery(() => new TaskCompletionSource<OpenIdConnectConfiguration>().Task),
            "throws" => new FakeDiscovery(() => Task.FromException<OpenIdConnectConfiguration>(new HttpRequestException("unreachable"))),
            _ => new StaticConfigurationManager<OpenIdConnectConfiguration>(new OpenIdConnectConfiguration { Issuer = OpenApiHost.Issuer })
        };
        await using var host = OpenApiHost.Start(Docs, discovery);
        var timer = Stopwatch.StartNew();
        var response = await host.Client.GetAsync("/api/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(5), $"Took {timer.Elapsed}.");
        Assert.Null(SignIn(JsonNode.Parse(await response.Content.ReadAsStringAsync())!));
    }
    // FakeDiscovery(Func<Task<OpenIdConnectConfiguration>>) implements GetConfigurationAsync and a no-op RequestRefresh.
}

[Trait("Category", "OpenApi")]
public sealed class ServedSpecTests
{
    [Fact] public async Task ServedSpecIsTheCommittedContractPlusSignInOnly() // B14
    {
        await using var host = OpenApiHost.Start(new() { ["Workspace:Docs:ClientId"] = "workspace-spa", ["Workspace:Docs:Scopes"] = "openid" });
        var served = await host.SpecAsync();
        Assert.NotNull(served["components"]!["securitySchemes"]!["workspaceSignIn"]);
        Assert.True(JsonNode.DeepEquals(OpenApiHost.CommittedSpec(), ServedSpec.StripDeploymentAdditions(served)));
    }

    [Theory] [InlineData(true)] [InlineData(false)]
    public async Task NothingInTheRequestPutsAServerAddressInTheSpec(bool signIn) // B14, B15, ADR 0017
    {
        await using var host = OpenApiHost.Start(signIn ? new() { ["Workspace:Docs:ClientId"] = "workspace-spa" } : null);
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/openapi/v1.json");
        request.Headers.Host = "attacker.example.test";
        request.Headers.Add("X-Forwarded-Host", "attacker.example.test");
        request.Headers.Add("X-Forwarded-Proto", "http");
        var text = await (await host.Client.SendAsync(request)).Content.ReadAsStringAsync();
        Assert.Null(JsonNode.Parse(text)!["servers"]);
        Assert.DoesNotContain("attacker.example.test", text);
    }

    [Fact] public async Task ServedSpecRevealsNoSecretOrStorageDetail() // B20
    {
        var secrets = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Workspace"] = "Host=db.private.example.test;Password=fixture-secret-one",
            ["Workspace:Database:SecretArn"] = "arn:aws:secretsmanager:eu-west-2:123456789012:secret:fixture-db",
            ["Workspace:Embeddings:Endpoint"] = "http://embeddings.private.example.test/v1/embeddings",
            ["Workspace:Objects:SecretKey"] = "fixture-secret-two",
            ["Workspace:Docs:ClientId"] = "workspace-spa"
        };
        await using var host = OpenApiHost.Start(secrets);
        var text = (await host.SpecAsync()).ToJsonString();
        foreach (var value in new[] { "fixture-secret-one", "fixture-secret-two", "db.private", "secretsmanager", "embeddings.private" })
            Assert.DoesNotContain(value, text);
    }
}
```

**Before the change**, the sign-in tests fail because no `workspaceSignIn` appears, and `NothingInTheRequestPutsAServerAddressInTheSpec` fails on the request-derived `servers`. **After the change**, they pass. `CommittedSpecTests` still passes without its `servers` line, because the build-time document is untouched.

## Task 10 — CI drift check and publish switches

**Files:** `.github/workflows/ci.yml`, `docs/automation-setup.md`, `deploy/workspace/app/Dockerfile`, `deploy/workspace/aws/package.sh`, `README.md`, `test/api-contract.test.mjs`.

Add a second job to `ci.yml`. Keep the existing `test` job and the `on:` triggers unchanged, and add no `paths:` filter:

```yaml
  api-contract:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          global-json-file: runtime/global.json
      - run: dotnet restore runtime/RuntimeKnowledge.slnx --locked-mode
      - name: Regenerate the API spec
        run: dotnet build runtime/RuntimeKnowledge.Api --no-restore
      - name: Check the committed API spec
        run: |
          if [ -n "$(git status --porcelain -- runtime/openapi)" ]; then
            git --no-pager diff -- runtime/openapi
            echo "::error file=runtime/openapi/workspace-api.json::The committed API spec is out of date. Run: dotnet build runtime/RuntimeKnowledge.Api, then commit runtime/openapi/."
            exit 1
          fi
      - name: Check deployment settings do not reach the committed spec
        env:
          Workspace__Identity__Provider: Keycloak
          Workspace__Identity__Issuer: https://issuer.example.test/realms/workspace
          Workspace__Docs__ClientId: workspace-spa
          ASPNETCORE_ENVIRONMENT: Development
        run: dotnet build runtime/RuntimeKnowledge.Api --no-restore --no-incremental && git diff --exit-code -- runtime/openapi
      - name: Check spec completeness
        run: dotnet test runtime/RuntimeKnowledge.Tests --no-restore --filter Category=OpenApi
```

**Other edits:**

- **`docs/automation-setup.md`:** the required checks sentence becomes "Set required status checks to include `test`, `api-contract`, `ai-fingerprint`, and `merge-policy` (from GitHub Actions)…". After it, add: "`api-contract` rebuilds the workspace API, fails if `runtime/openapi/` differs from what the code generates, and runs the `OpenApi` tests. It runs on every pull request."
- **Dockerfile:** `dotnet publish RuntimeKnowledge.Api -c Release --no-restore -p:OpenApiGenerateDocuments=false -o /out/api`.
- **`package.sh`:** add `-p:OpenApiGenerateDocuments=false` to the `dotnet publish "RuntimeKnowledge.$component"` line. The worker ignores it.
- **README:** in the workspace section, add one paragraph covering:
  - the spec at `/api/openapi/v1.json` and the docs UI at `/api/docs` in every deployment;
  - that the committed copy is `runtime/openapi/workspace-api.json`, regenerated by `dotnet build runtime/RuntimeKnowledge.Api` and committed in the same pull request as the code change;
  - that CI's `api-contract` job enforces it.

```js
import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import YAML from 'yaml';

const text = (path) => readFileSync(new URL(`../${path}`, import.meta.url), 'utf8');
const ci = YAML.parse(text('.github/workflows/ci.yml'));
const job = ci.jobs['api-contract'];
const run = (pattern) => job.steps.find((step) => pattern.test(step.run ?? ''));

test('api-contract runs on every pull request with the pinned SDK and locked restore', () => {
  assert.ok(job, 'ci.yml has an api-contract job');
  assert.equal(ci.on.pull_request?.paths, undefined);
  assert.equal(job.steps.find((s) => s.uses?.startsWith('actions/setup-dotnet@')).with['global-json-file'], 'runtime/global.json');
  assert.ok(run(/dotnet restore runtime\/RuntimeKnowledge\.slnx --locked-mode/));
});
test('the drift check catches changed and untracked spec files and names the regeneration command', () => {
  const check = run(/git status --porcelain -- runtime\/openapi/);
  assert.ok(check, 'uses git status so an uncommitted new file fails');
  assert.match(check.run, /The committed API spec is out of date\. Run: dotnet build runtime\/RuntimeKnowledge\.Api, then commit runtime\/openapi\/\./);
  assert.ok(job.steps.indexOf(run(/^dotnet build runtime\/RuntimeKnowledge\.Api --no-restore$/m)) < job.steps.indexOf(check));
});
test('deployment-shaped settings cannot change the committed spec', () => {
  const step = run(/--no-incremental && git diff --exit-code -- runtime\/openapi/);
  assert.equal(step.env.Workspace__Docs__ClientId, 'workspace-spa');
  assert.equal(step.env.ASPNETCORE_ENVIRONMENT, 'Development');
});
test('the completeness tests run in api-contract', () => assert.ok(run(/dotnet test runtime\/RuntimeKnowledge\.Tests .*--filter Category=OpenApi/)));
test('api-contract is a required check', () =>
  assert.match(text('docs/automation-setup.md'), /required status checks to include `test`, `api-contract`, `ai-fingerprint`, and `merge-policy`/));
test('packaged API builds do not generate the spec', () => {
  assert.match(text('deploy/workspace/app/Dockerfile'), /dotnet publish RuntimeKnowledge\.Api [^\n]*-p:OpenApiGenerateDocuments=false/);
  assert.match(text('deploy/workspace/aws/package.sh'), /dotnet publish "RuntimeKnowledge\.\$component" [^\n]*-p:OpenApiGenerateDocuments=false/);
});
test('the README documents the paths and the regeneration command', () => {
  const readme = text('README.md');
  for (const pattern of [/\/api\/openapi\/v1\.json/, /\/api\/docs/, /dotnet build runtime\/RuntimeKnowledge\.Api/, /runtime\/openapi\/workspace-api\.json/]) assert.match(readme, pattern);
});
```

**Before the change**, `node --test test/api-contract.test.mjs` fails with `ci.yml has an api-contract job`. **After the change**, it passes. Push the branch and confirm the `api-contract` job is green on the PR.

## Task 11 — identity-provider configuration and deployment checks

**Files:** `deploy/workspace/local/realm.json`, `deploy/workspace/home-lab/realm-template.json`, `deploy/workspace/local/compose.yml`, `deploy/workspace/home-lab/compose.yml`, `deploy/workspace/home-lab/verify-config.sh`, `deploy/workspace/aws/identity.tf`, `deploy/workspace/aws/outputs.tf`, `deploy/workspace/aws/tests/workspace.tftest.hcl`, `deploy/workspace/verify-docs.sh` (new, executable), `deploy/workspace/local/smoke.sh`, `deploy/workspace/aws/simulation.md`, `deploy/workspace/home-lab/deployment-contract.md`, `test/workspace-docs-config.test.mjs` (new), `test/init-division.test.mjs`.

**Keycloak realms.** On the `workspace-spa` client, the redirect URIs become:

| File | `redirectUris` |
| --- | --- |
| `local/realm.json` | `["${WORKSPACE_ORIGIN}/authentication/login-callback", "${WORKSPACE_ORIGIN}/api/docs/oauth2-redirect.html"]` |
| `home-lab/realm-template.json` | The same two paths, under `https://${WORKSPACE_PUBLIC_HOST}` |

The `webOrigins`, the PKCE attribute and the `workspace-api` audience mapper are unchanged, which covers B21.

**API settings.** The web UI's values, with no new client:

| File | API environment |
| --- | --- |
| `local/compose.yml` | `Workspace__Docs__ClientId: workspace-spa` |
| `home-lab/compose.yml` | `Workspace__Docs__ClientId: ${WORKSPACE_SPA_CLIENT:?Supply the public SPA client ID}` |
| `aws/identity.tf`, in `local.api_identity` | `Workspace__Docs__ClientId = var.entra_client_id` and `Workspace__Docs__Scopes = var.entra_api_scope` |

Compose sets no scopes, because the web UI there has no API scope.

**Terraform output and checks:**

- **`outputs.tf`:** add `output "entra_docs_redirect_uri" { value = "https://${aws_cloudfront_distribution.workspace.domain_name}/api/docs/oauth2-redirect.html" }`.
- **`verify-config.sh`:**
  - The compose `jq` check gains `(.services.api.environment.Workspace__Docs__ClientId == "workspace-spa")`.
  - The realm check expects `.redirectUris == ["https://${WORKSPACE_PUBLIC_HOST}/authentication/login-callback", "https://${WORKSPACE_PUBLIC_HOST}/api/docs/oauth2-redirect.html"]`.
- **`workspace.tftest.hcl`:** in `private_database_and_serverless_api`, add an assert that `local.workloads.api.environment.Workspace__Docs__ClientId == var.entra_client_id && local.workloads.api.environment.Workspace__Docs__Scopes == var.entra_api_scope`. The error message is "The docs UI must reuse the web client and API scope."

**`verify-docs.sh <origin> [--expect-sign-in]`.** This script uses `set -euo pipefail` and checks, anonymously:

1. `curl -fsS "$origin/api/openapi/v1.json"` returns JSON whose `.openapi` starts with `3.1`, and `jq -e 'has("servers") | not'` passes on it (ADR 0017).
2. With `--expect-sign-in`, `.components.securitySchemes.workspaceSignIn.flows.authorizationCode.authorizationUrl` is an absolute `http(s)` URL.
3. `$origin/api/docs/index.html` and `$origin/api/docs/oauth2-redirect.html` return 200.
4. `curl -s "$origin/api/inventory"` gives a 401 whose body has `.code == "authenticationRequired"` (B19, B22). Use `-w '%{http_code}'` with the body written to a temporary file.

On success, it prints `Docs verified at <origin>.`. On failure, it names the failing check and exits non-zero.

**Smoke run.** `smoke.sh` runs `"$root/deploy/workspace/verify-docs.sh" "http://workspace.localhost:$WORKSPACE_PORT" --expect-sign-in` straight after `bootstrap.sh`.

**Documentation:**

- **`simulation.md`:** after the sentence about Entra app registrations, add: "Register the `entra_docs_redirect_uri` output (`https://<workspace_url>/api/docs/oauth2-redirect.html`) as a *Single-page application* redirect URI on the existing SPA app registration, beside `entra_redirect_uri`. The docs UI at `/api/docs` signs in with that registration and `entra_api_scope`, using the authorization code flow with PKCE. It needs no new app registration, role or secret. After deployment, run `deploy/workspace/verify-docs.sh https://<workspace_url> --expect-sign-in`."
- **`deployment-contract.md`:** add: "The realm template's public SPA client also allows `https://<host>/api/docs/oauth2-redirect.html`, the callback of the API docs UI at `/api/docs`. On a Keycloak realm managed outside the template, add that redirect URI to the SPA client. The API reads `Workspace__Docs__ClientId` from `WORKSPACE_SPA_CLIENT`. After deployment, run `deploy/workspace/verify-docs.sh https://<host> --expect-sign-in`."
- **`test/init-division.test.mjs`:** add `'runtime/openapi'` to `copied`. In the first test, assert that `runtime/openapi/workspace-api.json` in the copy is byte-identical to the template's (B16).

```js
import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync, statSync } from 'node:fs';
import YAML from 'yaml';

const text = (path) => readFileSync(new URL(`../${path}`, import.meta.url), 'utf8');
const docs = '/api/docs/oauth2-redirect.html';
const spa = (realm) => JSON.parse(text(realm)).clients.find((c) => c.publicClient);

test('both Keycloak realms allow the docs callback on the web UI client', () => {
  assert.deepEqual(spa('deploy/workspace/local/realm.json').redirectUris,
    ['${WORKSPACE_ORIGIN}/authentication/login-callback', `\${WORKSPACE_ORIGIN}${docs}`]);
  assert.deepEqual(spa('deploy/workspace/home-lab/realm-template.json').redirectUris,
    ['https://${WORKSPACE_PUBLIC_HOST}/authentication/login-callback', `https://\${WORKSPACE_PUBLIC_HOST}${docs}`]);
  for (const realm of ['deploy/workspace/local/realm.json', 'deploy/workspace/home-lab/realm-template.json'])
    assert.ok(spa(realm).protocolMappers.some((m) => m.protocolMapper === 'oidc-audience-mapper'), 'audience mapper kept (B21)');
});
test('the API reuses the web UI client in every deployment', () => {
  assert.equal(YAML.parse(text('deploy/workspace/local/compose.yml')).services.api.environment.Workspace__Docs__ClientId, 'workspace-spa');
  assert.match(YAML.parse(text('deploy/workspace/home-lab/compose.yml')).services.api.environment.Workspace__Docs__ClientId, /^\$\{WORKSPACE_SPA_CLIENT:\?/);
  const identity = text('deploy/workspace/aws/identity.tf');
  assert.match(identity, /Workspace__Docs__ClientId\s*=\s*var\.entra_client_id/);
  assert.match(identity, /Workspace__Docs__Scopes\s*=\s*var\.entra_api_scope/);
  assert.match(text('deploy/workspace/aws/outputs.tf'), /entra_docs_redirect_uri[\s\S]*\/api\/docs\/oauth2-redirect\.html/);
});
test('Entra and externally managed Keycloak setups document the docs callback', () => {
  assert.match(text('deploy/workspace/aws/simulation.md'), /entra_docs_redirect_uri[\s\S]*Single-page application[\s\S]*PKCE/);
  assert.match(text('deploy/workspace/home-lab/deployment-contract.md'), /\/api\/docs\/oauth2-redirect\.html[\s\S]*managed outside the template/);
});
test('the docs check runs in the local smoke and is executable', () => {
  assert.match(text('deploy/workspace/local/smoke.sh'), /verify-docs\.sh" "http:\/\/workspace\.localhost:\$WORKSPACE_PORT" --expect-sign-in/);
  assert.ok(statSync(new URL('../deploy/workspace/verify-docs.sh', import.meta.url)).mode & 0o111);
});
test('the docs check refuses a served server address and expects the 401 code', () => {
  const script = text('deploy/workspace/verify-docs.sh');
  assert.match(script, /has\("servers"\) \| not/);
  assert.match(script, /authenticationRequired/);
});
```

**Before the change**, `node --test test/workspace-docs-config.test.mjs` fails on the realm redirect URIs. **After the change**, these must pass:

- `node --test test/workspace-docs-config.test.mjs test/init-division.test.mjs`
- `bash deploy/workspace/home-lab/verify-config.sh`
- `deploy/workspace/aws/verify-terraform.sh`

Where Docker is available, `bash deploy/workspace/local/smoke.sh` must pass, including `Docs verified at http://workspace.localhost:18087.`.

The home-lab and AWS runs of `verify-docs.sh`, and the live sign-in walkthroughs (B9–B13 in each deployment), belong to the user's acceptance phase.

## Completion check for the build phase

- [ ] All eleven tasks are committed and pushed, each with its test.
- [ ] `npm test` and `dotnet test runtime/RuntimeKnowledge.Tests --filter Category=OpenApi` pass. The Docker-backed suites pass where they can run.
- [ ] `api-contract` is green on the build PR, and `dotnet build runtime/RuntimeKnowledge.Api` leaves `git status` clean.
- [ ] Every 400, 401 and 403 from the API has its B22 body, and no served spec has a `servers` entry.
