# Runtime knowledge workspace implementation plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans or superpowers:subagent-driven-development to implement this plan task by task. Steps use checkbox syntax for tracking. Read the detailed companion plan before each task.

**Goal:** Let readers explore a pinned division knowledge baseline alongside durable, attributed annotations and runtime relationships, and let contributors update those records without changing source-owned facts.

**Architecture:** A standalone Blazor WebAssembly client calls an ASP.NET Core API. A publisher packages verified source commits and stages snapshots from immutable MinIO or S3 objects. PostgreSQL appends contribution and activation events in the same transaction as current SQL and Apache AGE projections. A worker indexes active text in pgvector and rebuilds projections by replaying the journal and pinned bundles.

**Tech Stack:** .NET 10, ASP.NET Core, Blazor WebAssembly, Npgsql, PostgreSQL 16 with Apache AGE and pgvector, Node.js 20+ and existing C4 loaders, Docker Compose, Keycloak, Entra ID, MinIO/S3, Qwen3 Embedding and Amazon Bedrock Titan Text Embeddings, Terraform and Lambda.

**Spec:** [Vision](../../../features/runtime-knowledge-workspace/01-vision.md), [behaviours](../../../features/runtime-knowledge-workspace/02-behaviours.md), [architecture and ADRs](../../../features/runtime-knowledge-workspace/03-architecture.md), and the [detailed implementation plan](../../../detailed-implementations/runtime-knowledge-workspace/plan.md).

## Global constraints

