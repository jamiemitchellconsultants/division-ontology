# Knowledge Graph Explorer Implementation Plan

> **For agentic workers:** Use `superpowers:executing-plans` or `superpowers:subagent-driven-development` to build this plan task by task after the plan PR is merged. Steps use checkbox syntax for tracking. Read the matching task in the detailed plan before writing its tests.

**Goal** Build an authorized, one-hop graph explorer for following relationships, inspecting evidence, and maintaining workspace contributions.

**Architecture** A Blazor route owns navigation, one-hop paging, and inspector state. Cytoscape.js and native HTML controls render the same loaded records. The inspector uses the existing API, editors, and draft storage.

**Tech stack** .NET 10.0.402 with the `runtime/global.json` patch policy, Blazor WebAssembly, Cytoscape.js 3.35.0, xUnit and bUnit, and the existing Node.js and Playwright workspace harness. [Pinned upstream release](https://github.com/cytoscape/cytoscape.js/releases/tag/v3.35.0)

**Spec** [Vision](../../../features/knowledge-graph-explorer/01-vision.md) · [Behaviours B1–B15](../../../features/knowledge-graph-explorer/02-behaviours.md) · [Architecture and ADRs 0021–0023](../../../features/knowledge-graph-explorer/03-architecture.md) · [Detailed implementation plan](../../../detailed-implementations/knowledge-graph-explorer/plan.md)

## Global constraints

- Work on `feature/knowledge-graph-explorer/dev-build` once this plan has been reviewed and merged and its build gate opens. Each task starts with its focused failing tests, ends with a reviewed diff, and has its own commit and push.
- Use `POST /api/traverse` at `maxDepth: 1` with `includeUnavailable: false`. Start with `both`. Submit `nextCursor` as `cursor` with the same query parameters, including when the preceding page was empty.
- Use published keys for entities, and distinct published keys and runtime IDs for relationships. Keep direction from `From` to `To` and display origin in text.
- Keep Back history in the open explorer's memory. Keep rejected contribution drafts in the existing tab draft store. The API enforces read and write access and owns all contribution persistence.
- **Every user edit goes only to the runtime workspace.** Turtle files in the adopting repository and all source repositories are read-only inputs. Use existing runtime contracts, API operations, and stores. Keep generated catalog artifacts on their existing sync path.

## File map

| Task | Product files | Tests |
| --- | --- | --- |
| 1 | `runtime/RuntimeKnowledge.Web/Services/GraphNeighbourhood.cs` | `runtime/RuntimeKnowledge.Tests/GraphNeighbourhoodTests.cs` |
| 2 | `Pages/GraphExplorer.razor`, `Components/GraphNeighbourList.razor`, `Services/WorkspaceClient.cs`, `Services/WorkspaceLinks.cs`, `Pages/Explore.razor`, `Pages/Entity.razor` under `runtime/RuntimeKnowledge.Web`, plus `deploy/workspace/aws/cdn.tf` | `runtime/RuntimeKnowledge.Tests/GraphTestContext.cs`, `runtime/RuntimeKnowledge.Tests/GraphExplorerNavigationTests.cs`, `test/graph-explorer-route.test.mjs` |
| 3 | `Components/GraphDiagram.razor`, `Pages/GraphExplorer.razor`, `wwwroot/graph-diagram.mjs`, `wwwroot/workspace.css`, and `wwwroot/vendor/cytoscape/3.35.0/` under `runtime/RuntimeKnowledge.Web`, plus `deploy/workspace/aws/storage.tf` | `test/graph-explorer-renderer.test.mjs`, `runtime/RuntimeKnowledge.Tests/GraphTestContext.cs`, `runtime/RuntimeKnowledge.Tests/GraphExplorerNavigationTests.cs`, `deploy/workspace/aws/tests/workspace.tftest.hcl`, `deploy/workspace/aws/tests/fixture/graph-diagram.mjs` |
| 4 | `Components/GraphInspector.razor`, `Components/OriginEvidence.razor`, `Services/GraphEvidenceLinks.cs`, `Pages/GraphExplorer.razor`, `Program.cs`, and `wwwroot/appsettings.json` under `runtime/RuntimeKnowledge.Web`, plus `deploy/workspace/aws/storage.tf`, `deploy/workspace/aws/variables.tf`, `deploy/workspace/home-lab/compose.yml`, and `deploy/workspace/home-lab/verify-config.sh` | `runtime/RuntimeKnowledge.Tests/GraphInspectorTests.cs`, `runtime/RuntimeKnowledge.Tests/GraphTestContext.cs`, `runtime/RuntimeKnowledge.Tests/GraphExplorerNavigationTests.cs`, `deploy/workspace/aws/tests/workspace.tftest.hcl` |
| 5 | `Components/GraphInspector.razor`, `Components/ContributionEditor.razor`, `Components/ApiError.razor`, `Services/WorkspaceLinks.cs`, and `Pages/GraphExplorer.razor` under `runtime/RuntimeKnowledge.Web` | `runtime/RuntimeKnowledge.Tests/GraphExplorerContributionTests.cs`, `runtime/RuntimeKnowledge.Tests/GraphTestContext.cs` |
| 6 | `deploy/workspace/verify-graph-explorer.mjs`, `deploy/workspace/verify-browser.mjs`, and `deploy/workspace/local/smoke.sh` | The added browser scenario runs in the existing signed-in workspace harness. |

## Review focus

1. Two sources publish **Order** and both a published edge and a runtime edge carry the same label and endpoints. Both nodes and both edge IDs survive append and remain independently selectable (Tasks 1, 3, 4 and 6).
2. A filtered-only page has zero edges and a continuation, while a later final page can also be empty. The view keeps **Load more** only while it has a continuation and preserves loaded edges (Tasks 1 and 2).
3. A late traversal or detail response arrives after another focus or selection wins. The visible view and Back history remain tied to the winning action (Tasks 2 and 4).
4. An observed snapshot or journal change invalidates a view with loaded pages. Reload replaces the saved neighbourhood and keeps its focus and direction. A failed read after saving still shows the successful write (Tasks 2 and 5).
5. A user opens the graph directly on CloudFront or returns to it after sign-in. The route serves the Blazor entry point. The S3 object for every `.mjs` file carries a JavaScript content type, so the module loads on AWS as well as behind Caddy. The resume key restores only an authorized editor (Tasks 2, 3, 5 and 6).
6. The traversal API reports coverage gaps for the whole snapshot. The explorer shows only gaps for loaded sources and nodes as coverage of this result. It counts the rest as gaps elsewhere, and it never calls a node isolated while a relevant gap exists (Tasks 1, 2 and 6).
7. A single tap or **Inspect** on a neighbour only selects it. Only a double tap or an explicit **Follow** traverses (Tasks 2, 3, 4 and 6).
8. Evidence repository links reach every environment that configures them: local `appsettings.json`, AWS `web_settings`, and the home-lab `web-settings` config. Unconfigured or invalid roots fall back to the plain citation (Tasks 4 and 6).

## Tasks

### Task 1 — Project traversal pages into a graph neighbourhood

**Interfaces** Add `GraphQuery`, `GraphEdge`, and `GraphNeighbourhood.FromPage` and `Append` in `GraphNeighbourhood.cs`. The component reads ordered edges, node keys, gaps scoped into `RelevantGaps` and `OtherGaps`, and continuation from the resulting neighbourhood.

- [ ] Add `GraphNeighbourhoodTests` for same-labelled sources, parallel identities, stored arrow direction, empty intermediate and final pages, and gap scoping by loaded source and node.
- [ ] Run the `GraphNeighbourhoodTests` filter. Expect the projection interface to be missing.
- [ ] Implement the projection and page-merge rules from the detailed plan.
- [ ] Run the focused tests and review the diff.
- [ ] Commit `feat: project one-hop workspace graph pages` and push.

### Task 2 — Wire entry links, navigation, and accessible paging

**Interfaces** Consume `GraphNeighbourhood`. Add `WorkspaceLinks.Graph(PublishedKey)`, the authorized `graph/{Key}` page, and `GraphNeighbourList`. The list has a follow callback and a single inspect callback that takes a node or edge identity. Add optional read cancellation in `WorkspaceClient`. Add `GraphTestContext`, the one place graph tests register services.

- [ ] Add navigation and CloudFront route tests for entry identity, direction, cursor parameters, empty and stale pages, scoped coverage states, inspect-without-follow, Back, and superseded reads.
- [ ] Run `GraphExplorerNavigationTests` and `node --test test/graph-explorer-route.test.mjs`. Expect missing entry/navigation and the CloudFront graph rewrite to fail.
- [ ] Implement the page, list, request coordination, recovery actions, and graph rewrite. Keep existing entity and search routes usable.
- [ ] Rerun both focused suites and the existing `WorkspaceUiTests` filter.
- [ ] Commit `feat: navigate paged graph neighbourhoods` and push.

### Task 3 — Draw the shared neighbourhood with Cytoscape

**Interfaces** Add `GraphDiagram` and the scoped `graph-diagram.mjs` bridge. Expose the same follow and inspect actions as the list: a tap inspects and a double tap on a neighbour follows. Also expose viewport capture, restore, zoom, and the `diagramFor(host).renderedPosition(id)` hook for the browser harness. Map `.mjs` to a JavaScript content type in the AWS web bucket.

- [ ] Add renderer tests for pinned bytes, a browser-global-free import, stable positions across append, and tap-to-inspect and double-tap-to-follow actions. Add a startup-failure bUnit test and the Terraform `.mjs` content-type assertion.
- [ ] Run `node --test test/graph-explorer-renderer.test.mjs`. Expect the module and pinned asset to be missing.
- [ ] Vendor the verified ESM and license and implement the component and bridge. Treat a null or failing module as diagram startup failure, keeping the native controls.
- [ ] Rerun the renderer and UI tests and `verify-terraform.sh`. Publish the web project and verify the asset and license in the published output.
- [ ] Commit `feat: render workspace graph with local Cytoscape` and push.

### Task 4 — Inspect published and workspace records

**Interfaces** Add `GraphInspector` over entity and relationship detail operations for any selected node or edge, and `GraphEvidenceLinks.TryCreate`. Both list and diagram select an exact record by identity. Add the `GraphEvidenceRepositories` map from configuration, and carry it into AWS `web_settings` and the home-lab `web-settings` config.

- [ ] Add inspector tests for two parallel edges, a selected neighbour with **Follow**, missing and pinned evidence links (including `division-core`), loaded-view preservation, and responses for superseded selections. Add the Terraform evidence-map runs.
- [ ] Run `GraphInspectorTests`. Expect the missing panel and evidence service.
- [ ] Implement detail selection, provenance and workspace history, optional source URL mapping in every environment, and conflict-free async reads.
- [ ] Rerun the inspector and navigation tests, `verify-terraform.sh`, and the home-lab config check with and without a map.
- [ ] Commit `feat: inspect graph relationship evidence` and push.

### Task 5 — Reuse workspace editing and reauthentication

**Interfaces** Extend the inspector with existing editor modes and a graph save callback. Add a graph-specific same-origin resume link that contains the focus, direction, editor action, and optional record ID.

- [ ] Add contribution tests for reader and manager controls, failure and resume, stale versions, and write success followed by a failed read.
- [ ] Run `GraphExplorerContributionTests`. Expect missing contribution controls and save/recovery handling.
- [ ] Compose existing `ContributionEditor`, `DraftState`, and `InventoryChoices` in the inspector. Restart the live neighbourhood on success and keep saved-record details accessible by ID.
- [ ] Rerun graph and workspace UI tests. Verify existing editor callers still sign in through their current route.
- [ ] Commit `feat: edit runtime contributions from the graph` and push.

### Task 6 — Verify the shipped browser experience

**Interfaces** Add the graph scenario to the existing initial browser smoke. Keep the synthetic checkouts in the local smoke's disposable scenario directory through the before/after input comparison. Journal-writing scenarios always run on Mac, but on home lab only with `WORKSPACE_VERIFY_GRAPH_WRITES=1`, and each skip is reported.

- [ ] Add the focused browser checks: the Order Fulfilment route; real canvas inspect and follow, using the rendered-position hook; keyboard actions; evidence links and scoped coverage per environment; continuation over 101 seeded relationships; stale reload; and contribution visibility for a separate reader. Include the before/after snapshot and repository checks and the direct route and module response checks.
- [ ] Run the new browser checks against the pre-feature `main` build and confirm failure at the missing graph route or asset.
- [ ] Wire the scenario into the existing local and home-lab browser harness. For local B15, supply the exact checkouts that built the published synthetic bundle.
- [ ] Run the integrated local checks and package verification listed in the detailed plan. Record deployed results only for environments exercised.
- [ ] Commit `test: verify graph explorer in workspace browser smoke` and push.

## Behaviour coverage

| Behaviours | Tasks and proof |
| --- | --- |
| B1–B2 | Tasks 1, 2, 3 and 6 verify identity, entry, arrows and the Order Fulfilment path. |
| B3–B5 | Tasks 2, 3 and 6 verify Back, viewport, keyboard navigation, request order, and retry. |
| B6–B7 | Tasks 1, 2, 3, 4 and 6 verify distinct identities, node and edge selection without following, origin, evidence links in each configured environment, and contribution history. |
| B8–B10 | Tasks 1, 2, 3 and 6 verify append, empty and final pages, coverage scoped to loaded sources, stale reload and transient retry. |
| B11–B14 | Tasks 5 and 6 verify manager controls, API refusals, draft recovery, save and subsequent reader visibility. |
| B15 | Task 6 verifies that the active snapshot is unchanged and compares Turtle bytes and repository revisions across the scenario. Task 5 uses only the existing contribution API, so no write path reaches repository inputs. |
