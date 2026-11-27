---
date: 2026-11-27
slug: docs-feature-plan-generalised-impact-assessment-build-202
title: "docs(feature): plan generalised impact assessment build (#202)"
summary: "- **The walking skeleton is a fixed baseline.** I1 first captures today's Node outputs on every C4 fixture scenario, with Git dates and identities pinned so the capture is deterministic, under `test/impact-corpus/`."
kind: product
status: accepted
sequence: 2026-11-27T08:13:36.000Z
evidence: "https://github.com/jamiemitchellconsultants/division-ontology/pull/218; merge commit 83750753c9f3de3a61801fc630b6eb1a01719bfe"
---

## Context

The architecture (#211, #215) settled on a single .NET engine that reads verified bundles. B37 requires that the C4 fixtures produce the same outputs as today, apart from an exhaustive list of differences. That makes the switch from the Node engine to the .NET engine the riskiest step. The workspace parts depend on the engine, and saved assessments must be byte-exact (Q5).

## Decision

- **The walking skeleton is a fixed baseline.** I1 first captures today's Node outputs on every C4 fixture scenario, with Git dates and identities pinned so the capture is deterministic, under `test/impact-corpus/`. It then ports the engine to .NET and checks it byte for byte against that baseline. Any later change to an expected file is a commit of its own, naming the B37 item that allows it.
- **JavaScript-exact JSON.** An `EcmaJson` writer reproduces `JSON.stringify`, so path ids and digests are unchanged.
- **Increments run command line first, then workspace.** B5 and B27 are P2 but are pulled into I2, because they change the same view and evidence code. B22 is P2 but is pulled into I5, because B33 (P1) refuses edits.
- **Implementation refinements within the architecture.** These are flagged for review:
  - the CLI builds the tool once and runs the built dll, where ADR 0028 has `dotnet run` on every call;
  - migrations are split into 006 (models) and 007 (assessments);
  - the stored "impact model" is a verified package of the bundle's impact objects, rebuilt into the model on load;
  - frozen assessments are stored as `bytea`, not `jsonb`, because `jsonb` reorders keys;
  - B33's `snapshotMismatch` is a separate error from B36's `snapshotChanged`;
  - revisions are returned with a saved assessment;
  - the search result gains the registry digests.

## Consequences

- The build is six sessions, one per increment, with a checkpoint after each.
- After I1, the Node test job in CI needs the .NET SDK, and architects need it too.
- Q1 is measured in I4 on the reference container. A miss there raises pushback, and paths are never cut short silently.
- `@axe-core/playwright` 4.10.2 is added as a dev dependency, for Q6.

---

🤖 Generated with [Claude Code](https://claude.com/claude-code)

AI-Fingerprint: sha256:be35fe70e880
