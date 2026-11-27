---
date: 2026-11-27
slug: docs-feature-amend-impact-assessment-architecture-for-pushback-201
title: "docs(feature): amend impact assessment architecture for pushback (#201)"
summary: "- The traceability table now covers B37 and B38. Comparing against the C4 baseline fails on any difference outside B37's list. - The engine reports one `unlinked-term` gap for each source and term, listing the nodes in sorted order."
kind: product
status: accepted
sequence: 2026-11-27T05:42:00.000Z
evidence: "https://github.com/jamiemitchellconsultants/division-ontology/pull/215; merge commit bc5715dffa42f9de0623732739ce1c449199f5ff"
---

## Context

The behaviours amendment (#213) made four changes that the architecture had to follow:
- it retired B4 in favour of B37 and added B38;
- it defined an exported page as a cited Markdown file that opens with a front-matter block;
- it limited `unlinked-term` to one gap for each source and term;
- it narrowed `no-c4-mapping` to a focus with no mapping at all.

Feature-analyze had also found that the successor route was open to readers, while B35 says a manager starts a new assessment from a saved one.

## Decision

- The traceability table now covers B37 and B38. Comparing against the C4 baseline fails on any difference outside B37's list.
- The engine reports one `unlinked-term` gap for each source and term, listing the nodes in sorted order.
- `no-c4-mapping` applies only to an unmapped focus. A wrong-level mapping keeps `unsupported-focus`.
- ADR 0031 is amended in place, not superseded, because its decision is unchanged, only narrowed, and none of it has been built. Page metadata is read only for exported pages, and ordinary documents keep code evidence's fields with no gap.
- `POST /api/assessments/{id}/successor` needs `ManagePolicy`.

## Consequences

- Repository ADRs and feature documents cited as evidence raise no page-metadata gaps.
- Path results stay bounded on divisions that have no reviewed links.
- Readers who are not managers can still draft from scratch, but cannot start a draft from a saved assessment.

---

🤖 Generated with [Claude Code](https://claude.com/claude-code)

AI-Fingerprint: sha256:01f18c928c91
