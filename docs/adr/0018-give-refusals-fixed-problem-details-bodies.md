# 0018 — Give 400, 401 and 403 fixed problem-details bodies through one status-code hook

- **Status:** accepted
- **Date:** 2026-11-19
- **Feature:** #105 (docs/features/workspace-api-openapi-docs/)
- **Amends:** [ADR 0016](0016-check-spec-completeness-statically-and-by-observation.md), on which statuses count as an operation's statuses

## Context

The amended vision allows one behaviour change. The API's 400, 401 and 403 responses become problem details carrying a `code`, so every error has the same documented shape (B2, B22). Which requests get which status must not change. A 401 must not say why a token was rejected.

Today the framework writes these responses with empty bodies:

- `JwtBearer`'s challenge (401, with `WWW-Authenticate: Bearer`, and `IncludeErrorDetails = false`).
- Authorisation's forbid (403).
- Minimal-API binding failures (400).

Workspace errors (404, 409, 422 and 503) already go through `ProblemResponses`.

The framework also rejects some requests before any operation is selected. A body that is not JSON gets 415, and endpoint selection returns it even to an anonymous caller on a protected route. A wrong method gets 405, an unmatched path gets 404, and a body over the server limit gets 413.

## Decision

A **refusal responses** hook in `ApiProgram.Configure` uses `UseStatusCodePages`. It writes problem details for a 400, 401 or 403 response that has no body:

| Status | `code` | `title` |
| --- | --- | --- |
| 400 | `unreadableRequest` | "The request body or parameters could not be read." |
| 401 | `authenticationRequired` | "Sign in to use the workspace API." |
| 403 | `roleRequired` | "Your account does not have the role this operation needs." |

The codes and titles live in `ProblemResponses`, beside the workspace code-to-status map, so there is one source for every error body.

The hook leaves every other status, and any response that already has a body, untouched. That includes the statuses the framework returns before an operation is selected.

Those pre-selection statuses (404 for an unmatched path, 405, 413 and 415) are not statuses of an operation, because no operation runs. The spec states each operation's method, path and `application/json` request media type. It does not list these statuses per operation, and the observation hook records only responses from a selected endpoint.

## Consequences

- One mechanism covers the container host and `LambdaHosting`, because both run `ApiProgram.Configure`. `JwtBearer`, the authorisation policies and the binding rules are unchanged, so every request keeps its status, and 401 keeps `WWW-Authenticate: Bearer`.
- A missing, invalid or expired token gets the same 401 body. Only the per-request `traceId` differs, and it does not depend on the cause.
- The Blazor web UI needs no change. It shows the API's title for 403 and 400 (U15).
- The completeness and observation checks treat 400, 401 and 403 as problem details with these codes.

## Alternatives considered

- **`JwtBearerEvents.OnChallenge` and `OnForbidden`, plus `ThrowOnBadRequest` mapped in the exception handler.** This needs three separate hooks for one rule. `ThrowOnBadRequest` also turns 415 and 413 binding failures into exceptions that the handler would have to map back to their statuses.
- **Problem details for 405, 413 and 415 too.** The vision allows the change for 400, 401 and 403 only.
- **Describing the pre-selection statuses on every operation.** It would repeat transport rules that no operation raises, and the observation hook cannot see them per operation anyway.
