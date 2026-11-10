# One-action source onboarding runbook

**Feature:** [#6](https://github.com/jamiemitchellconsultants/division-ontology/issues/6)

Acceptance uses two environments: the **local build** (the machine that runs `npm run acceptance:onboarding`, with scripted local repositories standing in for GitHub) and the **deployed environment** (a real division copy with its own pair of GitHub Apps and three test source repositories, named in the [acceptance record](05-acceptance.md#deployed-environment) before the run). The deployed environment has not been created yet.

## Deploy

Everything runs in GitHub Actions on the existing `Sync division catalog` workflow. There is no new service, database or webhook receiver.

1. **Create the division.** Create a repository from this template, clone it, run `npm ci`, then `node scripts/init-division.mjs --namespace https://<your-namespace>/ --steward <login> [--steward <login>]...`. Commit the result; the copy starts with no sources and no granted sources (B7).
2. **Create the two Apps and set the credentials**, exactly as [docs/automation-setup.md](../../automation-setup.md) section 1 describes: the division-writer App (installed on the division repository only) with `DIVISION_SYNC_APP_CLIENT_ID` and `DIVISION_SYNC_APP_PRIVATE_KEY`, and the source-read App (Contents: read and Metadata: read only, never installed on the division repository) with `DIVISION_SOURCE_APP_CLIENT_ID` and `DIVISION_SOURCE_APP_PRIVATE_KEY`. Set the client ID variable and the private key secret together. Do not put a key or token in a file, URL or argument list.
3. **Make the merge policy the gate** as automation-setup section 2 describes, so a refresh classified `automatic` can merge without a steward. Set `DIVISION_AUTO_MERGE_READY` only after the `merge-policy` check has been seen to run.
4. **Confirm the organisation allows source owners to add the source-read App** to repositories they administer (assumption U12). Do this before accepting B1.
5. **Run `Sync division catalog` once** (`gh workflow run "Sync division catalog"`). With no sources the run succeeds and opens no PR, which proves both Apps' tokens work.

For the local build: Node.js 20 or later, Git and `npm ci` in a clean checkout. No credentials are needed.

## Operate

- **Normal cycle.** The sync runs every two hours and on demand. It lists the source-read App's installation, imports each repository that carries `.division-ontology.json`, and updates the single `automation/catalog-refresh` PR. A refresh that changes only granted sources is classified automatic and merges without a steward; anything touching links, the core, `sources.json` or `division.json` waits for a steward.
- **Add or update a source.** Nothing is done in this repository. The source owner adds the repository to the source-read App and commits `.division-ontology.json`; later ontology commits arrive on the next sync. To see it sooner, dispatch the sync.
- **Read status.** Open `dist/catalog.md` and find **Import status**, or read `importStatus` in `dist/catalog.json`. Each granted source has one row with a status, its last attempted and last published commits, and a coded reason when it failed. The refresh PR body repeats the table.
- **Health checks.**
  - The latest `Sync division catalog` run is green: `gh run list --workflow "Sync division catalog" --limit 3`.
  - `npm test` passes in a clean checkout of the division revision.
  - For a deployed division: `node test/acceptance/onboarding-deployed.mjs <owner/division> <owner/source>=<status> ...` compares the published catalog with the live source repositories and prints `PASS` or each discrepancy. It is read-only and needs `gh` signed in with read access.
- **Local build of a division that holds granted sources** needs `DIVISION_SOURCE_APP=enabled` and `GH_SOURCE_TOKEN` set to a read-only installation token for the source-read App, passed through the environment only. Without them the command stops rather than treating the granted sources as removed.

## Recover

| Symptom | Meaning | Action |
| --- | --- | --- |
| The sync run fails with an HTTP, rate-limit or "incomplete" message | GitHub or the network failed, or the installation listing was cut short. `dist/` was left untouched and nothing was marked removed. | Wait for the next scheduled run, or dispatch it again. Check the Apps' tokens and the Actions variables if it repeats. |
| The sync stops with "no source-read App is configured" | The committed catalog holds granted sources but the source-read App variables are unset. | Set `DIVISION_SOURCE_APP_CLIENT_ID` and `DIVISION_SOURCE_APP_PRIVATE_KEY` together. Do not delete the granted rows. |
| A source is `invalid` | Its newest default-branch commit failed validation. The last valid version stays in the catalog marked stale. | Tell the source owner the reason code and detail from its row. When a valid commit lands the next sync returns it to `current`. |
| A source is `not configured` | The repository is in a selected-repositories installation but has no `.division-ontology.json`. | The owner commits the file, or removes the repository from the App. |
| A source is `access removed` | The repository is no longer in the installation. Its last published version stays, marked stale. | If unintended, add it back; the next sync resumes. |
| `namespace-conflict` | Its namespace overlaps the core's, a steward-registered source's or another source's. The existing holder is unaffected. | The owner chooses a namespace that does not overlap, or a steward resolves the clash. |
| A refresh PR waits for review | It changed links, the core, a linked concept, `sources.json` or `division.json`. | A steward reviews it as before. |

**Roll back.** `dist/` is generated and carries stale versions forward, so revert a bad catalog refresh by reverting its generated-only PR rather than editing `dist/`. Reverting also reverts what was carried forward until the next sync. To roll the tooling back, check out the previous reviewed division revision in a clean checkout, reinstall with `npm ci` and rerun the health checks.

**Known limit.** There is no supported way to remove a granted source that has lost access: it stays in the catalog, marked stale, until a steward decides what to do. This is recorded in the acceptance handoff.
