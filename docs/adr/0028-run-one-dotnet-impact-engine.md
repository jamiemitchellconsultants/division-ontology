---
status: accepted
date: 2026-11-26
feature: 198
supersedes: [0001]
---

# 0028 — Run one .NET impact engine for the command line and the workspace

Feature: docs/features/generalise-impact-assessment/

## Context

The C4 pilot's search, paths, views, assessment validation and Markdown rendering are JavaScript in `src/c4`, and they run only as a local Node command (ADR 0001). The workspace API and worker are .NET, deployed as containers on Mac and the home lab and as Lambda functions on AWS. Analysts must assess in the workspace (B15–B22). B23 and Q3 require byte-identical Markdown and JSON from the command line and the workspace for the same inputs. Architects must keep assessing from checkouts (B24).

## Decision

Implement the impact rules once, in a .NET library, `runtime/RuntimeKnowledge.Impact`, with no database or network dependencies. The command line, the worker and the API all use it.

- `npm run c4` stays the entry point and keeps today's commands and flags. Its Node front end verifies the checkouts and packages the inputs (ADR 0029). It then runs `runtime/RuntimeKnowledge.Impact.Cli` on that package, and the tool writes the outputs.
- The pilot's other commitments carry over: pinned Git inputs, no hosted service needed for the command line, deterministic outputs that name every revision, and editable local records.
- The Node engine modules (`candidates`, `paths`, `project`, `render`, `assessment`, `feedback`, `code-evidence`) are removed. That happens only after a conformance corpus captured from them on the C4 fixtures passes against the .NET engine.

## Consequences

- There is one implementation of every impact rule, so the command line and the workspace cannot drift apart.
- Architects need the .NET SDK pinned by `runtime/global.json` as well as Node. The first run builds the tool.
- Path ids and digests must be serialised exactly as ECMAScript `JSON.stringify` does, so that today's C4 path ids are unchanged (B37).
- The corpus that proves B37 must be captured before the Node engine is removed, because there is nothing to compare against afterwards.

## Alternatives considered

- **Port to C# and keep the Node engine, held together by a shared conformance corpus.** This means two implementations, and every rule change has to land twice. The user preferred a single engine.
- **Embed the JavaScript engine in .NET with Jint.** This keeps one implementation, but it needs the engine untangled from `node:crypto`, `n3` and Git. It also puts Q1 at risk on 50,000 assertions.
- **A Node sidecar service for impact.** This adds a second runtime, a deployment unit and an authentication hop in all three environments.
