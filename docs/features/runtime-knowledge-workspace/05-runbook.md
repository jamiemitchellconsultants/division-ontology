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

Run the script in Administrator PowerShell on `ai-mcp-server`. Finish the current use case first, then use the same workspace deployment for acceptance. The [LocalAI installer guide](https://github.com/jamiemitchellconsultants/LocalAI/blob/main/docs/deploy-division-workspace.md) covers the host prerequisites.

If you already have the acceptance checkout, run `git pull --ff-only` there once to get the script. Use your existing `$RunRoot` to reuse its fixture and retain the prior evidence. On a new terminal or host, prepare a checkout once:

```powershell
. {
    $Repo = 'jamiemitchellconsultants/division-ontology'
    $PrJson = gh pr view 64 --repo $Repo --json state,headRefName
    if ($LASTEXITCODE -ne 0) { throw 'Could not find the acceptance checkout. Check gh auth status.' }
    $Pr = $PrJson | ConvertFrom-Json
    $AcceptanceRef = if ($Pr.state -eq 'MERGED') { 'main' } elseif ($Pr.state -eq 'OPEN') { $Pr.headRefName } else {
        throw 'The acceptance PR is closed without merging.'
    }
    $RunRoot = Join-Path ([IO.Path]::GetTempPath()) ("workspace-acceptance-" + [guid]::NewGuid())
    New-Item -ItemType Directory -Path $RunRoot -ErrorAction Stop | Out-Null
    $AcceptanceCheckout = Join-Path $RunRoot 'src'
    gh repo clone $Repo $AcceptanceCheckout -- --branch $AcceptanceRef --single-branch
    if ($LASTEXITCODE -ne 0) { throw 'Could not clone the acceptance checkout.' }
    Set-Location -LiteralPath $AcceptanceCheckout -ErrorAction Stop
}
```

From the acceptance checkout, run the six steps with one command:

```powershell
& ./deploy/workspace/run-home-lab-acceptance.ps1 -RunRoot $RunRoot
```

The script runs preflight and waits for the selected release images, installs the locked dependencies and browser, builds or reuses the fixture, publishes it on the existing workspace, collects test credentials and runs the deployed walkthrough. It reports exceptions and the new evidence directory. The deployed walkthrough checks browser search and persistence after restart and guarded replay. It records B14 separately for an independent reviewer. Use `-Operator 'Name'` to override the Windows username in the evidence.

For the optional checkout update and preflight retry, rerun with the same directory:

```powershell
& ./deploy/workspace/run-home-lab-acceptance.ps1 -RunRoot $RunRoot -RetryPreflight
```

`-RetryPreflight` adds `git pull --ff-only` before preparation. Both commands reuse a valid fixture bundle and a matching deployment, then create a fresh evidence directory. Use `-Redeploy` to republish the same fixture if the actual deployment has drifted from its installer evidence. Use `-ReleaseRef` to select a reviewed lightweight release tag. The default is `main`.

The runner uses the existing embedding configuration. The search checks require a working embedding service. Model identity and GGUF digest are outside acceptance. The preflight reports missing deployment settings so the operator can supply the existing configuration. If the previous use case has finished and its data can be discarded, the optional reset procedure is under **Operate**. Keep both volumes during acceptance.

Inspect the result from the latest evidence directory printed by the script:

```powershell
$Evidence = Read-Host 'Evidence directory printed by the runner'
$Run = Get-Content -LiteralPath (Join-Path $Evidence 'run.json') -Raw | ConvertFrom-Json
$Run | Select-Object status, snapshot, applicationRevision
$Run.steps | Where-Object status -ne 'pass' | Format-Table name, status, message -Wrap
$Run.humanReview
```

Record the outcome and any remaining host scenarios in the [acceptance record](05-acceptance.md). Use the recovery commands below if a run stopped while writes were paused.

## Operate

Check the running workspace from the operator terminal. Use the standard LocalAI installer paths below.

```powershell
$WorkspaceRoot = 'C:\mcp\division-workspace'
$Deployment = Join-Path $WorkspaceRoot 'deployment-evidence.json'
$ComposeArgs = @('compose', '-p', 'runtime-knowledge-workspace',
    '-f', "$WorkspaceRoot\src\deploy\workspace\home-lab\compose.yml",
    '--env-file', "$WorkspaceRoot\workspace.env")
docker @ComposeArgs ps -a
docker @ComposeArgs exec -T api curl -fsS http://127.0.0.1:8080/health
docker @ComposeArgs logs --tail 80 migrate api indexer
```

To publish the verified fixture independently of installation, use the same `$RunRoot` you passed to the script:

```powershell
$Fixture = Get-Content -LiteralPath (Join-Path $RunRoot 'fixture-state.json') -Raw | ConvertFrom-Json
$BundleDir = Join-Path $RunRoot "$($Fixture.directory)/bundle"
$ManifestId = $Fixture.manifestId
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

If the runner reports a DNS error such as `ERR_NAME_NOT_RESOLVED`, check the workspace and issuer names from the same Windows terminal:

```powershell
$Installed = Get-Content -LiteralPath $Deployment -Raw | ConvertFrom-Json
Clear-DnsClientCache
Resolve-DnsName $Installed.publicHost -Type A -ErrorAction Stop
Resolve-DnsName ([uri]$Installed.issuer).DnsSafeHost -Type A -ErrorAction Stop
curl.exe -fsSI --max-time 15 "https://$($Installed.publicHost)/"
if ($LASTEXITCODE -ne 0) { throw 'Check the workspace HTTPS route before retrying the browser.' }
```

If Windows resolves the names but Chromium still reports a DNS error on `ai-mcp-server`, rerun through that server's local Caddy:

```powershell
& ./deploy/workspace/run-home-lab-acceptance.ps1 -RunRoot $RunRoot -LocalIngress
```

`-LocalIngress` checks the workspace and Keycloak HTTPS endpoints on port 443 at `127.0.0.1` using their configured hostnames and normal certificate validation. It then maps those two names to loopback for the test browser. Use this option on the host that runs Caddy. The runner reuses the fixture and records the browser route in `run.json`.

Check indexer logs if search remains pending. Additional scenarios are listed in the [acceptance record](05-acceptance.md).

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
$Preparation = & ./deploy/workspace/prepare-home-lab.ps1 -RunRoot $RunRoot -WorkspaceRoot $WorkspaceRoot -ReleaseRef $PreviousReleaseRef
$RollbackArgs = $Preparation.InstallArgs
& $Preparation.Installer @RollbackArgs
if ($LASTEXITCODE -ne 0) { throw 'Release rollback failed.' }
docker @ComposeArgs ps -a
docker @ComposeArgs exec -T api curl -fsS http://127.0.0.1:8080/health
```

To rehearse projection recovery while the workspace is assigned to acceptance, run the shared replay procedure with the expected synthetic digest. It checks the active snapshot, pauses writes, compares the live and rebuilt state at the same journal position, switches generations when they match, and resumes writes.

```powershell
$Evidence = Read-Host 'Evidence directory printed by the runner'
$ManifestId = (Get-Content -LiteralPath (Join-Path $Evidence 'snapshot') -Raw).Trim()
$ProjectionReport = node deploy/workspace/verify-replay.mjs $ManifestId @ComposeArgs
if ($LASTEXITCODE -ne 0) { throw 'Replay failed. Review the exception and the active projection.' }
$ProjectionReport | Set-Content -LiteralPath (Join-Path $Evidence 'manual-replay.json') -Encoding UTF8
$ProjectionReport | ConvertFrom-Json | Select-Object -ExpandProperty active
```

If the runner was interrupted while writes were paused, confirm that the expected fixture is still active before resuming:

```powershell
$Evidence = Read-Host 'Evidence directory printed by the runner'
$ManifestId = (Get-Content -LiteralPath (Join-Path $Evidence 'snapshot') -Raw).Trim()
$CurrentProjection = docker @ComposeArgs run --rm --no-deps -T replay describe-current
if ($LASTEXITCODE -ne 0) { throw 'Could not confirm the active fixture.' }
if (($CurrentProjection | ConvertFrom-Json).snapshotId -ne $ManifestId) { throw 'The expected fixture is not active.' }
docker @ComposeArgs run --rm --no-deps -T replay writes resume
if ($LASTEXITCODE -ne 0) { throw 'Could not resume workspace writes.' }
```

Keep the PostgreSQL journal volume and referenced MinIO bundles available for replay. Loss of either, or recovery when the active projection cannot be compared, belongs to [backup and disaster recovery #53](https://github.com/jamiemitchellconsultants/division-ontology/issues/53).
