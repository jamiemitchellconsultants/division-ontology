# Knowledge Graph Explorer acceptance

[Feature #145](https://github.com/jamiemitchellconsultants/division-ontology/issues/145) · [User phase #151](https://github.com/jamiemitchellconsultants/division-ontology/issues/151) · [Accepted behaviours](02-behaviours.md) · [Runbook](05-runbook.md)

## Prepare the walkthrough

Use the disposable Mac workspace, then run the same walkthroughs against the authorized home-lab fixture. Follow [Deploy](05-runbook.md#deploy) and record the application revision, active snapshot, date and operator. Open separate browser profiles for the reader and manager. The focus is the entity at the top of the graph. Use the graph's own **Back** button for navigation.

Capture the B15 input evidence before making any fixture writes. For B3, B4, B8 and B10, seed the busy Ecommerce **Order** node using the pagination fixture. Use `mac` locally and `home-lab` for the assigned deployed fixture. Keep the printed run label and pick a relationship from the last page to look for in the graph.

```bash
node deploy/workspace/verify-traversal-paging.mjs mac --seed-only
```

Clean up those seeded relationships using `--cleanup` after completing the walkthrough. Use unique labels for notes and relationships you create by hand and remove them when the scenario ends. Runs against a shared fixture need operator approval for journal writes.

For cases that need specific traversal or write responses, use an isolated test-browser interception of the fixture request. Simulate the response on that browser and leave the shared API and identity settings untouched. The named component tests in the executable coverage table define the controlled outcomes. Record a step as pending when its fixture or injected response is unavailable.

## Scripted walkthroughs

### B1 — Enter the graph at the chosen entity

1. Search for **Order** as a reader. Open **Graph** on the Ecommerce result. **Expect** the heading to name **Order · fixture/ecommerce**, and the loaded nodes to show labels and sources.
2. Open the Warehouse **Order** entity page and choose **Open graph**. **Expect** **Order · fixture/warehouse** and its own neighbourhood.

### B2 — Follow relationships with their original direction

1. Search for **What happens after a customer places an order?** and open the graph for **Order processing** in Ecommerce. **Expect** **Both** selected and direct connections.
2. Choose **Incoming** and follow the containment connection to **Storefront API**, then to **Ecommerce**. **Expect** each neighbour to become the focus and the arrow to keep its original direction.
3. Choose **Outgoing**. Follow the handoff to the Ecommerce placeholder for **Warehouse fulfilment**, then the reviewed binding to the Warehouse source. **Expect** the final heading to name **Warehouse fulfilment · fixture/warehouse**.

### B3 — Return to the previous view

1. Open Ecommerce **Order** with the busy fixture, choose **Outgoing**, zoom and pan, and load another page. Inspect one connection, then follow a neighbour. **Expect** the neighbour to become the focus.
2. Choose **Back**. **Expect** the earlier focus, direction, loaded pages and viewport. If the inspector was open, it returns with the saved selection while keyboard focus follows the Back interaction.
3. Repeat with an invalidated saved frame and again with a saved focus whose page-one read returns unavailable. **Expect** a refreshed-view message in the first case and a link back to search or the last available view in the second.

### B4 — Navigate and inspect with the keyboard

1. At a graph with neighbours, use Tab to reach a row and read its direction, origin and source. Activate **Inspect relationship** with Enter. **Expect** the details to open with focus on **Close details**.
2. Activate **Close details**, then **Follow**, then **Back**, using keyboard controls. **Expect** focus to return to the row after closing and the graph to follow and restore the expected nodes.
3. At the busy node, use the keyboard to change direction, **Load more**, and change or reset zoom. **Expect** new connections and the same focus and selection while magnification changes.

### B5 — Keep the latest focus when requests overlap

1. Hold the first graph response for **Incoming**, then select **Outgoing** before releasing it. Let the newer response finish first. **Expect** the displayed direction and connections to stay on **Outgoing** when the older request finishes.
2. Make a new follow or direction request fail with a temporary error. **Expect** the last successful view, a message identifying the failure, **Retry**, and **Return to current view**.
3. Release the failure and retry. **Expect** the attempted view to load.

### B6 — Inspect a node or an edge without losing the view

1. At the Ecommerce system, inspect the focus, then inspect its published handoff. **Expect** source, identifier, pinned revision and available meaning. The relationship shows its direction and both endpoints.
2. Open **View evidence** where the source map is configured. **Expect** the recorded commit, path and line range when available. With the map unset, **expect** the plain citation and an unavailable-link message.
3. Close details. **Expect** the same focus, loaded neighbours and viewport.

### B7 — Inspect parallel relationships independently

1. As manager, create a workspace relationship with the published Ecommerce handoff's endpoints and label. Give it a distinct explanation. **Expect** a successful save.
2. As reader, open both curves and each corresponding row in the refreshed graph. **Expect** distinct selections with published provenance on one and workspace origin, explanation, author and **Relationship history** on the other.
3. Remove the temporary relationship and refresh the reader's graph. **Expect** the published handoff to remain.

### B8 — Load more without moving away

1. At the busy Ecommerce **Order**, select a visible edge and zoom. Choose **Load more** until the known late relationship appears. **Expect** the same focus, selection and viewport. Each seeded ID appears once.
2. With a controlled empty page that has a continuation, or a captured real response of the same shape, proceed through that page. **Expect** **Load more** to remain available.
3. Load the final page. **Expect** **Load more** to disappear while all loaded connections stay visible.

### B9 — Explain empty, incomplete, and partial results

1. Use an isolated focus with a completed empty response and no relevant coverage gap, such as the fixture core **BusinessOrder** when the baseline is unchanged. **Expect** **No visible connections in this direction**.
2. Use an empty focus with a relevant source gap. **Expect** the source coverage notice, details of relevant gaps and a separate count for gaps elsewhere when present.
3. Use controlled responses for an empty page with continuation and a truncated response without a cursor. **Expect** a prompt to check more results in the first case and an incomplete-results message in the second. An unavailable endpoint should be absent from the default graph.

### B10 — Recover from an invalidated or failed continuation

1. As reader, open the busy node with **Outgoing** and a continuation. As manager, save a temporary note in the workspace. **Expect** the write to succeed.
2. As reader, choose **Load more**. **Expect** the loaded rows marked out of date and a **Reload** action. Reload and **expect** page one for the same focus and direction.
3. Cause a temporary failure during a continuation for a fresh view. **Expect** earlier rows and **Retry**. Remove the failure, retry, and **expect** the same cursor to append the next page. Remove the temporary note.

### B11 — Show contribution actions to managers

1. Open the same available node and a workspace relationship as reader and as manager. **Expect** both roles to inspect details and history. The manager sees add, create, edit and remove controls where applicable.
2. On the test browser or a controlled fixture account, exercise a write denial after opening an editor. **Expect** the API to refuse the save and the form to retain the draft. Restore the fixture role if the operator changed it.

### B12 — Create and discover workspace contributions

1. As manager on Ecommerce **Order**, select **Inspect focus**, then **Add note**. Enter a unique explanation and submit. **Expect** **Change saved** and **View saved note**.
2. Choose **Create relationship** and connect Ecommerce **Order** to Warehouse **Order**. Confirm **From entity** and **To entity**, then supply a type or a custom label and explanation. Save and open **View saved relationship**. **Expect** details for the saved ID even if it is outside the first page.
3. In the separate reader profile, reopen the focus and inspect the note and edge. **Expect** the exact saved text and the workspace origin.

### B13 — Reflect edits and removals in a fresh view

1. While using a chosen focus and direction, edit the B12 relationship. **Expect** the graph to refresh from page one and keep the focus and direction. A new reader view shows the edited meaning.
2. Open the note and relationship details and remove both records. **Expect** them to disappear from fresh reader results. The history still shows creation and subsequent changes.

### B14 — Preserve a rejected draft

1. Create a temporary note. Open the same revision in two manager contexts. Save an edit in one, then submit different text from the other. **Expect** the current revision beside the retained draft and a review step before resubmission.
2. In controlled cases, submit a draft with an unavailable endpoint, an expired session and a denied write. **Expect** the failure reason, retained draft and no new journal entry for each rejection.
3. Choose **Sign in again** for the expired session in the same tab. **Expect** the authorized editor and its draft to reopen. Remove the temporary record.

### B15 — Keep repository inputs read-only

1. Before any writes, read the active snapshot on the workspace home page. Capture the adopting and source checkout states as shown below. **Expect** the checkouts to be clean, their revisions to match the manifest, and the manifest digest to match the active snapshot.
2. Perform the create, edit and remove walkthroughs and confirm changes through workspace history.
3. Read the active snapshot again and capture the inputs a second time. **Expect** identical snapshot IDs, revisions, statuses and Turtle hashes.

Use the fixture's own `fixture.json` and the exact manifest staged on the environment. For the Mac fixture from the runbook, the files are under `$WORKSPACE_SCENARIO_DIR/checkouts` and `$WORKSPACE_SCENARIO_DIR/bundle/manifests`. Supply the snapshot read from the workspace for each capture.

```bash
node test/acceptance/graph-inputs.mjs "$fixture_json" "$manifest_json" "$snapshot_before" > "$evidence_dir/inputs-before.json"
# Perform the write walkthroughs and record the active snapshot again.
node test/acceptance/graph-inputs.mjs "$fixture_json" "$manifest_json" "$snapshot_after" > "$evidence_dir/inputs-after.json"
diff -u "$evidence_dir/inputs-before.json" "$evidence_dir/inputs-after.json"
```

## Executable coverage for approval

The browser suite drives the real graph from separate reader and manager sessions. The component suite drives the same graph controls against deterministic responses for cases that are difficult to produce on demand. Review both suites after the walkthrough. Run them once approved. [Browser scenarios](../../../deploy/workspace/verify-graph-explorer.mjs) · [Component tests](../../../runtime/RuntimeKnowledge.Tests)

| Behaviour | Browser, component or input check |
| --- | --- |
| B1 | Entry and handoff browser scenario and `LinksSelectTheExactAvailablePublishedEntity` |
| B2 | Entry and handoff browser scenario and `FirstPageAndDirectionChangesUseOneHop` |
| B3 | Entry and handoff browser scenario, `BackRestoresTheLoadedFrame` and `StaleCursorRequiresReloadAndInvalidatesBackFrames` |
| B4 | Keyboard and viewport browser scenario, `BackRestoresTheLoadedFrame` and `NodeInspectionAndClosingKeepTheLoadedNeighbourhood` |
| B5 | `LateResponsesCannotReplaceTheLatestQuery` and `ObservedSnapshotChangeSupersedesAPendingFirstPage` |
| B6 | Published evidence browser scenario, `NodeInspectionAndClosingKeepTheLoadedNeighbourhood` and `EvidenceLinksUseTheOwningRepositoryAndPinnedRevision` |
| B7 | Parallel-edges browser scenario and `ParallelEdgesOpenTheirOwnDetails` |
| B8 | 101 relationships browser scenario, `EmptyPageKeepsLoadMoreAndFinalPageKeepsEarlierEdges` and `ParallelOriginsAndSameLabelNodesRemainDistinctAcrossPages` |
| B9 | Scoped coverage browser scenario, `CoverageEmptyAndIncompleteStatesAreIndependent`, `CoverageGapsAreScopedToTheLoadedSourcesAndNodes` and `B03_IncomingAndBothDirectionsAndUnavailableEndpointsPageWithoutLossOrReorder` |
| B10 | Live invalidation browser scenario, `StaleCursorRequiresReloadAndInvalidatesBackFrames`, `TransientContinuationRetriesTheSameCursor` and `ContinuationTimeoutOffersRetryWithTheSameCursor` |
| B11 | Reader refusal browser scenario, `ReaderHasNoContributionControls` and `RejectedSaveKeepsDraftAndVersionConflictRequiresReview` |
| B12 | Separate-reader browser scenario, `CreatesANoteAndDirectedRelationshipFromTheGraph` and `SavedRelationshipStaysOpenWhenThePendingReloadFinishes` |
| B13 | Save, edit and history browser scenario and `SuccessfulWritesRefreshFromPageOneWithoutRepeatingTheWrite` |
| B14 | `RejectedSaveKeepsDraftAndVersionConflictRequiresReview` and `SignInResumeRestoresTheInterruptedEditor` |
| B15 | Before-and-after manifest and checkout capture, plus the Mac browser snapshot and Turtle assertions |

From the repository root on Mac:

```bash
deploy/workspace/database/test-db.sh run bash -c 'cd runtime && dotnet test RuntimeKnowledge.Tests --filter "FullyQualifiedName~GraphExplorerNavigationTests|FullyQualifiedName~GraphInspectorTests|FullyQualifiedName~GraphExplorerContributionTests|FullyQualifiedName~GraphNeighbourhoodTests|FullyQualifiedName~B03_IncomingAndBothDirectionsAndUnavailableEndpointsPageWithoutLossOrReorder"'
bash deploy/workspace/local/smoke.sh
```

For the deployed suite, use the fixture environment described in [Operate](05-runbook.md#operate), then run `bash deploy/workspace/verify.sh home-lab`. Match the expected snapshot to the deployed fixture. An operator-approved graph-write run sets `WORKSPACE_VERIFY_GRAPH_WRITES=1`. Keep the controlled component evidence separate from checks made against the live deployed graph. A skipped write scenario or a missing deployed fault fixture leaves the affected deployed result pending.

## Results

Build verification from [PR #171](https://github.com/jamiemitchellconsultants/division-ontology/pull/171) provides prior evidence for the local browser and component suites. User-phase walkthroughs, executable-test approval and deployed evidence are pending. Record each result with the date, walkthrough operator, revision, snapshot and evidence path. Mark an individual step failed if its observed outcome differs. The User phase routes a failure back to the responsible phase.

| Behaviour | Mac walkthrough | Mac executable | Home-lab walkthrough | Home-lab executable |
| --- | --- | --- | --- | --- |
| B1 | Pending | Pending | Pending | Pending |
| B2 | Pending | Pending | Pending | Pending |
| B3 | Pending | Pending | Pending | Pending |
| B4 | Pending | Pending | Pending | Pending |
| B5 | Pending | Pending | Pending | Pending |
| B6 | Pending | Pending | Pending | Pending |
| B7 | Pending | Pending | Pending | Pending |
| B8 | Pending | Pending | Pending | Pending |
| B9 | Pending | Pending | Pending | Pending |
| B10 | Pending | Pending | Pending | Pending |
| B11 | Pending | Pending | Pending | Pending |
| B12 | Pending | Pending | Pending | Pending |
| B13 | Pending | Pending | Pending | Pending |
| B14 | Pending | Pending | Pending | Pending |
| B15 | Pending | Pending | Pending | Pending |
