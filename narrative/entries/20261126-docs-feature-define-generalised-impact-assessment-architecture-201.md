---
date: 2026-11-26
slug: docs-feature-define-generalised-impact-assessment-architecture-201
title: "docs(feature): define generalised impact assessment architecture (#201)"
summary: "- One .NET impact engine, `RuntimeKnowledge.Impact`, serves both the command line and the workspace. `npm run c4` keeps its flags and calls a .NET tool. ADR 0028 supersedes ADR 0001."
kind: product
status: accepted
sequence: 2026-11-26T21:13:50.000Z
evidence: "https://github.com/jamiemitchellconsultants/division-ontology/pull/211; merge commit 923e242cb2120b4fec576b7a2b06684fe1c67179"
---

## Context

The C4 pilot's impact engine is JavaScript and runs only as a local command (ADR 0001). The behaviours accepted in #208 need the same assessment in the .NET workspace, with Markdown byte-identical to the command line's (B23). They also need cited page text kept behind sign-in (B29), and saved assessments that stay readable after a snapshot change (B34). The relationship master list (#205) does not exist yet.

## Decision

- One .NET impact engine, `RuntimeKnowledge.Impact`, serves both the command line and the workspace. `npm run c4` keeps its flags and calls a .NET tool. ADR 0028 supersedes ADR 0001.
- Node keeps checking the inputs, and packages the checked impact input and the master list into the snapshot bundle. The bundle is the engine's only input (ADR 0029).
- The worker stores an impact model for each snapshot, with the cited evidence lines, in PostgreSQL. The API serves it only from signed-in routes (ADR 0030).
- A page's title and Confluence address come from YAML front matter (`title`, `confluence_url`) (ADR 0031).
- The master list lives at `relationships/master-list.json` and is pinned by the catalog commit. The engine reads only `iri` and `impact` (ADR 0032).
- A saved assessment is stored as a journal event with its frozen content and its rendered Markdown (ADR 0033).
- Drafts live in browser memory, tied to their snapshot. The server refuses a changed snapshot (ADR 0034).

Alternatives rejected:
- porting the engine to C# while keeping a second JavaScript implementation alongside it;
- running the JavaScript engine inside .NET with Jint;
- running a Node sidecar service;
- reading the cited text from object storage on every request;
- storing only the record and rebuilding the rest when it is opened.

## Consequences

- Architects need the .NET SDK, as well as Node, to run the command line.
- Before the Node engine modules are removed, today's outputs on the C4 fixtures must be captured as a set of expected results (B4).
- Path ids must be serialised exactly as `JSON.stringify` does.
- Bundles built before this feature give `impactUnavailable`.
- #205 must keep the master list's location and its four impact values.
- The Q1 targets and the cold-start time on Lambda have not been measured yet.
- ADR 0001 still reads "accepted", because the gate check cannot yet change another feature's ADR. That is parked as #210.

---

🤖 Generated with [Claude Code](https://claude.com/claude-code)

AI-Fingerprint: sha256:778f8e2704eb
