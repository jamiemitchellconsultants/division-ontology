---
status: accepted
date: 2026-11-26
feature: 198
---

# 0030 — Hold evidence excerpts in a per-snapshot impact model behind sign-in

Feature: docs/features/generalise-impact-assessment/

## Context

Readers must see cited lines and page metadata in the workspace (B25, B28). That text must never appear in `dist/` or in an unauthenticated response (B29, Q7). The runtime reads only the projection today, while the cited bytes sit in object storage. Search and paths must meet Q1 (2 seconds and 5 seconds at the 95th percentile) on 10,000 nodes and 50,000 assertions, on an API that runs as Lambda on AWS.

## Decision

At candidate projection, the worker builds the impact model from the bundle with the engine. The model includes every evidence record's excerpt and page metadata. The worker stores it compressed in `workspace_projection.impact_models`, keyed by generation and snapshot, in the same transaction as the rest of the candidate.

The API loads the model for the snapshot it serves once per process, and keeps it in memory, keyed by snapshot id. Cited text leaves the API only through routes in the authenticated `/api` group.

## Consequences

- Requests never read object storage, and lookups are in memory, which gives Q1 its best chance. A cold start pays for one row read and one decompression, and the developer measures it.
- Page text stays inside the access-controlled database and the signed-in API. The public catalog build is untouched.
- The model is derived data, so replay rebuilds it from the bundle (ADR 0008).
- The size of the model row grows with the evidence text. Bundle objects are already capped at 64 MiB each.

## Alternatives considered

- **Read excerpts from object storage for each request.** That adds per-request latency and needs S3 access from the API path for every evidence display.
- **Store excerpts as rows and query the graph through AGE.** Path rules that depend on direction and crossings would become multi-step Cypher queries, which are harder to keep identical to the command line.
- **Publish excerpts with the catalog.** That breaks B29.
