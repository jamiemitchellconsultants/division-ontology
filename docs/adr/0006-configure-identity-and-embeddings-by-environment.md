# 0006 — Configure identity and embeddings by environment

- **Status:** accepted
- **Date:** 2026-11-07
- **Feature:** [#25](https://github.com/jamiemitchellconsultants/division-ontology/issues/25) ([architecture](../features/runtime-knowledge-workspace/03-architecture.md))

## Context

The workspace must enforce the same roles across Keycloak and Entra. The three environments have different embedding providers and model sizes, while using the same application and storage contract.

## Decision

Select identity, object storage and embedding adapters through deployment configuration. Normalize validated Keycloak and Entra access tokens to the same workspace roles and retain the issuer-qualified subject as the acting identity.

Use Qwen3 Embedding 0.6B on the Mac, Qwen3 Embedding 8B Q4 in the home lab and Amazon Titan Text Embeddings through Bedrock on AWS. Isolate each model profile's vectors and use that same profile for search requests. Build a replacement index before activating a changed profile.

## Consequences

Business logic and capability tests can share one contract. Sign-in configuration, model readiness and semantic search quality must still be checked for each environment. Model changes require re-embedding, and different deployments may rank the same text differently.

## Alternatives considered

- Use one external identity and embedding service for every environment. This introduces remote dependencies into the full local setup and conflicts with the selected providers.
- Reuse one set of vectors across profiles. Models can produce incompatible vector spaces even at equal dimensions.
