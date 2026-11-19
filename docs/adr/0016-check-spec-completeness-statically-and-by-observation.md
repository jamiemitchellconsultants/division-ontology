# 0016 — Check spec completeness statically and against observed responses

- **Status:** accepted
- **Date:** 2026-11-19
- **Feature:** #105 (docs/features/workspace-api-openapi-docs/)

## Context

The completeness check must fail on any operation that lacks its summary, role, schemas, or any status it can return (B2, B3). Route metadata shows routes, roles and declared responses. It cannot prove which error statuses the handlers actually raise, because those come from `WorkspaceException` codes mapped in `ProblemResponses`. The existing scenario tests need Docker, and CI has no .NET job yet.

## Decision

Combine two checks:

- **Static check.** Each route declares the workspace error codes it can raise. Its problem responses are derived from those codes, through the single code-to-status map. A Docker-free `OpenApi` test category compares every route in the endpoint data source with the generated document, and requires a summary, `x-workspace-role`, request and response schemas, 401 and 403 when the route is protected, and the generic 500. It runs in the `api-contract` CI job.
- **Observation check.** `ApiFixture` records every route and status that the existing Docker-backed scenario suites observe, and fails if any of them is undeclared in the spec.

## Consequences

- Missing descriptions fail fast in CI on every pull request. Undeclared real statuses fail wherever the scenario suites run.
- Declaring the error codes is the developer's job, and the code-to-status map stays the single source of status numbers.
- The observation check is only as good as the scenarios' coverage of error paths. Gaps there are test gaps, not spec gaps.

## Alternatives considered

- **Static check only:** cannot detect a real status the handler returns but nobody declared.
- **Hand-maintained expected-status lists in tests:** duplicates the contract and drifts.
- **Running the Docker suites in CI for this:** heavier and outside this feature. The observation hook works wherever they already run.