- Build only after the plan PR is reviewed and merged. Use `feature/runtime-knowledge-workspace/dev-build`. Each task ends in a focused commit and push after test and staged-diff review.
- Keep published source identity, source commit, snapshot, evidence and origin distinct. Use reviewed links and graph assertions as typed records. Equal labels and search scores cannot create equivalence, bindings or codification.
- Activate only complete validated snapshots. A query sees one snapshot. Commit a contribution event, current state, AGE change and index job together. Replay preserves recorded outcomes and provenance.
- Enforce `ReadKnowledgeGraph` and `ManageKnowledgeGraph` per request. Business RBAC notes are descriptive. External providers own assignments.
- Use full Docker Compose on Mac with Keycloak, MinIO and Qwen3 Embedding 0.6B. LocalAI operates the home-lab deployment using Keycloak, MinIO and Qwen3 Embedding 8B Q4. AWS uses Entra ID, S3, Titan Text Embeddings via Bedrock and Docker PostgreSQL on private EC2 with EBS. Use Lambda for API and bounded work.
- AWS checks use local simulation and mocked Terraform providers. Avoid live AWS calls or applies. Backup and disaster recovery belong to [#53](https://github.com/jamiemitchellconsultants/division-ontology/issues/53). Keep replay from the available journal and pinned bundles in scope.
- Keep authored changes separate from generated `dist/`. Run the existing repository tests. If a task changes catalog build inputs or builder code, run the repo's catalog build and check against the same clean source checkouts.
- Use fixtures for engineering verification. The pilot product, owner, wider-business reviewer and representative queries are nominated during acceptance planning. B14 is a human acceptance measure.

## File map and task order

| Task | Main files and contract | Behaviour coverage |
| --- | --- | --- |
| 1. Workspace and database foundation | `runtime/RuntimeKnowledge.slnx`, `runtime/RuntimeKnowledge.Store/`, `runtime/RuntimeKnowledge.Tests/`, `deploy/workspace/database/`. Start PostgreSQL with both extensions and prove SQL plus AGE rollback on one session. | B4, B22 |
| 2. Pinned bundle builder | `src/workspace/bundle.mjs`, `test/workspace-bundle.test.mjs`. Reuse pinned catalog, source graph and registry loaders. Produce deterministic manifests, verified artefacts and projected records. | B1, B3, B4 |
| 3. Stage, activate and read a snapshot | `runtime/RuntimeKnowledge.Store/Snapshots.cs`, `GraphQueries.cs`, `Sql/002_snapshots.sql`, `runtime/RuntimeKnowledge.Worker/BundleImport.cs`. Validate objects and stage before atomic activation. Expose snapshot-scoped reads and bounded traversal. | B1, B3–B6 |
| 4. Contributions, associations and history | `runtime/RuntimeKnowledge.Store/Contributions.cs`, `Sql/003_contributions.sql`. Apply validated versioned commands and append journal records with read-after-commit traversal. | B5–B12, B19–B25 |
| 5. Replay and projection cutover | `runtime/RuntimeKnowledge.Store/Replay.cs`, `runtime/RuntimeKnowledge.Worker/ReplayCommand.cs`. Replay into empty projections, verify and switch at a known journal position. | B4–B6, B10, B22–B25 |
| 6. API and identity | `runtime/RuntimeKnowledge.Api/`, `runtime/RuntimeKnowledge.Contracts/`. Expose JSON operations, provider claims and stable error contracts. | B1, B3, B6–B12, B15–B25 |
| 7. Semantic search | `runtime/RuntimeKnowledge.Store/Search.cs`, `Sql/005_search.sql`, `runtime/RuntimeKnowledge.Worker/IndexCommand.cs`, `runtime/RuntimeKnowledge.Providers/`. Provider adapters, versioned exact cosine search and staged index activation. | B2, B4, B7–B8, B13 |
| 8. Blazor workspace | `runtime/RuntimeKnowledge.Web/`. Implement signed-in exploration, contribution and history workflows and preserve pending edits. | B1–B3, B5–B13, B15–B20, B23–B25 |
| 9. Mac Compose stack | `deploy/workspace/local/`, application image files. Bootstrap the full stack and seed a fixture through the normal import path. | B1–B2, B4, B7, B15–B17, B22 |
| 10. Home-lab deployment contract | `deploy/workspace/home-lab/`. Versioned Compose, Caddy and Keycloak artefacts for LocalAI, plus checks of the Qwen endpoint and persistent state. | B1–B2, B4, B7, B15–B17, B22 |
| 11. AWS Terraform and simulation | `deploy/workspace/aws/`, Lambda entry points. Wire managed services and Docker PostgreSQL, test AWS adapters and handlers against simulated dependencies. | B2, B4, B15–B18, B22 |
| 12. Integrated behaviour and replay verification | `runtime/RuntimeKnowledge.Tests/WorkspaceScenarioTests.cs`, `deploy/workspace/verify.sh`. Exercise the same contract across the supported verification targets and record the evidence for user acceptance. | B1–B13, B15–B25. B14 is tracked for the user phase. |

## Review focus

- Corrupt or misidentified pinned artefacts cannot replace the active snapshot. Tasks 2–3.
- Publication racing a write must use the baseline active at commit, and an aborted write leaves no event, AGE edge or missing journal position. Task 4.
- Replay must preserve the last published endpoint revision even if its provenance changed without a user edit. Task 5.
- Valid tokens from the wrong issuer or audience, tokens with only an unrelated role and expired tokens must be denied before a mutation. Task 6.
- Stale index jobs cannot make edited, removed or wrong-profile content searchable, and an indexing outage must be distinguishable from no matches. Task 7.

### Task execution

Follow the [detailed plan](../../../detailed-implementations/runtime-knowledge-workspace/plan.md) for exact interfaces, fixtures, test code and commands. For every task:

- [ ] Add its named tests and run the focused command. Verify failure for the expected missing behaviour.
- [ ] Implement the task's interface and run its focused tests. Verify they pass.
- [ ] Run the existing repository test suite and the runtime tests introduced so far. Inspect the staged diff and run `git diff --cached --check`.
- [ ] Commit the task with the specified message and push its branch. Recompute the PR fingerprint after any push that changes an open PR's head.

## Behaviour verification at the build boundary

Pass focused tests and the complete engineering scenario on Mac Docker and the home-lab target, with AWS covered by infrastructure and adapter simulation. Use the pinned fixtures and assert B13's context retrieval independently of the eventual pilot. The user phase takes the nominated owner's product definition, asks the nominated reviewer to explain purpose and intended use, and records the B14 result.
