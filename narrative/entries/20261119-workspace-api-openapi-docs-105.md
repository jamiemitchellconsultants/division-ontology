---
date: 2026-11-19
slug: workspace-api-openapi-docs-105
title: "Workspace API OpenAPI docs (#105)"
summary: "Generate and commit the OpenAPI 3.1 contract with its source changes. Serve it and Swagger UI anonymously under `/api`. Use the deployment's existing SPA registration with authorization code and PKCE."
kind: product
status: accepted
sequence: 2026-11-19T15:46:19.000Z
evidence: "https://github.com/jamiemitchellconsultants/division-ontology/pull/112; merge commit a406ecf1fb90cb858010d1750c332cc75f8c1a26"
---

## Context

Workspace integrators and reviewers needed a discoverable API contract that follows the running API and can be tried on each supported deployment. The existing identity providers, audience and role assignments govern all data access.

## Decision

Generate and commit the OpenAPI 3.1 contract with its source changes. Serve it and Swagger UI anonymously under `/api`. Use the deployment's existing SPA registration with authorization code and PKCE. Keep requests on the docs origin by omitting a server address. Return fixed problem details for 400, 401 and 403 without changing which requests receive those statuses. Check drift and completeness in `api-contract` and verify observed statuses in API scenarios. The design decisions are recorded in ADRs 0013–0018.

## Consequences

Integrators can inspect the contract before signing in. Signed-in calls use the workspace's existing roles, and contributions made through Swagger appear in the workspace's history. Contract changes appear beside their code in review. Deployment operators need the docs callback on their SPA registration and must verify sign-in on the deployed origin.

---

AI-Fingerprint: sha256:2a591babf349
