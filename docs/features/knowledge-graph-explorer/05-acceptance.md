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

The bundled regression automates B1–B15 from the Windows operator machine, including the live cursor invalidation, browser-controlled fault responses, role checks, saved-record reload and input invariants from the walkthrough. Run it against the retained fixture as described in the runbook. The evidence identifies controlled browser responses in their steps and records the expected and actual outcome. Record the bundled run separately from the individually executed walkthroughs and the broader workspace smoke. [PowerShell runner](../../../deploy/workspace/run-home-lab-graph-regression.ps1) · [Runbook](05-runbook.md#graph-explorer-regression-on-ai-mcp-server)

## Results

Build verification from [PR #171](https://github.com/jamiemitchellconsultants/division-ontology/pull/171) provides prior evidence for the local browser and component suites. Record each User-phase result with the date, walkthrough operator, revision, snapshot and evidence path. Mark an individual step failed if its observed outcome differs. The User phase routes a failure back to the responsible phase.

On 22 November 2026, Codex drove the B1 and B2 walkthroughs as the reader in the local Mac workspace. The fixture was built from `d45bd4c`, which includes the build merge, with snapshot `82080f568a58ae92ab963ba6c94d039c27731ee90ab86033b3a7d7988d38f9b9`. The browser opened Ecommerce **Order** from search and Warehouse **Order** from its entity page. Direction started at **Both** for **Order processing**. The incoming containment arrows retained their source-to-destination order, and the outgoing handoff reached **Warehouse fulfilment · fixture/warehouse**. Step evidence is in `walkthrough-b1-b2.json` in the local fixture directory.

On 22 November 2026, Codex drove B3 in the same local build and snapshot. The browser loaded 100 then 105 connections for Ecommerce **Order** and followed Warehouse **Order**. **Back** restored all 105 connection IDs, **Outgoing**, the selected relationship, and the exact pan and zoom. Separate browser-only responses exercised a stale continuation and an unavailable saved focus. A stale frame refreshed page one at the saved focus and direction. An unavailable focus showed both search and return actions while the last successful graph stayed visible. Step evidence is in `walkthrough-b3.json` in the local fixture directory.

On 22 November 2026, Codex completed B4 in the same local build, snapshot and busy fixture using keyboard input. Inspecting a relationship moved focus to **Close details**, and closing returned focus to the same neighbour row. **Follow** moved to Warehouse **Order**, then **Back** restored Ecommerce **Order**. The direction menu changed from **Both** to **Outgoing**, **Load more** added five connections, and the three zoom actions worked while the focus and the selected edge stayed in place. Step evidence is in `walkthrough-b4.json` in the local fixture directory.

On 22 November 2026, Codex completed B5 in the same local build, snapshot and busy fixture. The browser held a real empty **Incoming** response until **Outgoing** showed 100 rows, then released it. All 100 outgoing rows remained visible. A browser-only 503 during a follow identified Warehouse **Order** and preserved the Ecommerce view. **Return to current view** cleared the failure. On a second attempt, **Retry** sent the same query and opened Warehouse **Order** in **Outgoing**. Step evidence is in `walkthrough-b5.json` in the local fixture directory.

On 22 November 2026, Codex completed B6 in the same local build and snapshot. The Ecommerce focus and published handoff showed their identifiers and source revision. The outgoing handoff showed both endpoints and cited `docs/architecture.md`, lines 1–5. The default settings showed the plain citation and **Evidence link unavailable**. A browser-only source map made **View evidence** open the exact commit, file and line range in a separate tab. Its destination was simulated because the repository is synthetic. Closing details preserved **Ecommerce**, **Both**, both loaded connection IDs, and the exact pan and zoom. Step evidence is in `walkthrough-b6.json` in the local fixture directory.

On 22 November 2026, Codex completed B7 in the same local build and snapshot. The manager saved a temporary relationship with the published Ecommerce handoff's exact label and endpoints and a distinct explanation. The reader independently selected both list rows and both diagram curves. The published inspector showed the pinned source revision and `docs/architecture.md` citation. The workspace inspector showed the explanation and author, and its **Relationship history** opened the creation event. After the manager removed the temporary relationship, a fresh reader view showed the published handoff and no temporary edge. Step evidence is in `walkthrough-b7.json` in the local fixture directory.

On 22 November 2026, Codex completed B8 in the same local build, snapshot and busy fixture. **Load more** took Ecommerce **Order** from 100 to 105 connections, including the expected late relationship `paging-acceptance-6081cc56-37`. All 105 seeded IDs appeared once. In a second traversal, a browser-only empty response supplied a continuation and left the same 100 connections and **Load more** available. Following that continuation showed the real final page and removed **Load more**, with all 105 connections still visible. Both traversals kept **Outgoing**, the selected relationship and its open inspector, and the same pan and zoom. Step evidence is in `walkthrough-b8.json` in the local fixture directory.

On 22 November 2026, Codex completed B9 in the same local build and snapshot. The real **BusinessOrder** traversal showed **No visible connections in this direction**. The real empty **Incoming** traversal for Ecommerce **Order** showed two neighbourhood coverage gaps and counted four gaps elsewhere. A browser-only empty response with a cursor prompted the reader to check more results and kept **Load more** enabled. A browser-only truncated response without a cursor showed the incomplete-results message, retained 100 connections and hid **Load more**. The live default graph requested `includeUnavailable: false` and showed no unavailable relationship. The opt-in comparison used a browser-only fixture because the active snapshot had no known unavailable relationship. Step evidence is in `walkthrough-b9.json` in the local fixture directory.

On 22 November 2026, Codex completed B10 in the same local build, snapshot and busy fixture. A temporary note saved by the manager at Warehouse **Order** invalidated the reader's Ecommerce **Order** cursor. The live **Load more** request returned `409 staleCursor`. The graph kept all 100 loaded rows, marked them out of date and offered **Reload**. Reload requested page one for the same focus and **Outgoing** direction and received a new cursor. In a fresh reader view a browser-only 503 preserved the first 100 rows and offered **Retry**. Retrying sent the identical cursor to the live workspace and appended five rows, including the expected late relationship. The manager removed the temporary note and a fresh reader inspector showed it was gone. Step evidence is in `walkthrough-b10.json` in the local fixture directory.

On 22 November 2026, Codex completed B11 in the same local build and snapshot. The reader and manager both inspected Ecommerce **Order**, the temporary note and the same workspace relationship. Both opened the note and relationship histories. Only the manager saw add, create, edit and remove controls. One browser submission from an open manager editor used the fixture reader's authorization. The live API returned `403`; the editor showed the failure and retained the draft text and category when reopened. A fresh reader inspection confirmed the denied draft was absent and the temporary fixture note was gone after cleanup. Step evidence is in `walkthrough-b11.json` in the local fixture directory.

On 22 November 2026, Codex completed B12 in the same local build and snapshot. The manager saved an Ecommerce **Order** note and used **View saved note** to open it. The manager confirmed both source-qualified endpoints and saved a custom relationship from Ecommerce **Order** to Warehouse **Order**. **View saved relationship** opened the exact record while the real reload was held. The relationship was outside the first page, and the inspector stayed open after the unmodified reload arrived. A separate reader saw the exact note and used **Load more** to find and inspect the relationship on page two, with the expected explanation, direction and workspace origin. Both contributions are retained for B13. Step evidence and fixture IDs are in `walkthrough-b12.json` in the local fixture directory.

On 22 November 2026, Codex completed B13 in the same local build and snapshot. Editing the B12 relationship explanation saved revision 2 and refreshed page one for the same Ecommerce **Order** focus in **Outgoing**. A fresh reader found the exact edited explanation on page two. The manager removed the relationship and note through their graph inspectors. Both removals refreshed page one at the same focus and direction. A fresh reader inspected the node and loaded both graph pages. The note and relationship were absent and all 105 paging fixture relationships remained. The reader opened both histories and verified creation and removal for the note and creation, edit and removal for the relationship, with the expected before and after values. Step evidence is in `walkthrough-b13.json` in the local fixture directory.

On 22 November 2026, Codex completed B14 in the same local build and snapshot. Two manager contexts opened revision 1 of the same note. One saved revision 2. The other's live `409 versionConflict` showed the current revision beside its retained draft and required a review before saving could be enabled. Controlled browser requests exercised a missing destination, reader-only permission and missing authorization. The live API returned `422 endpointUnavailable`, `403 roleRequired` and `401 authenticationRequired`. Each failure preserved the draft and left the journal position unchanged. **Sign in again** reused the fixture identity session and restored the original authorized editor, **Outgoing**, and the exact draft in the same tab. The manager removed the temporary note. Its history contained only creation, the accepted edit and removal. Step evidence is in `walkthrough-b14.json` in the local fixture directory.

On 22 November 2026, Codex completed B15 in the same local build and snapshot. The reader verified the B12 and B13 note and relationship histories against the live workspace, including the relationship edit and both removals. All five events followed the baseline capture and recorded its snapshot. The workspace home page still displayed the same active snapshot, whose digest matched the pinned manifest. The before and after captures matched byte for byte. All four checkouts were clean at their pinned revisions and all seven tracked Turtle hashes matched. Step evidence and both captures are in `walkthrough-b15.json`, `inputs-before.json` and `inputs-after.json` in the local fixture directory.

On 22 November 2026, the user approved the Mac executable acceptance run and Codex ran both commands above. The filtered .NET suite passed all 40 tests, with zero failures and zero skips. The disposable browser smoke passed all nine Graph Explorer scenarios, including the live write, paging, snapshot, and Turtle input checks. The smoke also passed the traversal paging and persistence checks, and the replay digest matched at journal position 428. The runner reported application revision `befe528+working-tree` and the disposable smoke snapshot `775e271936ec64be715455effb6b47f86b2f950fd1050d5f751547643db62cad`. Evidence is in `executable-dotnet.log` and `executable-mac-smoke.log` in the local fixture directory.

On 22 November 2026, the operator supplied the home-lab deployment summary for `division.tqaentry.com`. It reported application revision `e097a4d3de696b12fd4432c8b2259fecd745a0bb` and active manifest `540da809651de4e416cb00a13299143f0b56fa81c73e530ad8913d9f23f3d927`. Codex verified that the revision predated the PR #171 merge. Both public graph module URLs returned HTTP 200 with an HTML content type, so the walkthrough waited for the application upgrade.

On 22 November 2026, James Mitchell completed the application-only home-lab upgrade. The host verification reported revision `d45bd4c1be563b8bdd62f6dcf055f169ed3614cd`, the same active manifest, and JavaScript delivery for both graph modules. Codex independently confirmed that the public module responses matched the reviewed revision byte for byte. The active manifest is the synthetic Order Fulfilment fixture recorded in the traversal pagination acceptance.

On 22 November 2026, James Mitchell captured the B15 home-lab baseline on `ai-mcp-server` before the Graph Explorer fixture writes. The manifest digest matched the live snapshot verified during the upgrade. Four clean checkouts matched the pinned revisions and seven tracked Turtle files were hashed. The capture is `graph-explorer-inputs-before.json` in the existing traversal pagination fixture run directory.

On 22 November 2026, James Mitchell completed B1 on the home lab. The reader matched the active snapshot, searched for **Order**, found and opened the exact Ecommerce **Graph** link, and received HTTP 200 for the exact entity key. The graph showed **Order · fixture/ecommerce** and two labelled, source-qualified connections. A browser-driver assertion in the inspector stopped the first run after these checks. In the targeted B1.2 run the reader opened the Warehouse **Order** entity and used **Open graph**. The graph showed **Order · fixture/warehouse** and two connections. The host saved `home-lab-walkthrough-b1-2.json` in the traversal pagination fixture run directory.

On 22 November 2026, James Mitchell completed all three B2 steps on the home lab. The graph opened **Order processing · fixture/ecommerce** in **Both** with one direct connection. Following **Incoming** moved the focus through **Storefront API** and **Ecommerce** while both displayed arrows kept their source-to-destination order. Following **Outgoing** reached **Warehouse fulfilment · fixture/ecommerce** through the handoff and **Warehouse fulfilment · fixture/warehouse** through the binding. The host saved `home-lab-walkthrough-b2.json` in the traversal pagination fixture run directory.

On 22 November 2026, James Mitchell verified the live home-lab snapshot and seeded 105 temporary Ecommerce **Order** to Warehouse **Order** relationships under run `d6e5f801`. The seed is used by B3, B4, B8 and B10. The host saved `graph-explorer-paging-seed.txt` in the traversal pagination fixture run directory.

On 22 November 2026, James Mitchell completed B3 on the home lab. **Load more** increased the graph from 100 to 107 connections, including all 105 seed IDs. **Back** restored Ecommerce **Order**, **Outgoing**, the loaded connections, pan, zoom and inspected relationship without moving keyboard focus into the inspector. The first B3.3 attempt stopped in the test runner when its HTTP client could not resolve the workspace hostname outside Chromium. The revised browser test confirmed that a stale saved frame refreshed the first 100 rows at the same focus and direction. When the saved focus was unavailable, the graph identified Ecommerce **Order**, kept Warehouse **Order** visible and offered search and return actions. The host saved `home-lab-walkthrough-b3.json` for the two controlled checks and the late seed ID.

On 22 November 2026, James Mitchell completed B4 on the home lab. **Tab** reached the outgoing workspace edge, whose row identified both sources. **Enter** opened details and focused **Close details**. Closing returned focus to the original inspect control. **Follow** opened Warehouse **Order**, and **Back** restored Ecommerce **Order**. The first B4.3 test timed out awaiting **Outgoing** after keyboard type-ahead. The revised Windows Chromium test used arrow keys, **Enter** and **Tab**. **Outgoing** returned HTTP 200, **Load more** increased the graph from 100 to 107 connections, and zoom changed from 0.31 to 0.39, back to 0.31, then reset to 0.29. The focus and selected relationship stayed in place. The host saved `home-lab-walkthrough-b4-3.json` in the traversal pagination fixture run directory.

On 22 November 2026, James Mitchell completed B5 on the home lab. Chromium released the held response with zero **Incoming** paths after the live **Outgoing** response rendered. All 100 outgoing rows remained. A browser-only 503 during **Follow** named Warehouse **Order**, preserved the Ecommerce view and offered **Retry** and **Return to current view**. Return cleared the error. After a second simulated failure, **Retry** sent the identical query to the live workspace, received HTTP 200 and opened Warehouse **Order** in **Outgoing**. The host saved `home-lab-walkthrough-b5.json` in the traversal pagination fixture run directory.

On 22 November 2026, James Mitchell completed B6 on the home lab. The Ecommerce system and its outgoing published handoff showed their exact identifiers and pinned revisions. The handoff showed both endpoints. The deployed source map had no Ecommerce entry, so the inspector displayed the plain `docs/architecture.md` citation at lines 1–5 and **Evidence link unavailable**. Closing details kept the Ecommerce focus, **Both**, the same two connection IDs, pan and zoom. The host saved `home-lab-walkthrough-b6.json` in the traversal pagination fixture run directory.

On 22 November 2026, James Mitchell completed B7 on the home lab. The manager saved a temporary relationship with the published handoff's endpoints and label and a distinct explanation. The reader used both list rows and both curves to open their independent details and viewed the workspace relationship's creation history. The manager removed record `72b0a374-28ff-411f-9320-f4690fcc5f83` with HTTP 200. A fresh reader view showed the published handoff and no temporary relationship. The host saved `home-lab-walkthrough-b7.json` in the traversal pagination fixture run directory.

On 22 November 2026, James Mitchell completed B8 on the home lab. The live continuation took Ecommerce **Order** from 100 to 107 connections, including the known late relationship and each of the 105 seeded IDs once. A second traversal used a browser-only empty page. The first 100 rows and **Load more** remained available. Its continuation reached the live final page. All 107 connections remained visible, **Load more** disappeared, and the selected relationship and viewport were preserved. The host saved `home-lab-walkthrough-b8.json` in the traversal pagination fixture run directory.

On 22 November 2026, James Mitchell completed B9 on the home lab. The live **BusinessOrder** result showed **No visible connections in this direction**. The live empty **Incoming** result for Ecommerce **Order** listed two relevant coverage gaps and counted four elsewhere. A browser-only empty page kept **Load more** enabled. A browser-only truncated page kept 100 rows visible, showed the incomplete-results message and hid **Load more**. The live default query requested available endpoints only. The unavailable-endpoint opt-in comparison used a browser-only control record. The host saved `home-lab-walkthrough-b9.json` in the traversal pagination fixture run directory.

On 22 November 2026, James Mitchell completed B10 on the home lab. A manager note at Warehouse **Order** invalidated the reader's Ecommerce **Order** continuation. The live API returned `409 staleCursor` and the graph marked all 100 loaded rows out of date. **Reload** fetched page one for Ecommerce **Order** in **Outgoing** with a new cursor. A browser-only 503 left the first 100 rows visible. **Retry** sent the same query to the live API and showed 107 rows. The manager removed temporary note `0751e7a6-f1f5-442a-b8b1-c7a1a32b5426`, and it was absent from a fresh reader view. The host saved `home-lab-walkthrough-b10.json` in the traversal pagination fixture run directory.

On 22 November 2026, James Mitchell completed B11 on the home lab. Reader and manager sessions opened the same note and seeded relationship and both histories. Only the manager saw add, create, edit and remove controls. A browser test submitted one open-editor save with reader permission. The live API returned 403 and the note text and category survived reopening the editor. A fresh reader saw neither the denied draft nor the removed test note. The host saved `home-lab-walkthrough-b11.json` in the traversal pagination fixture run directory.

On 22 November 2026, James Mitchell completed B12 on the home lab. The manager used **View saved note** to open note `e7949f78-d07f-4874-8411-f621084a4b7c`. The manager confirmed the Ecommerce **Order** to Warehouse **Order** direction, saved relationship `b7ffb108-0ee9-4f7a-bddb-da2c50ef28e1` and opened it while the live reload was held and the row was absent. The inspector stayed open after the live first page arrived with the relationship. A separate reader found the exact note and the workspace relationship on page one. The host saved `home-lab-walkthrough-b12.json`, including both fixture IDs for B13.

On 22 November 2026, James Mitchell completed B13 on the home lab. The manager edited the B12 relationship to revision 2 and the graph refreshed page one at Ecommerce **Order** in **Outgoing**. A fresh reader verified the edited explanation on page one. The manager removed the two B12 records using their inspectors. The fresh reader view showed neither record and retained 107 other relationships. Both histories remained available, including the original values, the relationship edit and both removals. The host saved `home-lab-walkthrough-b13.json` in the traversal pagination fixture run directory.

On 22 November 2026, James Mitchell completed B14 on the home lab. Two manager contexts produced a live 409 and the graph displayed revision 2 beside the stale draft. Reviewing the current revision enabled saving. The unavailable endpoint, denied permission and missing authorization requests returned live 422, 403 and 401 responses. Each preserved the draft and left the journal position unchanged. **Sign in again** restored the same editor and draft in **Outgoing**. The temporary note was removed and its history contained only creation, the accepted edit and removal. The host saved `home-lab-walkthrough-b14.json` in the traversal pagination fixture run directory.

On 22 November 2026, James Mitchell completed B15 on the home lab. The live journal confirmed the five B12 and B13 creation, edit and removal events under the original snapshot after the baseline capture. All 105 temporary relationships from paging run `d6e5f801` were removed and the two original Ecommerce **Order** relationships were still visible. The workspace home page showed the same active snapshot. The four checkout revisions, clean statuses and seven Turtle hashes matched the baseline. The host saved `home-lab-walkthrough-b15.json` and `graph-explorer-inputs-after.json` alongside the baseline in the traversal pagination fixture run directory.

On 22 November 2026, Codex validated the bundled runner on the disposable Mac fixture at snapshot `82080f568a58ae92ab963ba6c94d039c27731ee90ab86033b3a7d7988d38f9b9`. The full run recorded 49 passing outcomes across B1–B15. It removed all 111 temporary records, including the 105 paging relationships, and verified the original connections and byte-identical input captures. A separate reader-only B9 run also passed. Evidence is in `run.json`, `records.jsonl`, `inputs-before.json` and `inputs-after.json` under the local `graph-regression-49255d29-6a1c-49c5-b008-c6c15cc60796` evidence directory.

On 22 November 2026, James Mitchell reported that the bundled B1–B15 run completed successfully on `ai-mcp-server` against `https://division.tqaentry.com`. The run was pinned to application revision `d45bd4c1be563b8bdd62f6dcf055f169ed3614cd` and synthetic snapshot `540da809651de4e416cb00a13299143f0b56fa81c73e530ad8913d9f23f3d927`. The deployed results below reflect the operator's confirmation. The runner stores its detailed evidence on the host under the retained fixture run directory.

| Behaviour | Mac walkthrough | Mac executable | Home-lab walkthrough | Home-lab executable |
| --- | --- | --- | --- | --- |
| B1 | Pass, 2 of 2 steps | Pass | Pass, 2 of 2 steps | Pass |
| B2 | Pass, 3 of 3 steps | Pass | Pass, 3 of 3 steps | Pass |
| B3 | Pass, including both B3.3 variants | Pass | Pass, including both B3.3 variants | Pass |
| B4 | Pass, 3 of 3 steps | Pass | Pass, 3 of 3 steps | Pass |
| B5 | Pass, 3 of 3 steps | Pass | Pass, 3 of 3 steps | Pass |
| B6 | Pass, including both B6.2 variants | Pass | Pass, deployed map unset | Pass |
| B7 | Pass | Pass | Pass, 3 of 3 steps | Pass |
| B8 | Pass, 3 of 3 steps | Pass | Pass, 3 of 3 steps | Pass |
| B9 | Pass, including controlled response variants | Pass | Pass, including controlled response variants | Pass |
| B10 | Pass, 3 of 3 steps | Pass | Pass, 3 of 3 steps | Pass |
| B11 | Pass, 2 of 2 steps | Pass | Pass, 2 of 2 steps | Pass |
| B12 | Pass, 3 of 3 steps | Pass | Pass, 3 of 3 steps | Pass |
| B13 | Pass, 2 of 2 steps | Pass | Pass, 2 of 2 steps | Pass |
| B14 | Pass, including all rejection variants | Pass | Pass, including all rejection variants | Pass |
| B15 | Pass, 3 of 3 steps | Pass | Pass, 3 of 3 steps | Pass |
