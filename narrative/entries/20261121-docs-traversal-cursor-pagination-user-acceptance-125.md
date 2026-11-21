---
date: 2026-11-21
slug: docs-traversal-cursor-pagination-user-acceptance-125
title: "docs: traversal cursor pagination user acceptance (#125)"
summary: "- **Deployed seeding.** Deployed acceptance creates its busy node through the manager API: 105 relationships, removed after each run. It does not publish a dedicated bundle or settle for small-walk checks."
kind: product
status: accepted
sequence: 2026-11-21T16:08:41.000Z
evidence: "https://github.com/jamiemitchellconsultants/division-ontology/pull/159; merge commit f441b4a834ea66a924f0fa75f524756d11b70187"
---

## Context

The User phase must show every traversal-paging behaviour from outside, in a local build and in the deployed home-lab workspace. The home-lab synthetic fixture has no node with more than 100 traversal paths, so paging could not be exercised there as it stood. B13's wording names an absolute workspace link relation, while the build emits the registered `rel="next"`.

## Decision

- **Deployed seeding.** Deployed acceptance creates its busy node through the manager API: 105 relationships, removed after each run. It does not publish a dedicated bundle or settle for small-walk checks. The active snapshot is never changed, and the removals stay in contribution history.
- **B13.** `rel="next"`, which `x-agent-discovery.linkRelations` maps to `traverseGraph`, is accepted as meeting B13. The behaviour text is not amended.
- **Local-only coverage.** The deployed criterion is accepted with B10, B17 and B19 covered only by the local suite. A snapshot change on the shared fixture, more than 1,000 filtered rows, and the real cursor limit are not reasonable to exercise on the home lab.
- **How acceptance is run.** Where the expected result is known, a walkthrough is an automated run that reports only misses. The user approves the executable tests from a plain-language summary rather than from code.

## Consequences

- Every home-lab acceptance run now writes and removes 105 relationships, so the runner must target the synthetic acceptance fixture only. The runbook says so, and `--cleanup` recovers from an interrupted run.
- The deployed run gives no evidence for snapshot staleness, the filtered-row budget or the oversized-frontier case. Those rely on the local tests.
- If B13's wording is ever tightened, the acceptance record already states the interpretation.

🤖 Generated with [Claude Code](https://claude.com/claude-code)

AI-Fingerprint: sha256:ad89f237e22e
