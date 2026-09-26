# Division Catalog Automation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Refresh the division catalog without human prompts while retaining steward approval for source registration, shared meaning and linked-concept drift.

**Architecture:** A scheduled workflow rebuilds the catalog from configured private repositories and maintains one generated-output PR. A trusted merge-policy workflow validates PR data using code from `main`; it permits deterministic catalog refreshes and requires a current steward approval for other changes. Provider-neutral agent instructions make onboarding a short prompt.

**Tech Stack:** Node.js 20+, N3 2.7.12, Git, GitHub Actions, GitHub App installation tokens, `actions/create-github-app-token@v3`, `peter-evans/create-pull-request@v8`, and a pinned `yaml` test dependency.

**Spec:** [Approved design](../specs/2026-09-26-catalog-automation-design.md)

## Global Constraints

- Change only `jamiemitchellconsultants/division-ontology`; no source-repository workflow or agent changes.
- Poll `sources.json` every 30 minutes and support `workflow_dispatch`.
- Preserve all source concepts and the existing three generated output files.
- A bot PR may change only `dist/catalog.json`, `dist/catalog.md` and `dist/combined.nq`; never push directly to `main` or grant a general branch-protection bypass.
- Keep credentials in Actions secrets and environment variables, never tracked files, Git configuration, command arguments or generated output.
- Use pull requests, the existing AI fingerprint footer, and the Project Narrative rules in `AGENTS.md`.
- Steward approval is required for `core/`, `links/`, `sources.json`, policy/workflow code, and generated changes that affect an existing linked concept.

## Review Focus

1. A private source is inaccessible or malformed: the sync fails with the source ID and leaves the committed catalog untouched (Task 2 test, Task 3 workflow failure path).
2. A linked local concept changes its label, definition, type, direct RDF statements or associated rule text: auto-merge is withheld (Task 1 tests).
3. A PR alters a generated file with content that a fresh build would not produce, including a symlink: trusted validation rejects it (Task 2 tests).
4. The source moves after a bot PR was opened: the trusted check rebuilds from current configured refs, rejects stale output, and the next sync updates the same PR (Task 2 test, Task 3 workflow).
5. A non-generated PR receives an old approval or approval from someone other than `@jamiemitchellconsultants`: the policy remains red until that steward approves the current head (Task 4 tests).

---

### Task 1: Decide when generated changes need a steward

**Files:**
- Create: `src/refresh-policy.mjs`
- Test: `test/refresh-policy.test.mjs`

**Interfaces:**
- `classifyRefresh({ previousCatalog, nextCatalog, previousNQuads, nextNQuads, changedPaths })` returns `{ mode: 'none' | 'automatic' | 'review', reasons: string[] }`.
- `mode: 'automatic'` requires a non-empty diff contained in the three `dist/` files, unchanged core triples, source IDs/namespaces and links, and no change to an existing linked local concept. Source commit SHAs may advance.
- Linked-concept comparison includes catalog label/definition/types; every N-Quad whose subject is the linked local IRI, ignoring its provenance graph; and source rules whose label equals the linked concept's label. Compare normalized sorted values so triple order and source-commit graph names alone do not count as drift.

- [ ] **Step 1: Write failing policy tests** for an unlinked new concept (`automatic`), linked definition/direct triple/rule changes (`review`), source or link changes (`review`), a non-`dist` path (`review`), and no change (`none`). Include a two-source fixture.
- [ ] **Step 2: Run** `node --test test/refresh-policy.test.mjs` and confirm the missing export or policy behavior fails for the intended reason.
- [ ] **Step 3: Implement** `classifyRefresh` with N3 N-Quads parsing and explicit sets of allowed paths and linked IRIs.
- [ ] **Step 4: Run** the focused test and `npm test`; both must pass.
- [ ] **Step 5: Commit** only this module and its test as `feat: classify safe catalog refreshes` after reviewing the staged diff.

### Task 2: Verify a PR's generated output with trusted code

**Files:**
- Create: `src/verify-refresh.mjs`
- Test: `test/verify-refresh.test.mjs`
- Modify: `src/cli.mjs` only if output calculation needs a reusable export.

