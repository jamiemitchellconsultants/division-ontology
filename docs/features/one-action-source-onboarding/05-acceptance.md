# One-action source onboarding acceptance

**Feature:** [#6](https://github.com/jamiemitchellconsultants/division-ontology/issues/6)

Every behaviour B1–B17 has a scripted walkthrough and an executable acceptance test, and both are run in two environments:

- **Local build.** The scripted harness `test/acceptance/onboarding-walkthrough.mjs` creates two divisions (A and B) from this template with the real `init-division` command, five local Git repositories as sources, and a file-backed stand-in for the App installation. The executable story is `test/acceptance/onboarding.test.mjs`; it runs the same actions in the same order as the walkthrough below. One extra executable check exercises the real GitHub client over HTTP against a local API stand-in.
- **Deployed environment.** A real division copy with its own pair of GitHub Apps and three test source repositories, checked with the read-only `test/acceptance/onboarding-deployed.mjs`.

Participant, architect and reviewer: the person who follows the walkthroughs and records each result below. The executable tests are shown to them and approved after their walkthrough has passed.

**Status: prepared, not yet accepted.** The executable local story has been run during preparation (see [Preparation run](#preparation-run)) but no walkthrough has been followed by the participant, no executable test has been approved, and the deployed environment does not exist yet. Nothing below the [Formal results](#formal-results) heading has been recorded as passed.

## Local build walkthrough

Open Terminal at the repository root of a clean checkout (zsh or bash) and run the following. Keep this Terminal tab for the whole walkthrough, because `x` and `w` exist only in it:

```bash
npm ci
x() { node test/acceptance/onboarding-walkthrough.mjs "$@"; }
w="$(mktemp -d)/w"
$x prepare "$w"
```

Expect `Prepared divisions A and B and 5 sources in …`. The sources are `acceptance/alpha`, `beta`, `gamma` (no `.division-ontology.json`), `delta` (invalid Turtle from the start) and `epsilon`. `$x show "$w" A` prints the sources, the import status rows and the concepts of division A. Commits are shown as their first 12 characters. Where a step says "head", compare with `git -C "$w/sources/acceptance__alpha" rev-parse HEAD`.

### B7 — A new division starts empty

1. Run `$x build "$w" A && $x show "$w" A && cat "$w/A/sources.json"`.
2. Expect `build ok: 0 source(s), 0 granted`, `(none)` under Sources and Import status, and `sources.json` containing an empty `sources` array. Division A was made with `init-division`.

### B1 — Granting joins a source without touching the division

1. Run `shasum "$w/A/sources.json" "$w/A/division.json"` and keep the output.
2. Run `$x grant "$w" A acceptance/alpha && $x build "$w" A && $x show "$w" A`.
3. Expect `build ok: 1 source(s), 1 granted` and `acceptance/alpha  granted  stale=false  <commit>`. Run the `shasum` command again: both hashes are unchanged.

### B6 — Exact commit and every concept

1. In the `show` output from B1, compare alpha's commit with the head of the alpha repository (see above).
2. Expect them to match, one `current` row with the same attempted and published commit, and `acceptance/alpha: Alpha` under Concepts.

### B4 — A grant reaches exactly one division

1. Run `$x build "$w" B && $x show "$w" B`.
2. Expect `(none)` under Sources and Import status. Division B, with its own App, is unaffected by A's grant.

### B5 — An all-repositories installation admits only configured repositories

1. Run `$x selection "$w" A all && $x grant "$w" A acceptance/gamma && $x build "$w" A && $x show "$w" A`.
2. Expect no mention of `acceptance/gamma` under Sources or Import status. Alpha is still `current`.

### B13 — A selected repository without configuration is reported

1. Run `$x selection "$w" A selected && $x build "$w" A && $x show "$w" A`.
2. Expect `acceptance/gamma  not configured` with reason `config-missing: .division-ontology.json`, and gamma absent from Sources and Concepts.

### B8 — A new valid version needs no renewed grant

1. Run `$x commit "$w" A acceptance/alpha new-version && $x build "$w" A && $x show "$w" A`.
2. Expect alpha still `current` at the new head commit, and `acceptance/alpha: Alpha, NewTerm`.

### B9 — A configuration change is followed

1. Run `$x commit "$w" A acceptance/alpha move-ontology && $x build "$w" A && $x show "$w" A`. This moves the ontology file and updates `.division-ontology.json` to match.
2. Expect alpha `current` and `acceptance/alpha: Alpha, Moved`.

### B11 — An invalid new version keeps the last valid one

1. Note alpha's head commit, then run `$x commit "$w" A acceptance/alpha break && $x build "$w" A && $x show "$w" A`.
2. Expect alpha `invalid` with the new commit attempted, the noted commit published, `stale=true` under Sources at the noted commit, reason `ontology-invalid-turtle` with a line number, and concepts still `Alpha, Moved`.
3. Run `$x commit "$w" A acceptance/alpha fix && $x build "$w" A && $x show "$w" A`. Expect `current`, `stale=false`, the new head, and `Alpha, Fixed`.

### B12 and B15 — An invalid first version is not published and holds nothing back

1. Run `$x grant "$w" A acceptance/beta && $x grant "$w" A acceptance/delta && $x build "$w" A && $x show "$w" A`.
2. Expect beta `current` and listed under Sources with its concept. Expect delta `invalid` with published `-`, a turtle reason, and absent from Sources and Concepts. Alpha is unchanged.

### B10 — Every joined source has a status, with no credentials

1. In the `show` output, expect one row for each granted source under Sources (alpha, beta) with attempted and published commits, and a coded reason on each non-current row.
2. Run `grep -ci 'ghp_\|ghs_\|github_pat_\|x-access-token' "$w/A/dist/catalog.json" "$w/A/dist/catalog.md"`. Expect `0` for both files.

### B16 — A conflicting namespace is rejected

1. Run `$x commit "$w" A acceptance/beta claim-namespace acceptance/alpha && $x build "$w" A && $x show "$w" A`.
2. Expect beta `invalid` with reason `namespace-conflict: overlaps acceptance/alpha` and stale at its previous commit. Alpha stays `current` with its own namespace.

### B14 — Removed access leaves the last version stale; granting again resumes

1. Run `$x revoke "$w" A acceptance/alpha && $x revoke "$w" A acceptance/delta && $x build "$w" A && $x show "$w" A`.
2. Expect alpha `access removed` with published equal to its last head, `stale=true` under Sources, and concepts `Alpha, Fixed` still present. Expect no row for delta, which was never published.
3. Run `$x grant "$w" A acceptance/alpha && $x commit "$w" A acceptance/alpha new-version && $x build "$w" A && $x show "$w" A`. Expect alpha `current` at the new head.

### B2 — Granted-only refreshes are automatic

1. Run `$x classify "$w" A` straight after the B14 step 3 build.
2. Expect `Refresh classification: automatic`.

### B3 — Steward registration continues; a registered and granted repository appears once

1. Run `$x register "$w" A acceptance/epsilon && $x grant "$w" A acceptance/epsilon && $x build "$w" A && $x show "$w" A && $x classify "$w" A`.
2. Expect `acceptance/epsilon  registered` once under Sources and no import status row for it. Expect `Refresh classification: review (source registration changed)`.

### B17 — Unchanged inputs produce no refresh

1. Run `shasum "$w"/A/dist/*`, then `$x build "$w" A`, then `shasum "$w"/A/dist/*` again, then `$x check "$w" A`.
2. Expect identical hashes for all three files and `check ok: …`.

### Clean up

Run `$x teardown "$w"`.

## Local build executable tests

From the repository root run `npm run --silent acceptance:onboarding`. It prints the machine, Node version and revision, runs the whole repository suite and `test/acceptance/onboarding.test.mjs`, and prints one line: `PASS — repository tests and one-action source onboarding B1–B17`. On a failure it prints the failing test names and assertions and exits nonzero.

The 18 acceptance checks are named after the behaviours and run as one ordered story: B7, B1, B6, B4, B5, B13, B8, B9, B11, B12 and B15 together, B10, B16, B14, B2, B3 and B17; then B1 through the real GitHub client against a local API stand-in, and one check of the deployed-environment checker's own comparison logic. After the walkthrough has passed, the participant reads `test/acceptance/onboarding.test.mjs`, confirms it automates what they just did, and approves it.

## Deployed environment

Name these before the first deployed run and record them under [Formal results](#formal-results): the division repository, the source-read App and division-writer App, the organisation, and three test source repositories (`alpha` and `beta` with a valid ontology, `gamma` without `.division-ontology.json`) in the same organisation. A second division copy with its own App is needed only for B4.

**Preconditions.** The division was made from the template and `init-division`, both Apps are installed as the [runbook](05-runbook.md#deploy) says, `Sync division catalog` has run once successfully, and an owner of a test repository who has no write access to the division repository can add that repository to the source-read App (U12). If that is not allowed, record it: B1 cannot pass.

`check` below means: `node test/acceptance/onboarding-deployed.mjs <owner/division> <owner/source>=<status> …` and expect `PASS — … expectation(s) match …`. The walkthrough runs `gh workflow run "Sync division catalog"` after each action and waits for the run, then for the `automation/catalog-refresh` PR to merge where the step says so.

| Step | Action in GitHub | Expected result | Check |
| --- | --- | --- | --- |
| D1 · B7 | Open `dist/catalog.json` in the new division before any grant. | `sources` and `importStatus` are empty arrays. | none |
| D2 · B1, B6, B2 | As the owner of `alpha` (no write access to the division repository), commit `.division-ontology.json` and a valid ontology to `alpha`'s default branch, then add `alpha` to the source-read App. Run the sync. | A generated-only PR opens, is classified automatic and merges with no steward review. No commit or PR by the `alpha` owner exists in the division repository. | `alpha=current` |
| D3 · B13 | Add `gamma` (no `.division-ontology.json`) to the App with a selected-repositories installation. Run the sync. | `gamma` has a `not configured` row and no concepts. | `alpha=current 'gamma=not configured'` |
| D4 · B8, B9 | Commit a new term to `alpha`, then move its ontology file and update `.division-ontology.json`. Run the sync after each. | `alpha` is `current` at its new head each time, with no renewed grant. | `alpha=current` |
| D5 · B11, B10 | Commit invalid Turtle to `alpha`. Run the sync. Then commit a fix and run it again. | `alpha` is `invalid` with the old commit still published and stale, with a coded reason that contains no credential; after the fix it is `current`. | `alpha=invalid`, then `alpha=current` |
| D6 · B16 | Commit a `.division-ontology.json` and ontology for `beta` that claim `alpha`'s namespace, and add `beta` to the App. Run the sync. | `beta` is `invalid` with `namespace-conflict`; `alpha` is unaffected. | `alpha=current beta=absent` for a never-published `beta`, or `beta=invalid` once published |
| D7 · B12, B15 | Add a freshly valid `beta` (own namespace) and, at the same time, a repository with invalid Turtle and a valid configuration. Run the sync. | The valid one is `current`; the invalid one is `invalid` with no published commit and is not in the catalog. | each repository with its status |
| D8 · B14 | Remove `alpha` from the App. Run the sync. Add it back. Run the sync. | `alpha` is `access removed`, still in the catalog, stale; then `current` again. | `alpha=access removed`, then `alpha=current` |
| D9 · B17 | Run the sync twice with no changes. | The second run is green and opens no PR; `dist/` is byte-identical. | `alpha=current` |
| D10 · B3 | Open a `sources.json` PR registering `alpha` while it is also in the App. | The PR needs a steward approval. After merge `alpha` appears once, as `registered`, with no status row. | the row is absent: `alpha=absent` |
| D11 · B4 | With a second division copy and its own App, check it after D2. | The second division's catalog does not contain `alpha`. | `<second division>`, `alpha=absent` |
| D12 · B5 | Install the source-read App on all repositories of the test organisation, if that is acceptable there. Run the sync. | Only repositories that carry `.division-ontology.json` appear; others have no row. | each repository |

## Preparation run

During preparation, before any acceptance, `npm run --silent acceptance:onboarding` was run on a working tree containing these tests and reported `PASS — repository tests and one-action source onboarding B1–B17` on `M4Mini.local` with Node `v26.7.0`. This is a developer check that the executable story works. It is not an acceptance result: the tests had not been approved and the tree was not a clean checkout of a reviewed revision.

## Formal results

Record the walkthrough result (pass or fail for each step), the executable result, the date, the revision and the person for every behaviour in each environment. A failure is pushback to the build, not something to fix here.

| Behaviour | Local walkthrough | Local executable | Deployed walkthrough | Deployed check |
| --- | --- | --- | --- | --- |
| B1 | not yet run | not yet run | not yet run | not yet run |
| B2 | not yet run | not yet run | not yet run | not yet run |
| B3 | not yet run | not yet run | not yet run | not yet run |
| B4 | not yet run | not yet run | not yet run | not yet run |
| B5 | not yet run | not yet run | not yet run | not yet run |
| B6 | not yet run | not yet run | not yet run | not yet run |
| B7 | not yet run | not yet run | not yet run | not yet run |
| B8 | not yet run | not yet run | not yet run | not yet run |
| B9 | not yet run | not yet run | not yet run | not yet run |
| B10 | not yet run | not yet run | not yet run | not yet run |
| B11 | not yet run | not yet run | not yet run | not yet run |
| B12 | not yet run | not yet run | not yet run | not yet run |
| B13 | not yet run | not yet run | not yet run | not yet run |
| B14 | not yet run | not yet run | not yet run | not yet run |
| B15 | not yet run | not yet run | not yet run | not yet run |
| B16 | not yet run | not yet run | not yet run | not yet run |
| B17 | not yet run | not yet run | not yet run | not yet run |

**Acceptance: not yet run.**
