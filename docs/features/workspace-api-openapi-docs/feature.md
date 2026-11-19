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