**Interfaces:**
- `verifyRefresh({ baseRoot, candidateDist, changedPaths, checkoutOverrides = new Map() })` asynchronously returns Task 1's classification or throws with a specific error.
- It uses the base checkout's `core/`, `sources.json`, `links/` and builder code. Generate expected output in a disposable directory; compare each candidate `dist` file byte-for-byte and require a regular file. Never execute or import code from the PR checkout.
- The caller passes paths obtained from GitHub's PR files API, not from a PR body or workflow input. Non-generated paths are classified as `review` before any source credential is used.

- [ ] **Step 1: Write failing integration tests** with temporary Git source fixtures for a changed committed source, no change, tampered output, symlink output, missing source access and a source that advances after candidate generation. Assert the old committed `dist/` remains unchanged.
- [ ] **Step 2: Run** `node --test test/verify-refresh.test.mjs` and observe the expected failure.
- [ ] **Step 3: Implement** `verifyRefresh`, reusing `runCli`/`renderOutputs` from base code and Task 1's classifier. Keep temporary directories cleaned in `finally`.
- [ ] **Step 4: Run** the focused test and `npm test`; both must pass.
- [ ] **Step 5: Commit** the verifier and tests as `feat: verify generated refresh candidates` after reviewing the staged diff.

### Task 3: Create and update one catalog refresh PR

**Files:**
- Create: `.github/workflows/sync-catalog.yml`
- Create: `scripts/git-askpass.sh`
- Create: `src/refresh-body.mjs`
- Test: `test/refresh-body.test.mjs`
- Test: `test/git-askpass.test.mjs`
- Test: `test/workflow.test.mjs`
- Modify: `package.json` and `package-lock.json` for the pinned YAML test parser.

**Interfaces:**
- `renderRefreshBody({ catalog, mode, reasons, fingerprint })` returns Markdown with source commits/counts, review status and exactly one `AI-Fingerprint: sha256:<12 hex>` footer.
- `scripts/git-askpass.sh` returns the username `x-access-token` or the password from `GH_SOURCE_TOKEN`, based on Git's prompt. It never prints the token except as the answer to Git's password prompt.
- The workflow takes `vars.DIVISION_SYNC_APP_CLIENT_ID` and `secrets.DIVISION_SYNC_APP_PRIVATE_KEY`; it creates a source token with `owner: ${{ github.repository_owner }}` and `permission-contents: read`, then a writer token scoped to `division-ontology` with Contents and Pull requests write.

- [ ] **Step 1: Write failing tests** for PR body content/footer and askpass response without leaking a token to stderr or files. Add a workflow test that parses YAML and checks scheduled/manual triggers, App token scopes, output-only PR paths and no direct secret interpolation into shell scripts.
- [ ] **Step 2: Run** both focused test files and observe intended failures.
- [ ] **Step 3: Implement** body rendering and askpass. Add a 30-minute `schedule` plus `workflow_dispatch`, a concurrency group, `npm ci --ignore-scripts`, and a clear missing-configuration error. Set `GIT_ASKPASS`, `GIT_TERMINAL_PROMPT=0` and `GH_SOURCE_TOKEN` only for source cloning.
- [ ] **Step 4: Add PR creation/update** with `peter-evans/create-pull-request@v8`, fixed branch `automation/catalog-refresh`, `add-paths: dist/`, and `delete-branch: false`. After its final commit, fetch its SHA, compute the existing merge-base fingerprint, write the final body to a temporary file and update the PR with the writer token. Enable `gh pr merge --auto --squash` only when Task 1 says `automatic` and the one-time branch-rule setup is marked ready; otherwise leave the PR open for review. Never label a mechanical refresh `narrative-required`.
- [ ] **Step 5: Verify** `npm test`, a local build/check against clean JointBooking, and the workflow test; inspect that no secret is embedded in shell arguments or Git config.
- [ ] **Step 6: Commit** workflow, helper and tests as `feat: schedule catalog refresh pull requests` after reviewing the staged diff.

### Task 4: Enforce the merge decision from default-branch code

