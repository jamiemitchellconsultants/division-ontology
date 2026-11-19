# 0015 — Sign in from the docs UI with the web UI's client and discovered endpoints

- **Status:** accepted; amended by [ADR 0017](0017-serve-the-spec-without-a-server-address.md) (no server address in the served spec)
- **Date:** 2026-11-19
- **Feature:** #105 (docs/features/workspace-api-openapi-docs/)

## Context

The docs UI must sign users in through the deployment's Keycloak or Entra, with no client secret, and grant nothing beyond their roles (B9, B13, B21). The served spec may add only the server address and sign-in details to the committed contract (B14). The web UI already uses a public PKCE client with the API audience in both providers.

## Decision

- Reuse the web UI's public client: `workspace-spa` in Keycloak, and the SPA app registration in Entra. Add `/api/docs/oauth2-redirect.html` as an extra redirect URI.
- The API reads `Workspace:Docs:ClientId` and `Workspace:Docs:Scopes`, which are set from the web UI's existing values.
- At request time, a document transformer adds the request origin as the server address and a `workspaceSignIn` OAuth2 authorization-code scheme. It takes the authorization and token endpoints from the discovery document that `JwtBearer` already loads, and adds the scheme as an alternative security requirement on each protected operation.
- Swagger UI uses PKCE.

## Consequences

- One code path serves both providers, and there is no new client, role or audience to administer.
- Tokens from the docs UI are indistinguishable from the web UI's, so the existing authorisation rules apply unchanged.
- Each identity provider needs one extra redirect URI, supplied in the repository's realm files and documented for Entra.
- With the docs settings missing, the spec and UI are still served, without sign-in.
- The served-spec test must strip exactly the server and `workspaceSignIn` additions to prove B14.

## Alternatives considered

- **A dedicated docs client:** separately revocable, but it is another client to register in every provider and keep aligned with the API audience and roles. That can be revisited if per-client control is needed.
- **Fixed per-provider endpoint templates:** Keycloak and Entra URL shapes differ, and discovery is already available and authoritative.
- **Putting sign-in details in the committed spec as placeholders:** breaks deployment neutrality (B8), and Swagger UI would need rewriting at runtime anyway.
- **Pasting a token in the UI:** the user chose sign-in from the UI.
