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

Run steps 1–7 in the same Administrator PowerShell on `ai-mcp-server`. Finish the current use case before assigning the existing workspace to acceptance. The [LocalAI installer guide](https://github.com/jamiemitchellconsultants/LocalAI/blob/main/docs/deploy-division-workspace.md) covers the host prerequisites.

1. **Identify the existing deployment and fetch the installer.** Use the workspace's existing hostname, project and Keycloak realm. The commands fetch the installer from the current LocalAI `main` commit.

   ```powershell
   Get-Command gh, git, node, npm, docker -ErrorAction Stop | Select-Object Name, Source
   gh auth status --hostname github.com
   if ($LASTEXITCODE -ne 0) { throw 'Sign in with gh auth login --hostname github.com, then retry.' }
   if (-not $env:MCP_GITHUB_TOKEN) {
       $env:MCP_GITHUB_TOKEN = [Environment]::GetEnvironmentVariable('MCP_GITHUB_TOKEN', 'Machine')
   }
   if (-not $env:MCP_GITHUB_TOKEN) { throw 'Set up the LocalAI installer credential first.' }
   $Repo = 'jamiemitchellconsultants/division-ontology'
   $WorkspaceRoot = 'C:\mcp\division-workspace'
   $Deployment = Join-Path $WorkspaceRoot 'deployment-evidence.json'
   $ComposeArgs = @('compose', '-p', 'runtime-knowledge-workspace',
       '-f', "$WorkspaceRoot\src\deploy\workspace\home-lab\compose.yml",
       '--env-file', "$WorkspaceRoot\workspace.env")
   $RunRoot = Join-Path ([IO.Path]::GetTempPath()) ("workspace-acceptance-" + [guid]::NewGuid())
   New-Item -ItemType Directory -Path $RunRoot | Out-Null
   $LocalAiCommit = gh api 'repos/jamiemitchellconsultants/LocalAI/commits/main' --jq '.sha'
   if ($LASTEXITCODE -ne 0 -or $LocalAiCommit -notmatch '^[a-f0-9]{40}$') { throw 'Could not resolve LocalAI main.' }
   Write-Host "LocalAI installer commit: $LocalAiCommit"
   $Installer = Join-Path $RunRoot 'setup-division-workspace-windows.ps1'
   $InstallerSource = gh api --method GET -H 'Accept: application/vnd.github.raw+json' `
       'repos/jamiemitchellconsultants/LocalAI/contents/docs/setup-division-workspace-windows.ps1' -f "ref=$LocalAiCommit"
   if ($LASTEXITCODE -ne 0) { throw 'Could not fetch the LocalAI installer.' }
   $InstallerSource | Set-Content -LiteralPath $Installer -Encoding UTF8
   & $Installer -WorkDir $WorkspaceRoot -Diagnostics
   $WorkspaceHost = if (Test-Path -LiteralPath $Deployment) {
       (Get-Content -LiteralPath $Deployment -Raw | ConvertFrom-Json).publicHost
   } else {
       Read-Host 'Workspace HTTPS hostname, without https://'
   }
   if (-not $WorkspaceHost) { throw 'Enter the workspace hostname before continuing.' }
   ```

2. **Check the release images and model.** Enter `main` or a reviewed lightweight `workspace-v*` release tag. Check for a successful image build for its exact commit.

   ```powershell
   $ReleaseRef = Read-Host 'Reviewed release ref (main or workspace-v* tag)'
   if ($ReleaseRef -ne 'main' -and $ReleaseRef -notlike 'workspace-v*') { throw 'Enter main or a reviewed workspace-v* tag.' }
   if ($ReleaseRef -ne 'main') {
       $TagTarget = gh api "repos/$Repo/git/ref/tags/$ReleaseRef" --jq '.object.type'
       if ($LASTEXITCODE -ne 0 -or $TagTarget -ne 'commit') { throw 'Select a lightweight release tag or main.' }
   }
   $ReleaseCommit = gh api "repos/$Repo/commits/$ReleaseRef" --jq '.sha'
   if ($LASTEXITCODE -ne 0 -or $ReleaseCommit -notmatch '^[a-f0-9]{40}$') { throw 'Could not resolve the release ref.' }
   gh run list --repo $Repo --workflow workspace-images.yml --commit $ReleaseCommit --status success --limit 3
   if ($LASTEXITCODE -ne 0) { throw 'Could not check image publication.' }
   ```

   If there is no successful run, publish the images and wait for the run. Use the run ID for the selected commit from the listing.

   ```powershell
   gh workflow run workspace-images.yml --repo $Repo --ref $ReleaseRef
   if ($LASTEXITCODE -ne 0) { throw 'Could not start image publication.' }
   gh run list --repo $Repo --workflow workspace-images.yml --commit $ReleaseCommit --limit 5
   $PublishRunId = Read-Host 'Image publication run ID'
   gh run watch $PublishRunId --repo $Repo --exit-status
   if ($LASTEXITCODE -ne 0) { throw 'Image publication did not succeed.' }
   ```

   On `ai-speed-core`, read the existing Qwen service's report:

   ```powershell
   Get-Content 'C:\llama.cpp-embeddings\install-report.json' -Raw | ConvertFrom-Json |
       Select-Object endpoint, servedModelName, dimensions, ggufSha256
   ```

   Back in the same `ai-mcp-server` terminal, enter those values and test the endpoint:

   ```powershell
   $EmbeddingEndpoint = Read-Host 'Qwen report endpoint'
   $EmbeddingModel = Read-Host 'Qwen report servedModelName'
   $ModelDigest = (Read-Host 'Qwen report ggufSha256').Trim().ToLowerInvariant()
   if ($ModelDigest -notmatch '^[a-f0-9]{64}$') { throw 'Enter the 64-character GGUF SHA-256.' }
   $Probe = Invoke-RestMethod -Uri $EmbeddingEndpoint -Method Post -ContentType 'application/json' -TimeoutSec 60 `
       -Body (@{model = $EmbeddingModel; input = 'Workspace acceptance health check'} | ConvertTo-Json -Compress)
   if (@($Probe.data[0].embedding).Count -ne 4096) { throw 'The embedding service did not return 4,096 dimensions.' }
   ```

3. **Check out the acceptance scripts and build the fixture.** Use the branch of the acceptance PR while it is open and `main` after it merges. This checkout sits outside the installer's managed `src` directory.

   ```powershell
   $AcceptanceRef = Read-Host 'Acceptance Git branch (feature/runtime-knowledge-workspace/user while PR #64 is open)'
   if (-not $AcceptanceRef) { throw 'Enter the acceptance branch.' }
   $AcceptanceCheckout = Join-Path $RunRoot 'src'
   gh repo clone $Repo $AcceptanceCheckout -- --branch $AcceptanceRef --single-branch
   if ($LASTEXITCODE -ne 0) { throw 'Could not clone the acceptance branch.' }
   Set-Location -LiteralPath $AcceptanceCheckout -ErrorAction Stop
   if (-not (Test-Path 'deploy/workspace/acceptance-home-lab.mjs' -PathType Leaf)) {
       throw 'Open the acceptance checkout containing deploy/workspace/acceptance-home-lab.mjs.'
   }
   npm ci
   if ($LASTEXITCODE -ne 0) { throw 'Dependency installation failed.' }
   node ./node_modules/playwright/cli.js install chromium
   if ($LASTEXITCODE -ne 0) { throw 'Chromium installation failed.' }
   $FixtureRoot = Join-Path $RunRoot 'fixture'
   $BundleDir = Join-Path $FixtureRoot 'bundle'
   $ManifestId = node src/workspace/seed-fixture.mjs (Join-Path $FixtureRoot 'checkouts') $BundleDir
   if ($LASTEXITCODE -ne 0 -or $ManifestId -notmatch '^[a-f0-9]{64}$') { throw 'Fixture build failed.' }
   ```

4. **Deploy in place and publish the fixture.** For a clean run, [reset the workspace data](#reset-between-use-cases) first if the previous use case has finished and its data can be discarded. Keep both volumes through acceptance so restart and replay can verify the same contributions.

   ```powershell
   $CurrentReleaseCommit = gh api "repos/$Repo/commits/$ReleaseRef" --jq '.sha'
   if ($LASTEXITCODE -ne 0 -or $CurrentReleaseCommit -ne $ReleaseCommit) { throw 'The release ref changed. Repeat step 2.' }
   & $Installer -WorkDir $WorkspaceRoot -PublicHost $WorkspaceHost -GitRef $ReleaseRef `
       -EmbeddingEndpoint $EmbeddingEndpoint -EmbeddingModel $EmbeddingModel -EmbeddingRevision $ModelDigest `
       -BundleDir $BundleDir -ManifestId $ManifestId
   if ($LASTEXITCODE -ne 0) { throw 'Fixture installation failed.' }
   $Installed = Get-Content -LiteralPath $Deployment -Raw | ConvertFrom-Json
   if ($Installed.commit -ne $ReleaseCommit -or $Installed.activeManifest -ne $ManifestId) {
       throw 'Installer evidence does not match the selected release and synthetic snapshot.'
   }
   docker @ComposeArgs ps -a
   ```

5. **Supply test identities.** The standard installer creates `manageknowledgegraph` and `readknowledgegraph` in the existing realm and prints their initial passwords when it creates them. Use these accounts or existing equivalents with the corresponding workspace roles. Enter passwords at the masked prompts.

   ```powershell
   $env:WORKSPACE_VERIFY_MANAGER_USERNAME = Read-Host 'Manager username'
   $env:WORKSPACE_VERIFY_MANAGER_PASSWORD = [System.Net.NetworkCredential]::new(
       '', (Read-Host 'Manager password' -AsSecureString)).Password
   $env:WORKSPACE_VERIFY_READER_USERNAME = Read-Host 'Reader username'
   $env:WORKSPACE_VERIFY_READER_PASSWORD = [System.Net.NetworkCredential]::new(
       '', (Read-Host 'Reader password' -AsSecureString)).Password
   ```

6. **Run the deployed walkthrough.** Run from the acceptance checkout where the Docker project and its HTTPS origin are reachable. The script reads the host, images and model revision from the installer evidence.

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
   if ($AcceptanceExit -ne 0) { Write-Warning 'Review the acceptance exception in step 7.' }
   ```

   The runner checks the configured and running image digests, service health and active fixture digest before making any test writes. It runs the browser checks, restarts only PostgreSQL and the API, checks persistence, performs guarded replay and checks persistence again. It resumes writes after a replay failure. `run.json` lists the steps and their outcomes. Browser logs record the pilot searches, index wait and query latency. The projection JSON files record the compared digests and journal positions.
7. **Review exceptions.** Print the run status, any failing step, and the human review items. Keep the full evidence directory.

   ```powershell
   $RunJson = Join-Path $Evidence 'run.json'
   if (Test-Path -LiteralPath $RunJson) {
       $Run = Get-Content -LiteralPath $RunJson -Raw | ConvertFrom-Json
       $Run | Select-Object status, snapshot, applicationRevision, embeddingRevision
       $Run.steps | Where-Object status -ne 'pass' | Format-Table name, status, message -Wrap
       $Run.humanReview
       Get-ChildItem -LiteralPath $Evidence | Select-Object Name, Length
   } else {
       Write-Warning 'The runner stopped before creating its evidence. Review the error from step 6.'
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

To publish a verified bundle independently of installation, use `$BundleDir` and `$ManifestId` from step 3:

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

For browser-only troubleshooting, open the acceptance checkout on the machine that can reach the workspace URL. Use the credential prompts in step 5 and a copy of the installer evidence:

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
& $Installer -WorkDir $WorkspaceRoot -PublicHost $WorkspaceHost -GitRef $PreviousReleaseRef `
    -EmbeddingEndpoint $EmbeddingEndpoint -EmbeddingModel $EmbeddingModel -EmbeddingRevision $ModelDigest
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
