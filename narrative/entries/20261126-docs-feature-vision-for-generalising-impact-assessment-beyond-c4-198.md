---
date: 2026-11-26
slug: docs-feature-vision-for-generalising-impact-assessment-beyond-c4-198
title: "docs(feature): vision for generalising impact assessment beyond C4 (#198)"
summary: "Repurpose #198 to generalise impact assessment beyond C4, in full mode. Any validated source graph can be assessed, with C4 views as an optional layer."
kind: product
status: accepted
sequence: 2026-11-26T19:04:36.000Z
evidence: "https://github.com/jamiemitchellconsultants/division-ontology/pull/206; merge commit 565d72a3f11dd89dc4d18c2675ce02e6a74cdc8a"
---

## Context

Issue #197 asked for source knowledge graphs to be imported into the catalog alongside the ontology Turtle, for the Confluence ontology import's group repositories. Product owner research found that the workspace bundle and the C4 pilot already read and validate source graphs from pinned checkouts, and the workspace already projects graph claims. A catalog-only import would add little. The C4 engine is mostly generic, but impact paths start only from C4-mapped nodes, run only from a local command with source checkouts, and treat code as the evidence that matters. That blocks impact assessment over policy and process knowledge preserved from Confluence.

## Decision

Repurpose #198 to generalise impact assessment beyond C4, in full mode. Any validated source graph can be assessed, with C4 views as an optional layer. Impact follows each relationship's meaning from the division's relationship master list, and crosses sources only through steward-reviewed links. Analysts and architects assess, save and share assessments in the runtime knowledge workspace, with cited document evidence shown from the pinned commit. Assessments stay pinned to their snapshot, carry their gaps and have no approval state. Rejected: importing graphs into the public catalog first (#197 set aside), following every relationship without impact meaning, and building the master list inside this feature.

## Consequences

The relationship master list becomes a prerequisite feature (#205), and outcome 2 cannot be accepted until it exists. #197 stays open, set aside. The analyst must decide who may save an assessment and how impact crosses `exact` and `specializes` links. The architect must decide how the workspace serves cited text from the pinned bundle, and how to keep C4 behaviour identical on existing fixtures.

---

🤖 Generated with [Claude Code](https://claude.com/claude-code)

AI-Fingerprint: sha256:8beafab92c89
