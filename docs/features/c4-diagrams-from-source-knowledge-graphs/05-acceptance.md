# C4 impact pilot acceptance

The three-repository preview passed eight automated output checks. For this pilot, James Mitchell approved B1–B15 and directed that the executable checks perform the comparisons between generated and expected results. Use the automated result as the comparison record. Record participant feedback for B4 separately.

- Participant, architect and reviewer: James Mitchell.
- Acceptance environment: `M4Mini.local`, running the local CLI from a clean checkout. On 6 November 2026, James Mitchell waived a repeat run on a second machine for this pilot.
- Change request: **CR-ORDER-01**. Change Order processing to send a new order identifier to Warehouse fulfilment and Accounting.
- Inputs: synthetic Ecommerce, Warehouse and Accounting repositories, plus the existing synthetic Alpha code fixture for B8. The fixture publishes a baseline and a newer catalog into disposable Git repositories.

## Inspect reports on demand

Use this block to investigate a discrepancy or view the sample assessment for B4. Open Terminal at the repository root on the M4 Mini and run the block. It opens the reports in Finder and the short review index in your text editor.

```bash
npm ci &&
review_dir=$(mktemp -d "${TMPDIR:-/tmp}/division-acceptance.XXXXXXXX") &&
node test/acceptance/c4-walkthrough.mjs prepare "$review_dir/fixture" &&
printf 'Review root: %s\n' "$review_dir/fixture" &&
open "$review_dir/fixture/reports" &&
open -t "$review_dir/fixture/reports/README.md"
```

Keep this Terminal tab for the feedback and cleanup commands. Use the index to locate the report named in a discrepancy. The synthetic GitHub links are identity markers. Use the exported pinned source excerpt if you need to investigate B8.

## Behaviour reference and expected results

These scenarios document the checks and identify the corresponding reports. Open the relevant section when investigating a discrepancy.

### B1 — Search

1. Open `B01-description/candidates.json`. Find **Order processing** as a supported token match from `fixture/ecommerce`, with its source IRI and the baseline Ecommerce commit.
2. Open `B01-unmatched/candidates.json`. Expect zero leads and `unknown-search`.
3. Open `B01-exact/candidates.json`. Expect one exact IRI match for the same Order processing component.

### B2 — Cross-source path

1. Open `B02-paths/paths.json`. Find the path ending at the Accounting system. Expect these four steps in order: Ecommerce assertion, Ecommerce-to-Warehouse binding, Warehouse assertion, Warehouse-to-Accounting binding.
2. Check that both assertions are outgoing and stay within their respective source. Check their evidence paths and source commits. Check each binding's identity evidence and registry commit. The report should show separate assertions and bindings for the journey.

### B3 — Review an assessment

1. Open `B03-assessment/assessment.md`. Expect CR-ORDER-01, the change description, the baseline snapshot, and the confirmed Ecommerce lead.
2. Find Accounting as a likely impact. Expect the reconciliation reason, the path ID from B2 and its evidence IRIs.
3. Find External bank under **Not expected to be affected** and the reason about bank payment instructions. Find the question about retaining historical order identifiers under unresolved questions. Check that the report presents findings for review.

### B4 — Record architect and reviewer feedback

1. Read the B3 assessment first as architect, then as reviewer. Select the reports folder in Finder, press Command–Up Arrow to go to the fixture root, and open `feedback.json` in your text editor. Put your name in both role fields and replace the sample observations with your own accuracy, completeness, confidence, discoveries, suspected misses, false leads, examples and open questions. Keep the assessment digest.
2. Save the JSON, then paste this command in the same Terminal tab.

   ```bash
   node test/acceptance/c4-walkthrough.mjs feedback "$review_dir/fixture" &&
   open -t "$review_dir/fixture/reports/B04-feedback/feedback.md"
   ```

3. Save your qualitative observations with the acceptance record. The executable B4 check verifies the assessment digest and the rendering of the feedback fields.

### B5 — System context

1. Open `B05-context/view-context.md`. Expect Ecommerce inside the system boundary. Expect Shopper and Warehouse fulfilment outside it.
2. Check the two supported relationships and their evidence: Shopper → Ecommerce and Ecommerce → Warehouse fulfilment.

### B6 — Containers

1. Open `B06-container/view-container.md`. Expect Storefront API and Order store inside the Ecommerce boundary, each with a source evidence row.
2. Find Storefront API → Order store. Check that its assertion and evidence appear in the report.

### B7 — Components

1. Open `B07-component/view-component.md`. Expect Order processing and Address validation inside the Storefront API boundary.
2. Find the Address validation → Order store relationship. Expect the Order store outside the boundary and the communication evidence in the report.

### B8 — Code

