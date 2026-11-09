# Runtime knowledge workspace runbook

## Deploy

Acceptance uses a disposable Mac Docker stack and the single LocalAI home-lab deployment while it is assigned to the synthetic acceptance use case. Use Node, Docker Compose, jq and the SDK pinned in `runtime/global.json`. Open a terminal at the root of the **same checkout that contains this runbook**. The runner and the pinned Playwright dependency are part of the acceptance changes.

```bash
if [ -f deploy/workspace/acceptance.sh ]; then
  npm ci &&
  node ./node_modules/playwright/cli.js install chromium &&
  evidence_root=$(mktemp -d) &&
  bash deploy/workspace/acceptance.sh local "$evidence_root/local"
else
  printf '%s\n' 'Open the acceptance checkout containing deploy/workspace/acceptance.sh.' >&2
  false
fi
```

The local runner builds and activates the Ecommerce, Warehouse and Accounting fixture. The Compose project, credentials, ports and volumes are isolated. The script restarts the API and PostgreSQL, verifies a guarded replay, then removes its containers and volumes. Set `WORKSPACE_PORT` if port 18087 is occupied. Keep the evidence directory for the acceptance record.

```bash
cat "$evidence_root/local/checks.tsv"
```

### Home-lab acceptance

Run steps 1–6 in the same Administrator PowerShell on `ai-mcp-server`. Finish the current use case before assigning the existing workspace to acceptance. The [LocalAI installer guide](https://github.com/jamiemitchellconsultants/LocalAI/blob/main/docs/deploy-division-workspace.md) covers the host prerequisites.

1. **Prepare the existing deployment in one command block.** This checks out the acceptance scripts, resolves the current workspace `main` commit, waits for its image build if needed, and fetches the LocalAI installer. It reads the existing workspace and embedding settings from the installer evidence. The preflight is quiet on success. If it starts an image build, wait for PowerShell to return to the prompt.

   ```powershell
   . {
       $Repo = 'jamiemitchellconsultants/division-ontology'
       $PrJson = gh pr view 64 --repo $Repo --json state,headRefName 2>$null
       if ($LASTEXITCODE -ne 0) { throw 'Could not find the acceptance checkout. Check gh auth status.' }
       $Pr = $PrJson | ConvertFrom-Json
       $AcceptanceRef = if ($Pr.state -eq 'MERGED') { 'main' } elseif ($Pr.state -eq 'OPEN') { $Pr.headRefName } else {
           throw 'The acceptance PR is closed without merging.'
       }
       $RunRoot = Join-Path ([IO.Path]::GetTempPath()) ("workspace-acceptance-" + [guid]::NewGuid())
       New-Item -ItemType Directory -Path $RunRoot -ErrorAction Stop | Out-Null
       $AcceptanceCheckout = Join-Path $RunRoot 'src'
       $null = gh repo clone $Repo $AcceptanceCheckout -- --branch $AcceptanceRef --single-branch 2>$null
       if ($LASTEXITCODE -ne 0) { throw 'Could not clone the acceptance checkout.' }
       Set-Location -LiteralPath $AcceptanceCheckout -ErrorAction Stop
       $Preparation = & ./deploy/workspace/prepare-home-lab.ps1 -RunRoot $RunRoot
       $Deployment = $Preparation.Deployment
       $ComposeArgs = $Preparation.ComposeArgs
       $InstallArgs = $Preparation.InstallArgs
   }
   ```

   Qwen provisioning is outside this runbook. If the preflight reports missing settings, set the named `WORKSPACE_PUBLIC_HOST` or `WORKSPACE_EMBEDDING_*` environment variables from the existing deployment or its operator, then repeat this step. To select a reviewed lightweight release tag, add `-ReleaseRef` to the preflight invocation. The default is `main`.

2. **Build the fixture.** Run these commands in the acceptance checkout opened by step 1:

   ```powershell
   npm ci
   if ($LASTEXITCODE -ne 0) { throw 'Dependency installation failed.' }
   node ./node_modules/playwright/cli.js install chromium
   if ($LASTEXITCODE -ne 0) { throw 'Chromium installation failed.' }
   $FixtureRoot = Join-Path $RunRoot 'fixture'
   New-Item -ItemType Directory -Path $FixtureRoot -ErrorAction Stop | Out-Null
   $BundleDir = Join-Path $FixtureRoot 'bundle'
   $ManifestId = node src/workspace/seed-fixture.mjs (Join-Path $FixtureRoot 'checkouts') $BundleDir
   if ($LASTEXITCODE -ne 0 -or $ManifestId -notmatch '^[a-f0-9]{64}$') { throw 'Fixture build failed.' }
   ```

3. **Deploy in place and publish the fixture.** For a clean run, [reset the workspace data](#reset-between-use-cases) first if the previous use case has finished and its data can be discarded. Keep both volumes through acceptance so restart and replay can verify the same contributions.

   ```powershell
   $CurrentReleaseCommit = gh api "repos/$Repo/commits/$($InstallArgs.GitRef)" --jq '.sha'
   if ($LASTEXITCODE -ne 0 -or $CurrentReleaseCommit -ne $Preparation.ReleaseCommit) { throw 'The release ref changed. Repeat step 1.' }
   & $Preparation.Installer @InstallArgs -BundleDir $BundleDir -ManifestId $ManifestId
   if ($LASTEXITCODE -ne 0) { throw 'Fixture installation failed.' }
   $Installed = Get-Content -LiteralPath $Deployment -Raw | ConvertFrom-Json
   if ($Installed.commit -ne $Preparation.ReleaseCommit -or $Installed.activeManifest -ne $ManifestId) {
       throw 'Installer evidence does not match the selected release and synthetic snapshot.'
   }
   docker @ComposeArgs ps -a
   ```

4. **Supply test identities.** The standard installer creates `manageknowledgegraph` and `readknowledgegraph` in the existing realm and prints their initial passwords when it creates them. Use these accounts or existing equivalents with the corresponding workspace roles. Enter passwords at the masked prompts.

   ```powershell
   $env:WORKSPACE_VERIFY_MANAGER_USERNAME = Read-Host 'Manager username'
   $env:WORKSPACE_VERIFY_MANAGER_PASSWORD = [System.Net.NetworkCredential]::new(
       '', (Read-Host 'Manager password' -AsSecureString)).Password
   $env:WORKSPACE_VERIFY_READER_USERNAME = Read-Host 'Reader username'
   $env:WORKSPACE_VERIFY_READER_PASSWORD = [System.Net.NetworkCredential]::new(
       '', (Read-Host 'Reader password' -AsSecureString)).Password
   ```

5. **Run the deployed walkthrough.** Run from the acceptance checkout where the Docker project and its HTTPS origin are reachable. The script reads the host, images and model revision from the installer evidence.

   ```powershell
   Set-Location -LiteralPath $AcceptanceCheckout -ErrorAction Stop
   $Operator = Read-Host 'Name to record as acceptance operator'
   $Evidence = Join-Path $RunRoot ("evidence-" + [guid]::NewGuid())
   try {
       node deploy/workspace/acceptance-home-lab.mjs --deployment $Deployment `
           --snapshot $ManifestId --evidence $Evidence --operator $Operator
       $AcceptanceExit = $LASTEXITCODE
   } finally {
       Remove-Item Env:WORKSPACE_VERIFY_MANAGER_PASSWORD, Env:WORKSPACE_VERIFY_READER_PASSWORD -ErrorAction SilentlyContinue
   }
   if ($AcceptanceExit -ne 0) { Write-Warning 'Review the acceptance exception in step 6.' }
   ```

   The runner checks the configured and running image digests, service health and active fixture digest before making any test writes. It runs the browser checks, restarts only PostgreSQL and the API, checks persistence, performs guarded replay and checks persistence again. It resumes writes after a replay failure. `run.json` lists the steps and their outcomes. Browser logs record the pilot searches, index wait and query latency. The projection JSON files record the compared digests and journal positions.
6. **Review exceptions.** Print the run status, any failing step, and the human review items. Keep the full evidence directory.

   ```powershell
   $RunJson = Join-Path $Evidence 'run.json'
   if (Test-Path -LiteralPath $RunJson) {
       $Run = Get-Content -LiteralPath $RunJson -Raw | ConvertFrom-Json
       $Run | Select-Object status, snapshot, applicationRevision, embeddingRevision
       $Run.steps | Where-Object status -ne 'pass' | Format-Table name, status, message -Wrap
       $Run.humanReview
       Get-ChildItem -LiteralPath $Evidence | Select-Object Name, Length
   } else {
       Write-Warning 'The runner stopped before creating its evidence. Review the error from step 5.'
   }
   ```

   Record the result and remaining host scenarios in the [acceptance record](05-acceptance.md). Record B14 when an independent reviewer is available. Reuse the same deployment for the next use case.

The runner derives the Compose path and environment file from the installer’s evidence directory. Use `--compose`, `--env-file` and `--project` if the existing workspace uses different paths or a different project name. If a runner is stopped while writes are paused, confirm the active fixture and follow the recovery steps below to resume them. Use a fresh evidence directory for each run.

## Operate

Check the running workspace from the operator terminal. Use the `$ComposeArgs` defined in step 1. If you opened a new terminal, enter the defaults below first.

```powershell
$WorkspaceRoot = 'C:\mcp\division-workspace'
$ComposeArgs = @('compose', '-p', 'runtime-knowledge-workspace',
    '-f', "$WorkspaceRoot\src\deploy\workspace\home-lab\compose.yml",
    '--env-file', "$WorkspaceRoot\workspace.env")
docker @ComposeArgs ps -a
docker @ComposeArgs exec -T api curl -fsS http://127.0.0.1:8080/health
docker @ComposeArgs logs --tail 80 migrate api indexer
```

To publish a verified bundle independently of installation, use `$BundleDir` and `$ManifestId` from step 2:

```powershell
docker @ComposeArgs run --rm --no-deps -T -v "${BundleDir}:/bundle:ro" publisher upload /bundle
if ($LASTEXITCODE -ne 0) { throw 'Bundle upload failed.' }
docker @ComposeArgs run --rm --no-deps -T publisher stage $ManifestId
if ($LASTEXITCODE -ne 0) { throw 'Bundle staging failed. Check the active snapshot before retrying.' }
docker @ComposeArgs run --rm --no-deps -T publisher activate $ManifestId
```

When activation reports `snapshotIndexPending`, wait and repeat the activation command. For any other error, inspect the publisher output and use the command below to confirm which snapshot is active:

```powershell
Start-Sleep -Seconds 3
docker @ComposeArgs run --rm --no-deps -T replay describe-current
```

For browser-only troubleshooting, open the acceptance checkout on the machine that can reach the workspace URL. Use the credential prompts in step 4 and a copy of the installer evidence:

```powershell
$BrowserEvidence = Read-Host 'Path to deployment-evidence.json'
$Installed = Get-Content -LiteralPath $BrowserEvidence -Raw | ConvertFrom-Json
if ($Installed.activeManifest -notmatch '^[a-f0-9]{64}$') { throw 'Expected the active synthetic manifest digest.' }
$env:WORKSPACE_SCENARIO_DIR = Join-Path ([IO.Path]::GetTempPath()) ("workspace-browser-" + [guid]::NewGuid())
New-Item -ItemType Directory -Path $env:WORKSPACE_SCENARIO_DIR | Out-Null
Set-Content -LiteralPath (Join-Path $env:WORKSPACE_SCENARIO_DIR 'snapshot') -Value $Installed.activeManifest -Encoding Ascii
$env:WORKSPACE_VERIFY_ORIGIN = "https://$($Installed.publicHost)"
$env:WORKSPACE_VERIFY_APP_VERSION = $Installed.commit
$env:WORKSPACE_VERIFY_MODEL_REVISION = $Installed.embeddingRevision
node deploy/workspace/verify-browser.mjs home-lab
if ($LASTEXITCODE -ne 0) { throw 'Browser checks failed.' }
```

For the browser-only restart check, restart the services from the operator terminal. Keep the browser's existing scenario directory for the second command:

```powershell
# On ai-mcp-server:
docker @ComposeArgs restart --no-deps db api
docker @ComposeArgs ps -a

# In the browser terminal, once the services are healthy:
try {
    node deploy/workspace/verify-browser.mjs home-lab --check-persistence
    if ($LASTEXITCODE -ne 0) { throw 'Persistence checks failed.' }
} finally {
    Remove-Item Env:WORKSPACE_VERIFY_MANAGER_PASSWORD, Env:WORKSPACE_VERIFY_READER_PASSWORD -ErrorAction SilentlyContinue
}
```

The first run records the contribution IDs in `scenario.json`. The persistence run looks them up and verifies traversal, history and indexed search. Record the operator, app revision, snapshot and model revision, along with the indexing wait and query latency logged by the browser. Check indexer logs for a provider or queue issue if search remains pending. Application-level checks that need replacement snapshots, additional identities or failure injection are listed in the [acceptance record](05-acceptance.md).

### Reset between use cases

When the current use case is finished and its data can be discarded, retain any acceptance evidence you need. With `$ComposeArgs` from **Operate**, the following PowerShell block validates the two volume names used by the standard LocalAI installer, stops the workspace and removes its PostgreSQL journal and MinIO bundles together.

```powershell
$ConfigJson = docker @ComposeArgs config --format json
if ($LASTEXITCODE -ne 0) { throw 'Could not resolve the workspace volumes.' }
$Volumes = ($ConfigJson | ConvertFrom-Json).volumes
Remove-Variable ConfigJson
$DatabaseVolume = $Volumes.database.name
$ObjectsVolume = $Volumes.objects.name
if ($DatabaseVolume -ne 'division-workspace-database' -or $ObjectsVolume -ne 'division-workspace-objects') {
    throw 'Expected the standard LocalAI workspace volume names.'
}
docker @ComposeArgs down
if ($LASTEXITCODE -ne 0) { throw 'Could not stop the workspace.' }
docker volume rm $DatabaseVolume $ObjectsVolume
if ($LASTEXITCODE -ne 0) { throw 'Check which workspace volume was removed before reinstalling.' }
```

Run the LocalAI installer for the next use case. It recreates the two application volumes. The deployment continues to use the same project, hostname, realm and embedding service.

## Recover

To roll back a published baseline, enter the previous verified digest. If the bundle needs uploading, use the upload command in **Operate** first. If activation reports `snapshotIndexPending`, wait and repeat it before checking the active digest.

```powershell
$PreviousManifestId = Read-Host 'Previous verified manifest digest'
if ($PreviousManifestId -notmatch '^[a-f0-9]{64}$') { throw 'Enter the 64-character manifest digest.' }
docker @ComposeArgs run --rm --no-deps -T publisher stage $PreviousManifestId
if ($LASTEXITCODE -ne 0) { throw 'Could not stage the previous bundle.' }
docker @ComposeArgs run --rm --no-deps -T publisher activate $PreviousManifestId
if ($LASTEXITCODE -ne 0) { throw 'Review activation output before continuing.' }
docker @ComposeArgs run --rm --no-deps -T replay describe-current
```

To roll back the application release, first confirm that the previous release is compatible with the installed database migrations. Then reuse the installer and existing volumes:

```powershell
$PreviousReleaseRef = Read-Host 'Previously deployed and compatible release ref'
if (-not $PreviousReleaseRef) { throw 'Enter the previous release ref.' }
$RollbackArgs = $InstallArgs.Clone()
$RollbackArgs.GitRef = $PreviousReleaseRef
& $Preparation.Installer @RollbackArgs
if ($LASTEXITCODE -ne 0) { throw 'Release rollback failed.' }
docker @ComposeArgs ps -a
docker @ComposeArgs exec -T api curl -fsS http://127.0.0.1:8080/health
```

To rehearse projection recovery while the workspace is assigned to acceptance, run the shared replay procedure with the expected synthetic digest. It checks the active snapshot, pauses writes, compares the live and rebuilt state at the same journal position, switches generations when they match, and resumes writes.

```powershell
Set-Location -LiteralPath $AcceptanceCheckout -ErrorAction Stop
$ProjectionReport = node deploy/workspace/verify-replay.mjs $ManifestId @ComposeArgs
if ($LASTEXITCODE -ne 0) { throw 'Replay failed. Review the exception and the active projection.' }
$ProjectionReport | Set-Content -LiteralPath (Join-Path $Evidence 'manual-replay.json') -Encoding UTF8
$ProjectionReport | ConvertFrom-Json | Select-Object -ExpandProperty active
```

If the runner was interrupted while writes were paused, confirm that the expected fixture is still active before resuming:

```powershell
$CurrentProjection = docker @ComposeArgs run --rm --no-deps -T replay describe-current
if ($LASTEXITCODE -ne 0) { throw 'Could not confirm the active fixture.' }
if (($CurrentProjection | ConvertFrom-Json).snapshotId -ne $ManifestId) { throw 'The expected fixture is not active.' }
docker @ComposeArgs run --rm --no-deps -T replay writes resume
if ($LASTEXITCODE -ne 0) { throw 'Could not resume workspace writes.' }
```

Keep the PostgreSQL journal volume and referenced MinIO bundles available for replay. Loss of either, or recovery when the active projection cannot be compared, belongs to [backup and disaster recovery #53](https://github.com/jamiemitchellconsultants/division-ontology/issues/53).
