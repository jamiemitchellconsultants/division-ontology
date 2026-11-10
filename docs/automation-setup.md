# One-time catalog automation setup

The workflows can be merged before credentials are configured. Until setup is complete, scheduled runs fail with a configuration message, and the existing branch review rule continues to govern PRs. Do not set `DIVISION_AUTO_MERGE_READY` until a live policy check and branch rule have been verified.

## 1. Create the two Apps

Each division creates its own pair of GitHub Apps, owned by the organisation or account that owns this repository. Webhooks are not needed for either.

**Division-writer App.** Repository **Contents: Read and write** and **Pull requests: Read and write**. Install it on this repository only. Record its client ID in the Actions variable `DIVISION_SYNC_APP_CLIENT_ID` and put its private key (the whole PEM) in the Actions secret `DIVISION_SYNC_APP_PRIVATE_KEY`.

**Source-read App.** Repository **Contents: read** and **Metadata: read**, and nothing else. This is the App source owners add their repositories to, so it must never be installed on this repository. Record its client ID in the Actions variable `DIVISION_SOURCE_APP_CLIENT_ID` and put its private key in the Actions secret `DIVISION_SOURCE_APP_PRIVATE_KEY`. Do not put either key or any token in a file, PR, URL or command argument.

The workflow mints a source token from the source-read App and a separate writer token, limited to this repository, from the division-writer App. Without the source-read App variables, the workflow falls back to the writer App for reading steward-registered sources and does not look for granted sources. A single-App setup therefore keeps working for steward-registered sources, but it cannot take grants, and the sync stops with a configuration message if the committed catalog already holds a granted source.

### How a source joins

A source owner needs only one action: add their repository to the source-read App's selected repositories (or install it on all repositories). The repository must have committed `.division-ontology.json` at the root of its default branch:

```json
{ "schemaVersion": 1, "ontologyPath": "docs/ontology.ttl", "namespace": "https://example.org/ontology#" }
```

The next normal sync, up to two hours later or when you run `Sync division catalog`, lists the App's installation, imports each configured repository's default-branch head, and publishes its concepts with a row in the catalog's import status table. That sync costs one request per installed repository to check for the file, so very large installations should be installed on selected repositories. With an **All repositories** installation only repositories that carry `.division-ontology.json` join; with selected repositories, a repository without it is reported as `not configured`.

A failed or incomplete listing, a rate limit or any other GitHub error fails the whole run and leaves `dist/` untouched; it is never read as removed access. A content problem in one source (invalid configuration, unreadable or invalid ontology, a namespace already in use) is reported in that source's status row and does not hold back the others; its last valid version stays in the catalog marked stale. Refreshes that only change granted sources merge without a steward. Links, shared terms, `sources.json` and `division.json` still need a steward.

Grants are discovered only in the division's own GitHub account or organisation: the source token is minted for this repository's owner, so an installation of the source-read App elsewhere is never listed and gets no status row. Set the client ID variable and the private key secret together; setting only one makes the workflow read with mismatched credentials and the run fails.

A steward-registered private source must also be installed on whichever App reads it. If it is both registered and granted, the registration governs.

## 2. Make the trusted policy the merge gate

After this feature PR merges, run `Sync division catalog` manually once. A current source may produce no PR; the run still proves source access and build health. Wait for a generated PR or a safe test PR to show the `merge-policy` check on its current head. Preserve PR-only merging, no force pushes, ordinary CI and existing Narrative controls on `main`.

The `Recheck division reviews` workflow polls review decisions every five minutes using default-branch code. When an approval or dismissal changes, it updates a hidden PR-body marker, which retriggers the trusted `pull_request_target` gate. The marker leaves the AI fingerprint footer intact. There is no PR-controlled review workflow with access to the App key; review effects may take one polling interval to appear. `merge-policy` only handles PRs targeting protected `main`.

Set required status checks to include `test`, `ai-fingerprint`, and `merge-policy` (from GitHub Actions), along with any other currently required checks. Do not require a path-filtered check on PRs where it never runs. Require the branch to be up to date with `main` before merge so changes to shared meaning or trusted policy are re-evaluated. Replace the blanket required approval/code-owner approval rule with the required `merge-policy` check; `.github/CODEOWNERS` continues to request review of sensitive paths, and the check requires a current approval from a steward listed in `division.json` for source registration, stewardship or namespace changes and linked-concept drift. Other authored changes pass this check; repository governance still calls for the applicable reviews. Do not give the App a branch-protection bypass.

Enable repository **Allow auto-merge**, then set the Actions variable `DIVISION_AUTO_MERGE_READY` to `true`. The sync workflow requests auto-merge only for an automatic classification; linked-concept drift remains open for review. If the rule change is unavailable, leave the variable unset and retain the existing approval rule.

## 3. Verify an end-to-end run

Trigger `Sync division catalog` with **Run workflow**. Confirm a no-change run exits successfully without a PR, or that a changed source updates the single `automation/catalog-refresh` PR. Check that its body lists source commits, its AI fingerprint check passes, `merge-policy` checks fresh output, and no credential appears in logs or generated files. On a linked-concept change, confirm that it waits for steward approval; after approving, wait for `Recheck division reviews` to rerun the gate. If the source moves after the PR was created, the policy fails it until the next sync refreshes the PR.

An inaccessible or malformed source causes a failed workflow and leaves the published `dist/` untouched. Correct the source or App installation, then use **Sync division ontology now** with a coding agent to retry.

To test a grant, commit `.division-ontology.json` to a scratch repository, add that repository to the source-read App, run `Sync division catalog`, and confirm the repository appears in `dist/catalog.md` under **Import status** with a `current` row. Remove it from the App and run the sync again to see `access removed`.
