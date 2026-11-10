# One-action source onboarding acceptance

**Feature:** [#6](https://github.com/jamiemitchellconsultants/division-ontology/issues/6)

Every behaviour B1–B17 has a scripted walkthrough and an executable acceptance test, and both are run in two environments:

- **Local build.** The scripted harness `test/acceptance/onboarding-walkthrough.mjs` creates two divisions (A and B) from this template with the real `init-division` command, five local Git repositories as sources, and a file-backed stand-in for the App installation. The executable story is `test/acceptance/onboarding.test.mjs`; it runs the same actions in the same order as the walkthrough below. One extra executable check exercises the real GitHub client over HTTP against a local API stand-in.
- **Deployed environment.** A real division copy with its own pair of GitHub Apps and three test source repositories, checked with the read-only `test/acceptance/onboarding-deployed.mjs`.

Participant, architect and reviewer: the person who runs the commands in [What to run](#what-to-run) and records each result below. They read and approve the executable tests; the commands, not the participant, compare actual with expected results.

**Status: prepared, not yet accepted.** The local executable layer has been run during preparation (see [Preparation run](#preparation-run)), but it has not been run on a clean checkout of a reviewed revision, no executable test has been approved by the participant, and the deployed environment does not exist yet. Nothing under [Formal results](#formal-results) has been recorded as passed.

## What to run

At the participant's direction (10 November 2026), the commands below compare actual results with the expected results themselves and stop only on an exception, printing what was expected and what was found. The step-by-step walkthroughs further down are the reference for investigating an exception; they are not followed by hand.

### Local build: one command

```bash
npm ci && npm run --silent acceptance:onboarding
```

Run it on a clean checkout of the reviewed revision. It prints the machine, Node version, revision and `Working tree: clean` (any other value is not valid as a record), runs the whole repository suite, then lists every acceptance check by name with `pass` or `FAIL`, and ends with `PASS — repository tests and one-action source onboarding B1–B17`. On a mismatch it prints the check's expected and actual values and exits nonzero. Nothing else is asked of you.

### Deployed environment

1. **One-time setup, by hand (it cannot be scripted):** follow the [runbook](05-runbook.md#deploy) to create the division copy, the two Apps, the Actions variables and the merge policy; create three test repositories (`alpha` and `beta` with no required content, `gamma` empty of `.division-ontology.json`) in the same organisation; and note the **installation id** of the source-read App, which is the number at the end of its settings URL (`…/settings/installations/<id>`). Do this on a fresh division, because step D1 expects an empty catalog.
2. **Review what the run will do, changing nothing** (this makes no calls to GitHub):

   ```bash
   node test/acceptance/onboarding-deployed.mjs run <owner/division> --alpha <owner/alpha> --beta <owner/beta> --gamma <owner/gamma> --installation <id> --dry-run
   ```

3. **Run it:**

   ```bash
   node test/acceptance/onboarding-deployed.mjs run <owner/division> --alpha <owner/alpha> --beta <owner/beta> --gamma <owner/gamma> --installation <id> --name "Your Name" --out "${TMPDIR:-/tmp}/onboarding-deployed-results.md"
   ```

   Through `gh`, as the signed-in user, it commits to the three test repositories and adds or removes them from the source-read App; it then dispatches the division's sync, waits for the run and for the refresh PR to merge, compares the published catalog with the live repositories, and moves to the next of its eleven steps. It stops at the first exception, prints the step, the expected and actual results and the steps not run, and exits nonzero. When all steps pass it prints a results table and the line `PASS — 11 steps covering …`. The `gh` token needs `repo` scope, and you must be an owner of the App installation; a permission error is reported as an exception with the `gh` message. Without `--installation` it asks you to make each GitHub change yourself (press Enter when done) and still compares automatically.
4. **Three behaviours need something the script cannot do for you.** The run prints one command for each when it finishes; each compares automatically after you have done the stated thing:

   | Behaviour | Needs | Command |
   | --- | --- | --- |
   | B3 | A steward-approved, merged `sources.json` PR registering `alpha` while it is also in the App | `node test/acceptance/onboarding-deployed.mjs --sync <owner/division> <owner/alpha>=registered` |
   | B4 | A second division with its own App that was never given `alpha` | `node test/acceptance/onboarding-deployed.mjs --sync <owner/second-division> <owner/alpha>=absent` |
   | B5 | The App installed on all repositories of the test organisation (only if acceptable there) | `node test/acceptance/onboarding-deployed.mjs --sync <owner/division> <owner/gamma>=absent` |

The deployed run has not yet been run against GitHub. The comparisons, the step list and the dry run are tested; dispatching the workflow, the `gh` calls and the PR polling are not, so the first real run may report an exception that is a tooling or permission problem rather than a product defect.

## Local build walkthrough (reference)

Use this to investigate a failing check. Open Terminal at the repository root of a clean checkout (zsh or bash) and run the following. Keep this Terminal tab for the whole walkthrough, because `x` and `w` exist only in it:

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

`npm run --silent acceptance:onboarding` (see [What to run](#what-to-run)) is the executable layer. Its acceptance file, `test/acceptance/onboarding.test.mjs`, runs the same actions in the same order as the walkthrough above as one ordered story: B7, B1, B6, B4, B5, B13, B8, B9, B11, B12 and B15 together, B10, B16, B14, B2, B3 and B17. It then runs B1 through the real GitHub client against a local API stand-in, and checks the deployed tooling itself: the checker's comparisons (including `registered` and `absent`), that the deployed steps and one-command checks cover B1–B17, that the dry run lists every action without touching GitHub, and that the run refuses to start without its repositories. A final check fails if any of B1–B17 is not named by an executable check. The participant reads this file once and approves it before the results are accepted.

## Deployed environment

Name these before the first deployed run and record them under [Formal results](#formal-results): the division repository, the source-read App and division-writer App, the organisation, and three test source repositories (`alpha` and `beta` with a valid ontology, `gamma` without `.division-ontology.json`) in the same organisation. A second division copy with its own App is needed only for B4.

**Preconditions.** The division was made from the template and `init-division`, both Apps are installed as the [runbook](05-runbook.md#deploy) says, `Sync division catalog` has run once successfully, and an owner of a test repository who has no write access to the division repository can add that repository to the source-read App (U12). If that is not allowed, record it: B1 cannot pass.

The `run` command above performs steps D1–D11 in the table and compares each result automatically; D12–D14 are the three separate commands in step 4. The automated check is `onboarding-deployed.mjs`'s comparison of the published catalog with the live repositories; you can also run it alone: `node test/acceptance/onboarding-deployed.mjs [--sync] <owner/division> <owner/source>=<status> …` and expect `PASS — … expectation(s) match …`. Statuses are `current`, `invalid`, `'not configured'`, `'access removed'`, `registered` (a steward-registered source with no status row) and `absent` (not in the catalog at all).

| Step | Behaviours | Action in GitHub | Expected result | Automated check |
| --- | --- | --- | --- | --- |
| D1 | B7 | None; open `dist/catalog.json` in the new division before any grant. | `sources` and `importStatus` are empty arrays. | catalog is empty |
| D2 | B1, B6, B2 | As the owner of `alpha` (no write access to the division repository), commit `.division-ontology.json` and a valid ontology to its default branch, then add `alpha` to the source-read App. | `alpha` is current at its head. A generated-only PR opens, is classified automatic and merges with no steward review. The `alpha` owner committed nothing in the division repository. | `alpha=current`, refresh PR merged |
| D3 | B13 | Add `gamma` (no `.division-ontology.json`) to the App with a selected-repositories installation. | `gamma` is `not configured` with no concepts; `alpha` is still current. | `alpha=current`, `gamma=not configured` |
| D4 | B8 | Commit a new term to `alpha`. | `alpha` is current at the new head with no renewed grant. | `alpha=current` |
| D5 | B9 | Move `alpha`'s ontology file and update its `.division-ontology.json` in the same commit. | `alpha` is current at the new head. | `alpha=current` |
| D6 | B11, B10 | Commit invalid Turtle to `alpha`. | `alpha` is `invalid`; the last valid version is still published, stale; the reason is a coded detail with no credential. | `alpha=invalid` |
| D7 | B11 | Commit a fix to `alpha`. | `alpha` is current at the fixing commit. | `alpha=current` |
| D8 | B16, B12, B15 | Commit a configuration and ontology for `beta` that use `alpha`'s namespace, and add `beta` to the App. | `beta` is `invalid` with `namespace-conflict` and is not published; `alpha` is still current in the same sync. | `alpha=current`, `beta=invalid` |
| D9 | B14 | Remove `alpha` from the App. | `alpha` is `access removed`, still in the catalog, stale. | `alpha=access removed` |
| D10 | B14 | Add `alpha` back to the App. | `alpha` is current again. | `alpha=current` |
| D11 | B17 | Make no changes; the check runs the sync twice. | The second run is green, opens no PR and leaves `main` unchanged. | `alpha=current`, `main` unchanged, no open refresh PR |
| D12 (separate command) | B3 | Open a `sources.json` PR registering `alpha` while it is also in the App, get a steward to approve it, and let it merge. | `alpha` appears once, as `registered`, with no status row. | `alpha=registered` |
| D13 (separate command) | B4 | In a second division with its own App, run its sync. | `alpha` is not in the second division's catalog. | second division `alpha=absent` |
| D14 (separate command) | B5 | If acceptable in the test organisation, install the App on all repositories. | Only repositories that carry `.division-ontology.json` appear; `gamma` has no row. | `gamma=absent` |

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