**Files:**
- Create: `.github/workflows/merge-policy.yml`
- Create: `src/merge-policy.mjs`
- Test: `test/merge-policy.test.mjs`
- Modify: `.github/CODEOWNERS`

**Interfaces:**
- `evaluateMergePolicy({ changedPaths, classification, reviews, headSha, stewardLogin })` returns `{ allowed: boolean, reason: string }`.
- Generated-only output passes without review only when Task 2 returns `automatic`. A validated Task 1 `review` result, or any other changed path, passes only when the latest review by `stewardLogin` is `APPROVED` for `headSha`; later `CHANGES_REQUESTED` or a new head invalidates it. A candidate that fails byte-for-byte output verification is never mergeable, even after approval.
- The required GitHub job name is `merge-policy`. It runs for PR opening/update and review submission/dismissal, checks out the base SHA, obtains changed paths from the API, and checks out PR head only as data with persisted credentials disabled. It never executes PR code with a secret.

- [ ] **Step 1: Write failing tests** for valid automatic refresh, linked drift without/with current steward approval, non-generated changes, stale approval, unrelated reviewer and superseding changes-requested review.
- [ ] **Step 2: Run** `node --test test/merge-policy.test.mjs` and observe intended failures.
- [ ] **Step 3: Implement** review evaluation and the trusted workflow. Use `pull_request_target` plus a review event; explicitly checkout the base SHA for both. Mint only a source-read App token on the generated-only path, run Task 2 validation, and return one stable required status. Do not run code from the PR checkout.
- [ ] **Step 4: Narrow CODEOWNERS** to review-sensitive paths (`core/`, `links/`, `sources.json`, `.github/`, `src/`, `scripts/`, `AGENTS.md`, `GOVERNANCE.md`, `README.md`), leaving `dist/` unowned. The trusted job supplies the actual conditional enforcement.
- [ ] **Step 5: Extend the workflow test** to parse the policy YAML and assert base-SHA checkout, restricted token use and a stable `merge-policy` job. Run focused tests, `npm test`, and a manual review of workflow token exposure and branch-rule assumptions.
- [ ] **Step 6: Commit** as `feat: enforce conditional division merge policy` after reviewing the staged diff.

### Task 5: Agent playbook, setup and release

**Files:**
- Modify: `AGENTS.md`
- Modify: `GOVERNANCE.md`
- Modify: `README.md`
- Create: `docs/automation-setup.md`

**Interfaces:**
- The short prompts are exactly **“Add `owner/repo` to division ontology”** and **“Sync division ontology now”**. The playbook gives agents the discovery/build/PR sequence and makes absence of a deterministic Turtle source an explicit stop condition.
- Setup names the App client ID variable and private-key secret from Task 3, installation repository scope, Actions auto-merge setting, required `test`, `ai-fingerprint` and `merge-policy` checks (while preserving existing Narrative checks), and the replacement of blanket owner review with conditional trusted policy. It states that adding a new private source requires adding it to the App installation.

- [ ] **Step 1: Document** the two prompts and the exact one-time UI setup, including the App's latent Contents write permission across installed repositories and the two-App stricter alternative.
- [ ] **Step 2: Review** the staged documentation against the approved spec and existing `AGENTS.md`/Narrative/AI fingerprint rules; run `git diff --cached --check` and `npm test`.
- [ ] **Step 3: Commit** as `docs: explain automated catalog onboarding and setup` after reviewing the staged diff.
- [ ] **Step 4: Run release verification:** all tests including workflow YAML checks, local build/check against JointBooking, a clean diff, and one fresh branch review. Create a PR to `main` with `narrative-required`, exact Narrative headings and a current AI fingerprint. Attach the PR to the task.
- [ ] **Step 5: After the PR is reviewed and merged, complete one-time setup** of the App, secret, variable, required checks and auto-merge. App installation expands repository access and branch-protection changes alter a security control; obtain action-time confirmation before applying either. Run `workflow_dispatch` and inspect the workflow result. If setup is unavailable, leave the PR and code ready, and report the exact remaining setup action without claiming unattended sync is active.
