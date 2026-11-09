# Runtime knowledge workspace acceptance

**Accepted on 9 November 2026.** James Mitchell accepted the recorded Mac and home-lab walkthroughs. Any step without recorded evidence is **deferred and non-blocking** for this acceptance. The follow-up covers the remaining human review and scenario evidence. [Follow-up #65](https://github.com/jamiemitchellconsultants/division-ontology/issues/65)

The pilot product is **Order Fulfilment** using the synthetic Ecommerce, Warehouse and Accounting repositories. Use the approved [behaviours](02-behaviours.md) and [runbook](05-runbook.md). Run the automated walkthrough first and ask the supervising user to inspect failures, inconsistencies, missing coverage and human decisions. The acceptance runner writes private logs, a test-results file and a check summary to its evidence directory. A passing suite is evidence for the scenarios it exercised. Record any remaining environment or human observations separately.

## Walkthrough and expected results

The named .NET classes run through `deploy/workspace/acceptance.sh local` and are recorded in `runtime.trx`. **Browser** refers to the shared Mac and home-lab browser runner. The [home-lab procedure](05-runbook.md#home-lab-acceptance) chains browser checks, restart and guarded replay and records each step in `run.json`. **Host** indicates additional deployed coverage deferred to #65. The local runtime tests provide automated evidence for those scenarios using disposable databases.

| Behaviour | Steps and expected result | Executable evidence |
| --- | --- | --- |
| B1 Inventory | 1. Sign in as reader and page through published inventory. 2. Open a relationship and an isolated entity. Expect separate records with source identity, commit and snapshot, plus explicit coverage gaps. | `WorkspaceScenarioTests`, `SnapshotTests`, `WorkspaceUiTests`. Browser opens published inventory and checks the active snapshot. |
| B2 Semantic discovery | 1. Save a finance reconciliation annotation, wait for indexing and search for the related business phrase. Expect the annotation attached to the expected entity and its provenance. 2. Search for an unrelated phrase in the deterministic fixture. Expect an empty completed search. | `WorkspaceScenarioTests`, `SearchTests`, `WorkspaceUiTests`. Browser retrieves the indexed note with the deployed model. |
| B3 Distinct identities | 1. Compare Ecommerce and Warehouse **Order** records. 2. Open each and traverse from its own source key. Expect distinct records and a contribution associated with its original key. | `WorkspaceScenarioTests`, `WorkspaceUiTests`. Browser uses distinct source keys in the pilot and relationship checks. |
| B4 Baseline activation | 1. Activate a replacement fixture bundle and request inventory and traversal. Expect the replacement digest and coherent endpoint resolution. 2. Stage an invalid bundle. Expect failure and the previous active digest and contributions. | `SnapshotTests`, `WorkspaceScenarioTests`, `SearchTests`. Host needs valid and invalid replacement bundles. |
| B5 Missing and restored entity | 1. Create a note or edge, activate a snapshot omitting its endpoint, and inspect the record. Expect the stable key and last-known provenance marked unavailable. 2. Activate a snapshot restoring that key. Expect the existing contribution to resolve. | `WorkspaceScenarioTests`, `ReplayTests`, `ContributionTests`. Host needs missing and restored endpoint snapshots. |
| B6 Unavailable paths | 1. Traverse normally after an endpoint disappears. Expect the retained edge to be excluded. 2. Enable unavailable endpoints and traverse again. Expect the retained edge, marked endpoint and last-known snapshot. | `WorkspaceScenarioTests`, `ContributionTests`, `WorkspaceUiTests`. Host needs a missing endpoint snapshot. |
| B7 Save annotation | 1. Save a business RBAC note as manager. 2. Reopen the entity as reader after restarting the fixture. Expect the same saved identity, category and text with author and creation time. A rejected write leaves no saved note. | Browser before and after restart. `WorkspaceScenarioTests`, `ContributionTests`, `IdentityTests`. |
| B8 Traversal context | 1. Traverse to an entity with a saved business RBAC note. 2. Inspect its context. Expect the note on that entity with its attribution and runtime origin. | Browser, `ContributionTests`. |
| B9 Shared edit authority | 1. Create a contribution as one manager. 2. Attempt to edit and remove it as reader, then as a different manager. Expect reader denials and successful manager changes attributed to the second manager. | `IdentityTests.AnotherManagerCanEditAndRemoveWithTheirOwnAttribution` covers annotations. Host needs a second manager account. |
| B10 History and removal | 1. Edit a saved note, then remove it. 2. Search and traverse again, and open its history. Expect the removed note to be absent from normal results and its stable ID, before/after content, actor and time to remain in history. | `WorkspaceScenarioTests`, `SearchTests`, `ContributionTests`. Browser checks creation history persists. |
| B11 Stale changes | 1. Load the same version twice and save the first edit. 2. Submit the second edit or removal with the stale version. Expect a conflict, the newer version preserved, and the draft available for explicit review and retry. | `WorkspaceScenarioTests`, `ContributionTests`, `WorkspaceUiTests`. Host needs a concurrent edit or stale-version fixture request. |
| B12 Descriptive RBAC | 1. Add a business RBAC note to a reached entity. 2. Traverse as reader. Expect the note to be displayed and the same path to remain available. | Browser, `ContributionTests.RoleNotesReturnDuringTraversalAndRemovalKeepsHistory`. |
| B13 Product context | 1. Search both Order Fulfilment questions below. Expect the named source-owned leads and snapshot provenance. 2. Trace the Ecommerce → Warehouse → Accounting path. Expect reviewed bindings and source evidence for both handoffs. 3. Use that evidence in the product definition. | Browser checks both searches and the evidence-backed path. Pilot product definition below. |
| B14 Business understanding | 1. Give the pilot definition to an independent wider-business reviewer. 2. Ask them to explain its purpose and when they would use it. Record the explanation, reviewer, date and outcome. | Human review. The simulated response below rehearses the prompt. |
| B15 External identity | 1. Sign in with each deployment’s trusted identity provider and query the session. 2. Compare reader and manager capabilities. Expect the roles granted by the valid token. | Browser exercises real Keycloak locally and on the home lab. `IdentityTests` exercises Keycloak and Entra signed-token contracts. `AwsSimulationTests` covers the simulated Entra gateway. |
| B16 Roles | 1. Query and read history as reader, then attempt a write. Expect readable data and a forbidden write. 2. Create, edit, remove and associate as manager. Expect valid changes to succeed. | Browser covers reader query, history, write denial and manager saves. `IdentityTests`, `WorkspaceScenarioTests`. |
| B17 Invalid access | 1. Request data or a write without a token, with an invalid or expired token, or with a valid token lacking the workspace role. 2. Recheck persisted changes. Expect denial and no successful change. | `IdentityTests.InvalidTokensAreDeniedBeforeWrites` and role-claim tests. Host needs controlled invalid and roleless tokens. |
| B18 Re-evaluate access | 1. Save successfully with a manager token. 2. Make a subsequent request with an expired token or one without the required role. Expect denial and no new change. | `IdentityTests.WorkspaceRolesAreRecheckedOnEveryRequest`, `WorkspaceUiTests`. Host needs controlled replacement tokens. |
| B19 Cross-source relationship | 1. Connect Ecommerce **Order** to Warehouse **Order**. 2. Open the saved edge as reader and inspect direction, meaning, author, time and both endpoint identities and provenance. Repeat with published architecture nodes. | Browser covers both the business edge and a typed edge from **Order processing** to **Fulfilment processing**. `WorkspaceScenarioTests` exercises the business edge through the API. |
| B20 Meaning | 1. Create one relationship with an applicable published type and another with a custom label and explanation. 2. Inspect both records. Expect the selected meaning, with the custom meaning stored on the runtime edge. | Browser exercises both choices. `ContributionTests.ExplicitAssociationPreservesBothEdgesAndProvenances` exercises published type selection with codification. |
| B21 Validation and race | 1. Submit a relationship with unavailable endpoints or incomplete meaning. 2. Race a save against a publication removing the endpoint. Expect validation failure, no partial edge and no successful journal entry. | `ContributionTests.IncompleteMeaningCreatesNeitherRelationshipNorHistory` and `PublicationWinsBeforeWaitingWriteAndRetainedRecordsStayEditable`. Host needs fixture mutation and controlled invalid requests. |
| B22 Immediate visibility | 1. Save a valid edge as manager. 2. Traverse and inspect history as reader immediately. Expect the same edge ID, visible runtime origin and a successful history event. Inject a commit failure in the disposable integration database and expect neither edge nor event. | Browser, `WorkspaceScenarioTests`, `ContributionTests.SuccessfulEdgeAndHistoryAreImmediatelyVisible` and `ARejectedCommitLeavesNeitherHistoryNorGap`. |
| B23 Review history | 1. Create, edit and remove a runtime contribution. 2. Open its change log as an authorised reader. Expect stable ID, actor, time and before/after records with the original context. | `WorkspaceScenarioTests`, `ContributionTests`, `IdentityTests`. Browser verifies relationship creation history and checks it after restart. |
| B24 Explicit codification | 1. Save a runtime relationship beside a published relationship with the same endpoints. 2. Associate them explicitly. Expect a separate association and both identities and provenances. | `ContributionTests.ExplicitAssociationPreservesBothEdgesAndProvenances`, `WorkspaceScenarioTests`. Host needs an association walkthrough. |
| B25 Preserve both origins | 1. Traverse after association. Expect distinct runtime and published edges. 2. Remove or make an endpoint unavailable and inspect history. Expect prior evidence retained and normal visibility to follow availability and removal. | `WorkspaceScenarioTests`, `ContributionTests`, `ReplayTests`. Host needs the codification and missing-endpoint scenarios. |

## Pilot questions and definition

1. **What happens after a customer places an order?** Expect Ecommerce’s **Order processing** as a search lead. Ecommerce’s `docs/architecture.md` supports the handoff to Warehouse Fulfilment for picking and dispatch.
2. **How does Accounting learn that an order has shipped?** Expect Accounting’s **Order** as a search lead. Warehouse’s `docs/architecture.md` supports sending shipment completion to Accounting for sales invoicing. The delivery mechanism remains open in the fixture.

**Product definition.** Order Fulfilment gives Operations and Finance a shared view of how a customer order moves from Ecommerce to Warehouse dispatch and how shipment completion feeds sales invoicing in Accounting.

**Simulated review.** “I’d use this to see who owns each handoff. If a shipped order hasn’t been invoiced, I’d start by checking whether the shipment completion reached Accounting.” The independent B14 review is deferred to #65.

## Results

Record the date, runner or operator, application revision and snapshot for each environment. Include relevant test outcomes and a reference to retained evidence. On the home lab, record index lag, search latency, persistence after restart and the same-position journal replay comparison. Search must work with the configured embedding service. Its model identity and digest are outside acceptance.

| Environment | Date and runner | Result |
| --- | --- | --- |
| Local automated walkthrough | 9 November 2026, Codex on Mac | 118 repository tests and 35 runtime tests passed. The browser found both expected pilot leads and the reviewed cross-source handoff. It verified the business and architecture relationships, the saved annotation after API and database restart, and the same state digest after journal replay through position 5. |
| Home-lab configuration | 9 November 2026, Codex on Mac | The Compose, Keycloak and Caddy configuration checks passed locally. |
| Home-lab fixture | 9 November 2026, Jamie | The scripted checks passed against the existing workspace with the synthetic fixture active. The run passed DNS, the initial browser checks, persistence after restarting PostgreSQL and the API, guarded journal replay, and persistence after replay. Additional deployed coverage is deferred. |
| B14 | 9 November 2026, simulated rehearsal | Deferred. An independent wider-business reviewer is unavailable. |

The local run used application revision `b2bfdb5455323a42dca080ec8ec8ed347c018791` with the acceptance changes in the working tree, snapshot `3c5908215dd4076bda34e9f5d90ed34e868fe66e8b53286f28702133e05075fd` and Qwen model revision `ac6da0dfba84a81fdbfbaf330198c33cd77c4cdfc53e8bc50eb581914a15621d`. The runner observed the indexed annotation after 1,664 ms initially and 12,356 ms after replay. The corresponding individual search requests took 128 ms and 136 ms. These measurements describe the disposable Mac fixture.

The first browser run retrieved both search leads, then failed to find the full handoff because the walkthrough sent the wrong field for traversal depth. The final run set depth through the browser and checked the four-step handoff. The results above describe the automated run. Any additional user-led walkthrough evidence is deferred.

The shared replay procedure added for the home lab was then exercised against the real disposable Mac stack on 9 November 2026. The follow-up passed 122 repository tests, 35 runtime tests and browser checks before restart, after restart and after replay through position 5. The deployed runner's preflight also rejected a mismatched installer snapshot and mismatched Compose image before any browser writes or restart.

On 9 November 2026, James Mitchell ran the automated local walkthrough on M5MBP using revision `b46ce948b24e3550ac72f0cc8a0d7fee9391cd50` and synthetic snapshot `be2600100572809b63ef67a5a24e98b5cf50d6f0da94b1d4095d1725c7b089c0`. The evidence directory's `checks.tsv`, `runtime.trx` and `local-browser.log` confirm 122 repository tests, 35 runtime tests, the home-lab configuration check and all local browser checks passed. The replay digest matched at journal position 5. B14 was the only item flagged for human review in this local run.

On the home lab, Jamie reported `passed scripted checks` in `run.json` for application revision `b2bfdb5455323a42dca080ec8ec8ed347c018791` and synthetic snapshot `bf5821c9e3194b30834aec9219944a5bff1a35d8a5c5087b4d3a5075049862fc`. An initial browser attempt stopped at workspace DNS resolution. Windows subsequently resolved the name and the retry passed. The logs recorded the configured embedding service as `qwen3-embedding-8b-q4_k_m`.

The deployed browser logs recorded these timings for the same fixture annotation:

| Browser check | Time to retrieve the indexed annotation | Final query |
| --- | ---: | ---: |
| Initial | 2,289 ms | 76 ms |
| After restart | 119 ms | 119 ms |
| After replay | 2,269 ms | 75 ms |

The guarded replay compared state at journal position **5**. After cutover, `projection-active.json` reported generation `0617272d-4664-4937-9fd8-e79f44e6ecca`, the same snapshot, and matching state digest `e48463f44ff5461b2a9f2e4e16e7e8918c9256223a5332b86ce8f551a1703b28`. The browser retrieved the saved data again from the rebuilt projection.

Jamie also completed a home-lab rerun using the one-command PowerShell procedure on 9 November 2026. The terminal printed the final B14 reminder and a new evidence directory, `evidence-9f421b73-2811-467f-a0ee-fd7db7a8122d`. That reminder appeared after passing the browser checks, restart, guarded replay and post-replay persistence. It was the only exception reported by the scripted rerun.

## Deferred evidence

- Collect an independent B14 explanation of the Order Fulfilment product's purpose and intended use.
- Record any remaining manual walkthrough observations and executable coverage for the scenarios above.
- Exercise the remaining deployed variants, including full inventory and search edge cases, valid and invalid snapshot replacement, unavailable and restored endpoints, cross-manager edits and stale versions, role and token changes, invalid saves and codification. Record live Entra evidence when that deployment is exercised.

All of these items are non-blocking and tracked in [#65](https://github.com/jamiemitchellconsultants/division-ontology/issues/65).
