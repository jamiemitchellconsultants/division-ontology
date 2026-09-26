# One-time catalog automation setup

The workflows can be merged before credentials are configured. Until setup is complete, scheduled runs fail with a configuration message, and the existing branch review rule continues to govern PRs. Do not set `DIVISION_AUTO_MERGE_READY` until a live policy check and branch rule have been verified.

## 1. Give the sync workflow scoped installation tokens

Create a GitHub App owned by `jamiemitchellconsultants`, with repository **Contents: Read and write** and **Pull requests: Read and write** permissions. Webhooks are not needed. Install it on **selected repositories**: `division-ontology` and the configured private sources, initially `JointBooking`. Record its client ID in the `division-ontology` Actions variable `DIVISION_SYNC_APP_CLIENT_ID`. Generate a private key and put its entire PEM content in the Actions secret `DIVISION_SYNC_APP_PRIVATE_KEY`. Do not put the key or token in a file, PR, URL, or command argument.

The workflow mints a source token with Contents **read** over the installed repositories, and a separate writer token with Contents and Pull requests **write** limited to `division-ontology`. The App itself still has latent Contents write permission across every repository in its installation. For stricter separation, use two Apps: a source-read App installed on sources and a division-writer App installed only here; adjust the two token steps and variable/secret names before setup. The single-App version needs the fewest administrative actions.

When a new private source is approved, add that repository to the App installation. Its entry in `sources.json` alone does not grant read access. All division readers are assumed to have permission to read every participating source's published concepts.

## 2. Make the trusted policy the merge gate

After this feature PR merges, run `Sync division catalog` manually once. A current source may produce no PR; the run still proves source access and build health. Wait for a generated PR or a safe test PR to show the `merge-policy` check on its current head. Preserve PR-only merging, no force pushes, ordinary CI and existing Narrative controls on `main`.

Set required status checks to include `test`, `ai-fingerprint`, and `merge-policy` (from GitHub Actions), along with any other currently required checks. Do not require a path-filtered check on PRs where it never runs. Require the branch to be up to date with `main` before merge so changes to shared meaning or trusted policy are re-evaluated. Replace the blanket required approval/code-owner approval rule with the required `merge-policy` check; `.github/CODEOWNERS` continues to request review of sensitive paths, and the check requires a current `@jamiemitchellconsultants` approval for them. Do not give the App a branch-protection bypass.

Enable repository **Allow auto-merge**, then set the Actions variable `DIVISION_AUTO_MERGE_READY` to `true`. The sync workflow requests auto-merge only for an automatic classification; linked-concept drift remains open for review. If the rule change is unavailable, leave the variable unset and retain the existing approval rule.

## 3. Verify an end-to-end run

Trigger `Sync division catalog` with **Run workflow**. Confirm a no-change run exits successfully without a PR, or that a changed source updates the single `automation/catalog-refresh` PR. Check that its body lists source commits, its AI fingerprint check passes, `merge-policy` checks fresh output, and no credential appears in logs or generated files. On a linked-concept change, confirm that it waits for steward approval. If the source moves after the PR was created, the policy fails it until the next sync refreshes the PR.

An inaccessible or malformed source causes a failed workflow and leaves the published `dist/` untouched. Correct the source or App installation, then use **Sync division ontology now** with a coding agent to retry.
