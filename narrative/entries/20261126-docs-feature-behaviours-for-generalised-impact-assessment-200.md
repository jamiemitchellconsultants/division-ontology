---
date: 2026-11-26
slug: docs-feature-behaviours-for-generalised-impact-assessment-200
title: "docs(feature): behaviours for generalised impact assessment (#200)"
summary: "Decided with the user on 26 November 2026: - Readers search, trace, draft and export. Saving and editing need `ManageKnowledgeGraph`."
kind: product
status: accepted
sequence: 2026-11-26T20:00:55.000Z
evidence: "https://github.com/jamiemitchellconsultants/division-ontology/pull/208; merge commit 055f08b64ca0acd2a7505d13e580e8617087505f"
---

## Context

The accepted vision (PR #206) passed four questions to the analyst:
- who may save an assessment;
- how impact crosses `exact` and `specializes` links;
- what makes a lead supported;
- what happens to assessments when a new snapshot is activated.

It also left an unstated conflict. Outcome 2 says unlisted relationships are not followed, but the relationship master list (#205) does not exist yet. Applied strictly, that would stop every path, including on the C4 fixtures, which must behave as today.

## Decision

Decided with the user on 26 November 2026:
- Readers search, trace, draft and export. Saving and editing need `ManageKnowledgeGraph`.
- Until a master list exists, paths follow every assertion from subject to object, as today, and report a `no-relationship-master-list` gap.
- `exact` links are crossed in both directions. `specializes` links are crossed only from the general term to its specialisations.
- A lead is supported by a C4 mapping, or by verified evidence of any role.
- Saved assessments keep a frozen copy of what they cite and stay readable after a new snapshot, but cannot be edited. Unsaved drafts are discarded.

The analyst also decided that:
- workspace and command-line Markdown are byte-identical;
- a relationship listed as carrying no impact is counted, not reported as a gap;
- a link crossing counts as one depth step;
- workspace search is cursor-paged;
- deleting and retaining saved assessments is deferred.

Rejected: following nothing until #205 lands, crossing both link kinds both ways, and counting only certain evidence roles.

## Consequences

Outcome 2 no longer blocks the other outcomes. On the C4 fixtures, B4 allows exactly three differences from today: extra support flags, paths from evidenced starts that had no mapping, and the master-list gap. The product owner should confirm that this reading of "same results as today" is acceptable. The architect must decide:
- how the runtime serves cited text from the pinned bundle without publishing it;
- how saved assessments freeze their content;
- how page titles and Confluence addresses are read from exported pages.

---

🤖 Generated with [Claude Code](https://claude.com/claude-code)

AI-Fingerprint: sha256:0651f3b80c6f
