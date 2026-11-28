---
status: accepted
date: 2026-11-28
feature: 198
---

# 0035 — Record the traces an assessment cites

Feature: docs/features/generalise-impact-assessment/

## Context

B17 says a likely impact may cite only paths and evidence from the traces the assessor ran, and that the command line applies the same validation. A record stores cited paths by id only. To check them, preview, save, edit and the command line re-trace the paths from the record's confirmed leads at depth 4. That re-trace only works when the assessor traced from exactly the leads they later confirmed, at the default depth. A path from a depth 5 or 6 trace, or from a start the assessor did not confirm, fails with `unresolved path` on the same snapshot. The PR #220 review found this (item 4).

Path ids are digests of their steps, so a path can be checked only by re-tracing it against the pinned model. The validator therefore needs to know which traces to rerun.

## Decision

A schema 2 record may carry `traces`: a list of the traces its cited paths came from, each with its `starts` (source and node keys) and its `maxDepth` (1 to 6). Validation re-traces every listed trace against the record's snapshot and takes the union of their paths as the citable paths. A record without `traces` keeps today's rule: one trace from its confirmed leads at depth 4. Existing records, and the command line's records written by hand, are therefore unchanged.

The browser adds each trace a draft takes paths from to `traces`. It does this when the draft starts from a trace and when it takes more paths from a later one. The engine applies the same rule for the command line, the workspace and freezing, so B23 still holds. `traces` is part of the frozen record and the digest. A record lists at most 5 traces, each with at most 20 starts, and anything larger is refused as `invalidAssessment`. A draft that already lists 5 traces does not take paths from a sixth: the page says so, and the assessor starts another assessment for the rest.

**Amended on 28 November 2026.** The cap was 20 traces. Measured on the Q1 fixture, a first preview at 20 traces of 20 starts took 37.8 s, above API Gateway's 30 s limit, so the cap is now 5 traces (pushback on #201 from the I7 build).

## Consequences

- A record can cite any path the assessor was shown on its snapshot, whatever its depth or starts.
- Validation reruns up to 5 traces, each once per request. On the Q1 fixture a trace of 20 starts takes about 1.9 s, so a worst-case preview, save or edit takes about 10 s, inside the 30 s that API Gateway allows on AWS. The developer measures the worst case in the Q1 fixture.
- The saved assessment shows which traces it cites, with their starts and depth.
- Schema 1 records have no `traces` and keep the pilot's rule.

## Alternatives considered

- **A cap of 20 traces.** Chosen first, and measured at 37.8 s for a worst-case first preview, which fails on AWS.

- **Keep today's rule and restrict the browser.** Only traces at depth 4 from confirmed leads could be used. This is simpler, but it hides depth 5 and 6 traces from assessments and makes the assessor confirm every start before tracing.
- **Send the cited paths with the request and check each step.** The workspace could verify each path step by step against the model, with no re-trace. That is not possible for the command line, whose records hold path ids only, so the two would validate differently and B23 would break.