1. Open `B08-code/graph-code.json` and `B08-code/pinned-code.json`. Find `handle` and check its repository commit, evidence path `src/app.ts`, line 1 and blob ID against the exported source. Expect the source excerpt `export const api = 1;`.
2. Open `B08-code/view-code.md`. Find the additional **architect-inspected** entry. Check that it cites the same commit, path and line.
3. Open `B08-code-gap/view-code.json`. Expect no code items or edges for Ecommerce Order processing and an `empty-level` gap.

### B9 — Follow levels

1. Open `B05-context/view-context.json`. In `nextFocus`, find the Ecommerce container view. Open `B06-container/view-container.json` and find the Storefront API component view in `nextFocus`.
2. Open `B07-component/view-component.json`, then `B08-code-gap/view-code.json`. Check that the catalog and registry commits match the context view. Expect an explicit code gap for Order processing while the component view still has its supported items.

### B10 — Inspect support

1. Open `B07-component/view-component.json`. Find Order processing. Expect a source-owned IRI, its repository, baseline commit, supporting assertion and committed evidence.
2. Find Order processing → Address validation. Expect `basis: inferred` and `docs/acceptance-architecture.md` as evidence. Check the Markdown diagram legend for tentative inference.
3. Find `stale-mapping` for the `#unresolved` Legacy routing node. Expect it in gaps and outside the supported items.

### B11 — Keep identities distinct

1. Open `B11-order-labels/candidates.json`. Find the three concept leads labelled **Order**.
2. Expect separate Ecommerce, Warehouse and Accounting source IDs, source-owned IRIs and source commits. Check each commit against that source in the reported snapshot.

### B12 — Reproduce and advance

1. Compare the baseline `B06-container` and repeated `B12-repeat` views. Expect identical JSON and Markdown. The review index shows both comparisons.
2. Open `B12-newer/view-container.json`. Expect a different catalog commit and Ecommerce source commit, and **Order store v2**. Reopen the baseline and confirm it still displays **Order store** at its original source commit.

### B13 — Coverage and empty path

1. Open the component view and confirm supported items remain visible alongside unknown and excluded coverage.
2. Open `B13-empty-path/paths.json`. Expect zero paths, plus `unknown-paths`, `coverage-unknown` and `coverage-excluded`. Read the reason attached to the empty result.

### B14 — External context

1. Open `B14-bank/view-context.md`. Expect External bank outside Accounting and a supported connection.
2. Open `catalog.json` and the Accounting view's coverage table. Expect only the three participating sources in both inventories.

### B15 — Source admission with partial coverage

1. Open `catalog.json`. Expect Ecommerce with eight concepts, Warehouse with eight and Accounting with nine.
2. Confirm Ecommerce appears in the context view. Check the code-gap view for `src/**` marked unknown and `empty-level`.

## Clean up optional review files

Save any participant feedback you want to record, then run this block in the Terminal tab where you generated the review files.

```bash
node test/acceptance/c4-walkthrough.mjs teardown "$review_dir/fixture" &&
rmdir "$review_dir"
```

## Automated reruns

1. In Terminal, open the repository root on the acceptance machine and check out the revision you want to verify. On a clean checkout, install the locked dependencies and run the automation in one command.

   ```bash
   npm ci && npm run --silent acceptance:c4
   ```

   If the dependencies are already installed, run `npm run --silent acceptance:c4` by itself.
2. Read the machine, Node version and division revision. A passing run prints one `PASS` line. On a failure, record the failing test name, assertion and actual output. The command exits with a nonzero status and reports the discrepancies.
3. If a comparison fails, open the matching behaviour reference and use the review index to investigate. For B4, record your architect and reviewer observations separately. The automated check renders sample feedback and verifies its link to the assessment digest.

The command runs the regular repository suite and B1–B15. It creates disposable repositories, builds the baseline and revised catalogs, checks catalog consistency, renders the views, assessment and sample feedback, checks the expected values, and tears down the temporary inputs and outputs. Record the rerun result with the revision and date.

## Formal results

Record the automated result, date, revision and participant for the acceptance run. Record the participant’s qualitative B4 feedback when provided.

On 6 November 2026, James Mitchell reviewed the B1–B15 descriptions, approved all 15 executable checks and reported `PASS — B1–B15` on `M4Mini.local` using Node `v26.7.0` and division revision `83e3c98`. The automated B4 check used sample feedback.

On 6 November 2026, James Mitchell ran the consolidated command on `M4Mini.local` using Node `v26.7.0` and division revision `da31b91`. The command reported `PASS — repository tests and B1–B15`.

**Pilot acceptance: passed.** On 6 November 2026, James Mitchell accepted the B1–B15 automated results and confirmed that the pilot scope was sufficient. This is his B4 feedback for this pilot.
