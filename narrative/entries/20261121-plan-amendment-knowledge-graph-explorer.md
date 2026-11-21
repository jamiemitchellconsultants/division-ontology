---
date: 2026-11-21
slug: plan-amendment-knowledge-graph-explorer
title: "Plan amendment: Knowledge Graph Explorer"
summary: "- Map `.mjs` to `text/javascript; charset=utf-8` in `local.web_types`, and assert it in `workspace.tftest.hcl`. - Thread an optional, validated `evidence_repositories` map through AWS `web_settings` and the home-lab `web-settings` config."
kind: product
status: accepted
sequence: 2026-11-21T20:30:31.000Z
evidence: "https://github.com/jamiemitchellconsultants/division-ontology/pull/169; merge commit 9c223d6b2a70b1ee820880dfb4e5b8e9aaecd0f5"
---

## Context

The plan in #167 merged before review. The review found one defect that would block the build: the S3 web bucket has no content type for `.mjs`, so on AWS both graph modules would be served as `application/octet-stream` and browsers would refuse to run them. It also found these gaps:

- The evidence repository map never reaches the AWS or home-lab web settings.
- The traversal API returns coverage gaps for the whole snapshot, but the plan treated them as gaps for the focused node.
- The plan gives neighbour nodes no inspect action, and a single tap on a node follows it at once.
- The canvas pointer test has no way to find nodes on the canvas.
- There are smaller test-harness and verification weaknesses.

## Decision

- Map `.mjs` to `text/javascript; charset=utf-8` in `local.web_types`, and assert it in `workspace.tftest.hcl`.
- Thread an optional, validated `evidence_repositories` map through AWS `web_settings` and the home-lab `web-settings` config. Key links on the record's own provenance source. This includes `division-core`; reviewed links have no evidence to link.
- Scope gaps in the projection. A gap is relevant when it is for a loaded source, and for a loaded node or the whole source. Other gaps are counted as gaps elsewhere in the division. The explorer never says a node is isolated while a relevant gap exists.
- Use one graph identity for selection. **Inspect** in the list, or a tap in the diagram, selects a node or edge. Only **Follow** or a double tap on a neighbour traverses. The inspector loads entity detail for any selected node.
- Add `diagramFor(host).renderedPosition(id)` for the browser harness, with no `window` global.
- Graph tests share one `GraphTestContext`. A null or failing module counts as diagram startup failure. The bridge must import in Node without browser globals.
- B15 also asserts that the active snapshot is unchanged.
- Journal-writing scenarios always run on Mac, but on home lab only with `WORKSPACE_VERIFY_GRAPH_WRITES=1`. The busy-node seed is 101 relationships.

## Consequences

AWS has a test-enforced module content type, and evidence links work in every environment that configures a map. Coverage notices describe the loaded result rather than the whole division. Tapping the diagram no longer navigates away by accident. The canvas browser checks use computed positions instead of pixels. Later tasks can add services without breaking earlier tests. Home-lab runs no longer add permanent journal history unless someone opts in. Deployed paging and authorization stay covered by `verify-traversal-paging.mjs home-lab`.

The build (#150) traces to every task. The changed interfaces are `GraphNeighbourhood` gap fields, `OnInspect(string id)` replacing `OnInspectEdge`, `GraphInspector`'s `SelectedId`, the bridge's `diagramFor`/`renderedPosition`, `GraphEvidenceRepositories`, and `verifyGraphExplorer`'s `writes`. The user-phase acceptance (#151) should note the opt-in for home-lab writes and the per-environment evidence-link outcomes.

- [Task plan](https://github.com/jamiemitchellconsultants/division-ontology/blob/feature/knowledge-graph-explorer/dev-plan-amend/docs/superpowers/plans/knowledge-graph-explorer/plan.md)
- [Detailed implementation plan](https://github.com/jamiemitchellconsultants/division-ontology/blob/feature/knowledge-graph-explorer/dev-plan-amend/docs/detailed-implementations/knowledge-graph-explorer/plan.md)

🤖 Generated with [Claude Code](https://claude.com/claude-code)

AI-Fingerprint: sha256:f9f662b312e4
