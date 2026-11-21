# Traversal cursor pagination runbook

**Feature:** [#119](https://github.com/jamiemitchellconsultants/division-ontology/issues/119) · **Phase:** [#125 — User docs & acceptance](https://github.com/jamiemitchellconsultants/division-ontology/issues/125)

Cursor pagination adds no component, table, migration, setting or background job ([architecture](03-architecture.md#deployment-and-runtime)). It ships inside the existing workspace API release, so it is deployed, operated and recovered with the [runtime knowledge workspace runbook](../runtime-knowledge-workspace/05-runbook.md). This page covers only the extra traversal-paging steps.

**Deployed environment for acceptance:** the home-lab workspace on `ai-mcp-server` (Compose project `runtime-knowledge-workspace`), running the synthetic Ecommerce, Warehouse and Accounting fixture. The **local build** is the disposable Mac Docker stack started by `deploy/workspace/acceptance.sh local`.

## Deploy

PR #152 merged to `main` on 21 November 2026. A home-lab release from `main` at or after that merge includes the feature.

### Local build

From the root of a checkout that contains this runbook:

```bash
npm ci && node ./node_modules/playwright/cli.js install chromium
```

```bash
evidence_root=$(mktemp -d) && bash deploy/workspace/acceptance.sh local "$evidence_root/local"
```

`acceptance.sh local` runs the repository tests and the full .NET suite, including `TraversalPaginationAcceptanceTests` (B1–B19). It then runs `local/smoke.sh`, which starts the disposable stack, runs the browser smoke, and runs `verify-traversal-paging.mjs mac` before restarting and replaying. Check the results:

```bash
cat "$evidence_root/local/checks.tsv"; grep '"behaviour"' "$evidence_root/local/local-browser.log"
```

To run only the traversal acceptance tests, against a disposable database:

```bash
cd runtime && ../deploy/workspace/database/test-db.sh run dotnet test RuntimeKnowledge.slnx --filter "Category=Acceptance"
```

### Home lab

Use the existing one-command procedure from an acceptance checkout on `ai-mcp-server` ([home-lab acceptance](../runtime-knowledge-workspace/05-runbook.md#home-lab-acceptance)). On a checkout that already exists, run `git pull --ff-only` first so the runner includes the paging step.

```powershell
& ./deploy/workspace/run-home-lab-acceptance.ps1 -RunRoot $RunRoot
```

The runner deploys or reuses the `main` release, then runs these steps in order: **initial browser**, **traversal paging**, restart, guarded replay and persistence. The **traversal paging** step signs in as the fixture manager and reader. It creates 105 runtime relationships from Ecommerce **Order** to Warehouse **Order**, labelled `paging-acceptance-<run>-<n>`, and pages through them as the reader. It checks the cursor errors and staleness, then removes every relationship it created. The removals are journal events, so contribution history keeps them. The active snapshot is not changed.

Check the paging step's result:

```powershell
$Evidence = Read-Host 'Evidence directory printed by the runner'
(Get-Content -LiteralPath (Join-Path $Evidence 'run.json') -Raw | ConvertFrom-Json).steps | Format-Table name, status, message -Wrap
Get-Content -LiteralPath (Join-Path $Evidence 'traversal-paging.log')
```

Each passing behaviour prints one JSON line, and the log ends with `home-lab traversal paging acceptance passed` followed by `Removed 105 seeded relationships`.

## Operate

Health is unchanged: use the `/health` check and the logs under **Operate** in the workspace runbook. For paging, check these:

| Signal | Meaning | Action |
| --- | --- | --- |
| `200` with `nextCursor` and a `Link: </api/traverse>; rel="next"` header | Another page exists. | None. The client submits `nextCursor` with its original parameters. |
| `409 staleCursor` | A snapshot was activated, or a contribution was created, edited, removed or codified, after the cursor was issued. Any edit invalidates it (U3). | Expected after any workspace write. The client restarts from page one. If readers see it constantly, check for a client or job writing in a loop through `/api/history`. |
| `422 invalidCursor` | The cursor is malformed or was sent with different start, direction, `maxDepth` or `includeUnavailable`. | Client defect. Restart from page one with the intended parameters. |
| `422 invalidTraversal` asking to restart with a smaller `maxDepth` | The remaining frontier exceeds the 262,144-character cursor cap (B19). | Expected only for very wide, deep walks. The client narrows the walk. If it recurs on ordinary walks, raise it against [#139](https://github.com/jamiemitchellconsultants/division-ontology/issues/139). |

To probe paging on the deployed workspace without the whole runner, set the same fixture variables the runner sets (`WORKSPACE_VERIFY_ORIGIN` and the manager and reader `WORKSPACE_VERIFY_*_USERNAME`/`_PASSWORD`), then run:

```powershell
node deploy/workspace/verify-traversal-paging.mjs home-lab
```

This creates and removes 105 contributions, so use it on the synthetic acceptance fixture only, never on a workspace holding real use-case data.

## Recover

**Interrupted paging run.** If the paging step stops before its cleanup, for example because the terminal closed, some `paging-acceptance-*` relationships may remain. With the same environment variables set, remove them:

```powershell
node deploy/workspace/verify-traversal-paging.mjs home-lab --cleanup
```

It reads contribution history as the manager and removes every relationship still active whose custom label starts with `paging-acceptance-`. It prints how many it removed. If you run it again, it removes nothing.

**Rollback.** No migration is involved, so rolling back to a release before PR #152 uses the application rollback under **Recover** in the workspace runbook unchanged. After a rollback, the API ignores the `cursor` request field and answers every request with page one again, so a client that follows `nextCursor` loops on page one. If you roll back, tell API clients to stop paging until the feature is redeployed.

**Persistent `staleCursor` with no writes.** Compare the active snapshot (`replay describe-current`) and the latest history position before and after a reader request. Neither should move on a read (B18). If either does, stop and raise it against the build.
