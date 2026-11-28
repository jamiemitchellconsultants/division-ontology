---
date: 2026-11-28
slug: docs-feature-amend-impact-assessment-behaviours-for-the-pr-220-review-pu
title: "docs(feature): amend impact assessment behaviours for the PR #220 review pushback (#200)"
summary: "- A reviewed-link crossing substantiates a likely impact on the node it arrives at. Its links and catalog commit are the support, and no evidence IRI is needed (B39)."
kind: product
status: accepted
sequence: 2026-11-28T08:16:00.000Z
evidence: "https://github.com/jamiemitchellconsultants/division-ontology/pull/228; merge commit 8f4ac9c510b3eda58e216f1f69ac66f2c12ce2d8"
---

## Context

The PR #220 review found three cases the behaviours did not settle:
- A path can reach a node through a reviewed-link crossing, which carries reviewed links and a catalog commit but no evidence IRI. B17's validation then refuses every likely impact on that node.
- During a trace the workspace shows code and ordinary-document lines, but a saved assessment kept only page lines. B34 promises the evidence excerpts exactly as saved.
- B30 says an assessment carries the gaps of the searches it cites, but a record cites paths, never a search.

## Decision

- A reviewed-link crossing substantiates a likely impact on the node it arrives at. Its links and catalog commit are the support, and no evidence IRI is needed (B39). The user chose this over requiring the assessor to trace on to an evidenced assertion.
- A saved assessment freezes every cited excerpt shown during the trace: page, code and ordinary-document lines. The export still prints page lines only (B34). The user chose this over keeping page lines only.
- The searches an assessment cites are the ones that returned its confirmed and rejected leads. It carries the gaps search reports about those leads, recomputed at its pinned snapshot, and the record is unchanged (B30). The user chose this over recording search queries in the record, and over dropping search gaps.

## Consequences

- The engine, the draft editor and the command line accept an impact supported only by a crossing, and show the crossing as its support.
- Saved assessments grow by the cited code and ordinary-document lines. Those lines stay inside the signed-in workspace (B29).
- Freezing recomputes lead gaps from the record's confirmed and rejected leads.
- The C4 fixtures have no reviewed links, so B37's baseline is unaffected.
- 03-architecture.md must trace B39 to the impact engine and the assessment pages.

---

🤖 Generated with [Claude Code](https://claude.com/claude-code)

AI-Fingerprint: sha256:ded4efbaa3f5
