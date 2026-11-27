---
date: 2026-11-27
slug: docs-feature-amend-impact-assessment-behaviours-for-pushback-200
title: "docs(feature): amend impact assessment behaviours for pushback (#200)"
summary: "- B4 is retired. B37 lists every allowed difference, including B30's gap sections for every assessment, schema 1 records included. The user chose this over exempting schema 1 records."
kind: product
status: accepted
sequence: 2026-11-27T05:36:00.000Z
evidence: "https://github.com/jamiemitchellconsultants/division-ontology/pull/213; merge commit 05d1297070755a4a6a8e0735fa600cdeb1425498"
---

## Context

Feature-analyze found that B4's list of allowed differences on the C4 fixtures did not match three other behaviours:
- B30, which adds gap sections to every assessment;
- B5, which adds `no-c4-mapping`;
- B3, which changes a reason text.

It also found three things left open:
- what makes a cited file an exported page;
- what it means for evidence to cite a node;
- how often `unlinked-term` is reported.

The developer's first task is to capture today's C4 outputs as the fixed baseline the new engine must match, and that baseline depends on all of these.

## Decision

- B4 is retired. B37 lists every allowed difference, including B30's gap sections for every assessment, schema 1 records included. The user chose this over exempting schema 1 records.
- An exported page is a cited Markdown file that opens with a front-matter block. Any other cited file is an ordinary document, shown like code evidence (B38). The user chose this over requiring `confluence_url`, and over treating every cited file as a page.
- Evidence cites a node when the node carries it, or when the node is the subject or object of an evidenced assertion.
- `unlinked-term` is reported once for each source and term.
- `no-c4-mapping` replaces `unsupported-focus` only for a node with no mapping at all.
- `ImpactPath` now starts from a supported lead chosen as a start, not a confirmed one.

## Consequences

- Any change to C4 output that is not in B37's list now fails the baseline comparison.
- Repository ADRs and feature documents cited as evidence no longer produce page-metadata gaps.
- The architect must amend ADR 0031 to apply only to exported pages, and restrict the successor route to managers as B35 says. 03-architecture.md must trace B37 and B38.

---

🤖 Generated with [Claude Code](https://claude.com/claude-code)

AI-Fingerprint: sha256:15ca906dca66
