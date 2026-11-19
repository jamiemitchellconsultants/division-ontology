# Workspace API OpenAPI docs

**Feature:** [#105](https://github.com/jamiemitchellconsultants/division-ontology/issues/105) (lite mode)

## Product owner — vision

**Status:** Accepted by the product owner on 19 November 2026.

### Problem

A division that deploys the runtime knowledge workspace gets a JSON API under `/api`, but nothing describes that API except its C# routes and contract types. Developers who want another division system, agent or tool to read the knowledge graph have to read the source to learn which endpoints exist, what they accept and return, and which role each one needs. The people maintaining this template and its division copies have the same problem when they test or debug the API, and reviewers cannot see from a pull request whether the API contract has changed. Both groups need an accurate description of the API that is always up to date, plus a page where they can browse it and try calls against a running deployment with their own access rights.

### Users

- **Integrating developer:** builds or connects another division system, agent or tool (for example a future MCP server) that reads from, or contributes to, a deployed workspace. Needs the contract without reading this repository's code.
- **Workspace developer:** maintains this template or a division copy of it. Needs to explore, test and debug the API against local, home-lab and AWS deployments.
- **Contract reviewer:** a code owner or steward reviewing a pull request. Needs to see when the API contract changes, and how.

The existing roles still decide what anyone can do through the API. A signed-in user with `ReadKnowledgeGraph` can call the read operations. A user with `ManageKnowledgeGraph` can also call the contribution commands.

### Outcomes and measures

1. **Integrators can learn the API without reading code.** Measure: every operation mapped by the API, plus the health check, appears in the generated spec. Each one has its request and response shapes, the role it needs, and its error responses. An automated check compares the API's mapped routes with the spec and fails on any gap.
2. **Contract changes are visible in review.** Measure: the spec is written when the app builds and committed to the repository. CI fails when the committed spec differs from the one the code generates. A pull request that changes a route or contract type without updating the spec cannot pass.
3. **Developers can try the API against a live deployment.** Measure: from the docs UI, a user signs in through that deployment's identity provider (Keycloak or Entra) and calls the API with their own roles. A reader gets a successful response from a read operation. A contributor can run a contribution command. A user without the needed role sees the API's refusal. This walkthrough passes in the local, home-lab and AWS deployments.
4. **It works wherever the workspace is deployed.** Measure: the spec and docs UI are available at one documented path in the local compose, home-lab and AWS deployments. A division copy created from the template gets them with no extra per-division code.
5. **Publishing the docs does not widen access to data.** Measure: anyone who can reach the deployment can fetch the spec and open the docs UI without signing in. Every call that returns or changes knowledge-graph data still needs a valid token with the right role. An anonymous data request is refused, just as it is today.

### Scope

- An OpenAPI description of the workspace API, generated from the code, covering every read operation, contribution command and the health check.
- The spec served by the running API, and also written as a file when the app builds, committed to the repository and kept up to date by a CI check.
- A Swagger-style docs UI served with the deployed workspace, showing the generated spec.
- Signing in from the docs UI through the deployment's configured identity provider, and calling the API with the signed-in user's roles.
- Open, unauthenticated access to the spec and docs UI in every deployment. The API's shape is not treated as secret.
- Making the local, home-lab and AWS deployments, and the template that division copies inherit, serve the spec and docs UI.
- Describing what the API already does. Adding descriptions, examples and response metadata to existing endpoints is in scope. Changing endpoint behaviour is not.

### Out of scope

- An MCP server, SPARQL endpoint or any other new query interface over the knowledge graph.
- Setting up identities for other systems to call the API without a person signing in, such as service clients. The spec describes the authentication the API already accepts; giving other systems a way to get tokens is a separate feature.
- Generated client libraries or SDKs.
- An API versioning or deprecation policy.
- New endpoints, or changes to the behaviour, paths or payloads of existing ones, beyond what the description needs.
- A switch to hide the spec or docs UI in particular deployments.
- A separate public developer portal, or publishing the spec outside the deployment and the repository.
- Changes to the workspace's Blazor web UI.

### Research

- **Generating the spec while the app builds.** The API targets .NET 10 (`runtime/global.json`, `RuntimeKnowledge.Api.csproj`). In current ASP.NET Core, `Microsoft.AspNetCore.OpenApi` generates the spec while the app runs, and adding `Microsoft.Extensions.ApiDescription.Server` also writes it to a file during the build. The build starts the app's entry point with an inert server to do this. The output folder can be set with an MSBuild property, and one app can produce several documents. The package version should match the framework. Sources: [Microsoft Learn — Generate OpenAPI documents](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/openapi/aspnetcore-openapi), [Microsoft Learn — Using OpenAPI documents](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/openapi/using-openapi-documents), [Meziantou — Generate OpenAPI specification at build time](https://meziantou.net/generate-openapi-specification-at-build-time-from-the-code-in-asp-net-core.htm), [Scalar — Build-time generation](https://scalar.com/products/api-references/integrations/aspnetcore/build-time-generation).
  - *Implication:* the API's startup currently resolves database and identity settings, and fails without them. Generating the spec during a build or in CI needs startup to tolerate having no real settings. The architect should resolve this.
- **Signing in from a Swagger-style UI.** Swagger UI supports the authorization code flow with PKCE (`usePkceWithAuthorizationCodeGrant`) using a public client with no secret. Keycloak supports this for public clients. Entra examples exist from the community but not in Microsoft's own documentation. Known problems: Swagger UI's dialog may still ask for a client secret, which can be left empty, and the PKCE setting must match the identity provider's client. Scalar, a newer Swagger-style UI with an ASP.NET Core integration, also supports the authorization code flow with PKCE, extra authorization parameters such as an audience, and settings read from configuration. Either UI needs the spec to declare an OAuth2 security scheme. Sources: [Swagger UI — OAuth2 configuration](https://swagger.io/docs/open-source-tools/swagger-ui/usage/oauth2), [Duende — Swagger authentication in .NET 10 with OAuth 2.0 and PKCE](https://duendesoftware.com/blog/20251126-securing-openapi-and-swagger-ui-with-oauth-in-dotnet-10), [Milan Jovanović — Keycloak with ASP.NET Core](https://milanjovanovic.tech/blog/integrate-keycloak-with-aspnetcore-using-oauth-2.md), [Scalar — ASP.NET Core integration](https://guides.scalar.com/scalar/scalar-api-references/integrations/net-aspnet-core), [swagger-ui issue #7438](https://github.com/swagger-api/swagger-ui/issues/7438).
  - *Implication:* each deployment's identity provider must allow the docs UI's sign-in callback address, alongside the web UI's existing one. The realms in `deploy/workspace/local` and `deploy/workspace/home-lab` currently allow only the web UI's address.
- **Does public documentation reveal anything sensitive?** The spec would show the shape of the routes and contract types, the required roles, and the identity provider's issuer and client ID. The issuer and client ID are already public: the web UI loads them from its `appsettings.json`. The API already refuses data requests without a valid token and role (`runtime/RuntimeKnowledge.Api/Identity.cs`), and the docs UI adds no other way in. Examples in the spec must use invented values, never real catalogue content, because the catalogue is private (see the README). Source: this repository.
- **Routing in each deployment.** Only `/api/*` reaches the API today. In AWS, API Gateway routes `ANY /api/{proxy+}` (`deploy/workspace/aws/application.tf`) and CloudFront forwards `/api/*` (`deploy/workspace/aws/cdn.tf`). Locally, Caddy proxies `/api/*` and serves everything else as the web app (`deploy/workspace/local/Caddyfile`). The spec and docs UI need either a path under `/api` or new routes in all three deployments. The architect should decide which. Source: this repository.

## Analyst — behaviours

**Status:** Accepted by the user on 19 November 2026.

**Basis:** the accepted product-owner vision above, the product-owner handoff on #105, and the analyst decisions recorded with the user. The outcome numbers match the vision. The scenarios describe observable behaviour. Components, products, paths and deployment details are left to the architect.

### Behaviours

#### Outcome 1 — Integrators can learn the API without reading code

- **B1 — Every operation is described.** **Given** the operations the workspace API maps, **when** the spec is generated, **then** each read operation, each contribution command and the health check appears with its method, path, summary, parameters (including `cursor` and `limit` where it pages), request schema, success response schema and required role. The required role is `ReadKnowledgeGraph` for read operations, `ManageKnowledgeGraph` for contribution commands, and none for the health check.
- **B2 — Every error response is described.** **Given** an operation in the spec, **then** it lists every status the API can return for it: 401 for a missing, invalid or expired token; 403 for a token without the required role; and whichever of 404, 409, 422 and 503 apply. Each is described as a problem-details response carrying its `code`. A 409 version conflict also describes the `current` record it returns. A generic 500 is described once for all operations.
- **B3 — The completeness check fails on gaps.** **Given** a change that maps a new operation, or leaves an operation without a summary, required role, request or response schema, or any status it can return, **when** the automated tests run, **then** the completeness check fails and names each operation and what it is missing. **Given** a spec that describes every mapped operation fully, **then** the check passes.
- **B4 — Examples are invented.** **Given** the spec contains examples, **then** every example value is invented and contains no catalog content, source identifiers or names of real people.

#### Outcome 2 — Contract changes are visible in review

- **B5 — Building writes the spec.** **Given** a developer changes a route or contract type, **when** they build the API with the documented command, **then** the committed spec file is rewritten to match the code and the change appears in their working-tree diff.
- **B6 — The drift check fails when the spec is out of date.** **Given** a pull request in which the committed spec differs from the spec its code generates, **when** CI runs, **then** the drift check fails, says the committed spec is out of date, and names the command that regenerates it. **Given** a committed spec that matches, **then** the check passes.
- **B7 — The spec changes with its code.** **Given** a pull request that changes the API contract, **then** the regenerated spec is committed in that same pull request. Unlike `dist/`, it is never left to a separate generated pull request.
- **B8 — The committed spec is deployment-neutral.** **Given** the committed spec file, **then** it contains no deployment-specific value: no server address, identity-provider issuer, tenant, client ID or secret. **When** it is generated in a build or in CI, **then** no database, identity provider, embedding provider or secret is needed.

#### Outcome 3 — Developers can try the API against a live deployment

- **B9 — Sign in from the docs UI.** **Given** a deployment configured with Keycloak or Entra, and a user with an account in that identity provider, **when** the user opens the docs UI and chooses to sign in, **then** they are sent to that deployment's identity provider and returned to the docs UI signed in. They are never asked for a client secret.
- **B10 — A reader can call a read operation.** **Given** a user signed in to the docs UI with `ReadKnowledgeGraph`, **when** they run a read operation, **then** they receive the same response the API gives any other client with that token.
- **B11 — A contributor can run a contribution command.** **Given** a user signed in with `ManageKnowledgeGraph`, **when** they run a contribution command, for example creating a `KnowledgeAnnotation`, **then** the change is saved against the deployment's real data, attributed to that user, recorded as a `KnowledgeContributionChange`, and visible in history, exactly as if the web UI had made it. There is no separate trial mode.
- **B12 — A missing role is refused.** **Given** a user signed in with `ReadKnowledgeGraph` only, **when** they run a contribution command, **then** the docs UI shows the API's 403 response and nothing is saved. **Given** a signed-in user with no workspace role, **when** they run any operation except the health check, **then** the docs UI shows a 403 response.
- **B13 — Expired and signed-out sessions.** **Given** a user whose docs UI token has expired, **when** they run an operation, **then** the docs UI shows the API's 401 response, and the user can sign in again without leaving the page. **When** a user signs out of the docs UI, **then** the docs UI stops sending their token, and later data calls are refused with 401.
- **B14 — The served spec adds only sign-in details.** **Given** a deployment, **when** its spec is fetched, **then** it is the committed contract plus that deployment's server address and its identity provider's sign-in details, and nothing else. Comparing it with the committed file shows no other difference.

#### Outcome 4 — It works wherever the workspace is deployed

- **B15 — Same path in every deployment.** **Given** the local, home-lab or AWS deployment, **when** someone requests the documented spec path or docs UI path, **then** they get the spec or the docs UI. The web UI, `/api` and the health check behave as before.
- **B16 — Division copies need no extra code.** **Given** a repository created from the template and initialised with `init-division`, **when** it is deployed with the existing deployment instructions, **then** it serves the spec and docs UI. Its committed spec matches its code.
- **B17 — Identity-provider setup is supplied.** **Given** the local and home-lab realm configuration in the repository, **then** it allows the docs UI's sign-in callback alongside the web UI's. **Given** an Entra or other externally managed deployment, **then** the deployment documentation states the docs UI's callback address and what to register for it.

#### Outcome 5 — Publishing the docs does not widen access to data

- **B18 — Anonymous visitors can read the docs.** **Given** a visitor who has not signed in, **when** they fetch the spec or open the docs UI, **then** both are returned without a sign-in prompt.
- **B19 — Anonymous data calls are refused.** **Given** a visitor who has not signed in, **when** they run any operation except the health check, from the docs UI or directly, **then** the API refuses it with 401, as it does today.
- **B20 — The served spec reveals nothing private.** **Given** a deployment's served spec, **then** it contains no secret, database or storage detail, or catalog content. The only deployment values it includes are the server address and the identity provider's public sign-in details, which are already served to the web UI.
- **B21 — The docs UI grants nothing beyond the user's roles.** **Given** a user signed in through the docs UI, **then** their token has the same audience, and is checked against the same roles, as a token issued to the web UI. Signing in through the docs UI never grants any capability that the user's roles do not grant.

### Uncertainties

| ID | Question | Decision or explicit deferral | Basis |
| --- | --- | --- | --- |
| U1 | How do the committed spec and the served spec relate? | The committed file is deployment-neutral. The served spec is that contract plus the deployment's server address and sign-in details. The drift check compares contracts only (B8, B14). | Decided by the user. |
| U2 | How strict is the completeness check? | Each operation needs a summary, its required role, its request and response schemas, and every status it can return (B1–B3). | Decided by the user. |
| U3 | What does the drift check compare, and what does a failing contributor see? | The committed file against the spec the build generates from the same commit. On failure it says the spec is out of date and names the regeneration command (B6). | Analyst decision, following vision outcome 2. |
| U4 | Does the spec follow the `dist/` rule of a separate generated pull request? | No. It changes in the same pull request as the code, so that reviewers see the contract change alongside the code change (B7). | Analyst decision, following vision outcome 2. |
| U5 | What does a user see when a role is missing, a token expires, or they sign out? | The API's own 401 or 403 response, shown by the docs UI, and the option to sign in again in place (B12, B13). | Analyst decision, open to steering. |
| U6 | How fully are errors and the health check described? | Every status each operation can return, as problem details with `code` (and `current` for 409). The health check is described as anonymous (B1, B2). | Analyst decision, following U2. |
| U7 | Do contribution commands run from the docs UI change real data? | Yes. They are attributed and journalled like any other contribution, and there is no trial mode (B11). A sandbox or trial mode is out of scope. | Vision outcome 3. |
| U8 | Should the docs be open in every deployment, with no switch? | Yes, as the vision decided. The challenge raised in the handoff is answered by B20: the served spec adds nothing that is not already public. | Vision scope; product-owner handoff. |
| U9 | Does leaving out service clients still meet integrators' needs? | Explicitly deferred as out of scope. The spec describes the bearer-token requirement in a way that does not assume only people sign in, so that a later service-client feature can add its sign-in flow. | Vision out of scope; product-owner handoff. |
| U10 | Can the spec be generated without a database or identity provider? | Required as behaviour (B8). How it is achieved is deferred to the architect. | Product-owner research. |
| U11 | What are the spec and docs UI paths, which UI product, and how is each deployment routed? | Deferred to the architect. The behaviours need one documented path for each, the same in every deployment (B15). | Analyst/architect phase boundary. |
| U12 | Which OpenAPI version, document version and title? | Deferred to the architect. A versioning or deprecation policy is out of scope. | Vision out of scope. |
| U13 | What exactly is registered with Entra (scopes, redirect address, app registration)? | Deferred to the architect, within B9, B17 and B21. | Analyst/architect phase boundary. |
| U14 | Does this feature add domain terms to the ontology? | No. It describes the existing API and introduces no division knowledge concept. The existing terms `KnowledgeAnnotation` and `KnowledgeContributionChange` and the role names are used unchanged. | Analyst decision; see Terms. |

### Terms

This feature introduces no domain term, so `core/ontology.ttl` is unchanged. The words below are application vocabulary used only by these behaviours:

- **Spec:** the OpenAPI description of the workspace API.
- **Committed spec:** the deployment-neutral spec file in the repository, generated by the build.
- **Served spec:** the spec a deployment returns at its documented spec path: the committed contract plus that deployment's server address and sign-in details.
- **Docs UI:** the Swagger-style page a deployment serves for browsing the served spec and calling the API.
- **Completeness check:** the automated test that compares the API's mapped operations with the spec (B3).
- **Drift check:** the CI check that compares the committed spec with the spec the code generates (B6).

Existing ontology terms used: `KnowledgeAnnotation` and `KnowledgeContributionChange`. Existing role names used: `ReadKnowledgeGraph` and `ManageKnowledgeGraph`.
