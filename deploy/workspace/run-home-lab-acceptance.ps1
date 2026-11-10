[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $RunRoot,
    [string] $WorkspaceRoot = 'C:\mcp\division-workspace',
    [string] $ReleaseRef = 'main',
    [string] $Operator = [Environment]::UserName,
    [switch] $RetryPreflight,
    [switch] $Redeploy,
    [switch] $LocalIngress
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$Repo = 'jamiemitchellconsultants/division-ontology'
$Checkout = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
New-Item -ItemType Directory -Path $RunRoot -Force | Out-Null
$RunRoot = (Resolve-Path -LiteralPath $RunRoot).Path
try {
    $Lock = [IO.File]::Open((Join-Path $RunRoot 'acceptance.lock'), 'OpenOrCreate', 'ReadWrite', 'None')
} catch {
    throw 'Could not lock the acceptance directory. Check whether another run is using it.'
}

$PromptedPasswords = @()
Push-Location -LiteralPath $Checkout
try {
    if ($RetryPreflight) {
        git pull --ff-only | Out-Null
        if ($LASTEXITCODE -ne 0) { throw 'Could not update the acceptance checkout.' }
    }
    $Preparation = & ./deploy/workspace/prepare-home-lab.ps1 -RunRoot $RunRoot -WorkspaceRoot $WorkspaceRoot -ReleaseRef $ReleaseRef

    npm ci --loglevel error --no-audit --no-fund | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Dependency installation failed.' }
    node ./node_modules/playwright/cli.js install chromium | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Chromium installation failed.' }

    $FixtureState = Join-Path $RunRoot 'fixture-state.json'
    $FixtureName = 'fixture'
    $ManifestId = $null
    if (Test-Path -LiteralPath $FixtureState) {
        $Saved = Get-Content -LiteralPath $FixtureState -Raw | ConvertFrom-Json
        if ($Saved.directory -notmatch '^fixture(-[a-f0-9]{32})?$' -or $Saved.manifestId -notmatch '^[a-f0-9]{64}$') {
            throw 'The saved fixture record is invalid.'
        }
        $FixtureName = $Saved.directory
        $ManifestId = $Saved.manifestId
    } else {
        $LegacyManifests = Join-Path $RunRoot 'fixture/bundle/manifests'
        if (Test-Path -LiteralPath $LegacyManifests -PathType Container) {
            $Manifests = @(Get-ChildItem -LiteralPath $LegacyManifests -Filter '*.json' -File)
            if ($Manifests.Count -gt 1) { throw 'The existing fixture has multiple manifests. Use a new run directory.' }
            if ($Manifests.Count -eq 1) { $ManifestId = $Manifests[0].BaseName }
        }
        if (-not $ManifestId -and (Test-Path -LiteralPath (Join-Path $RunRoot $FixtureName))) {
            $FixtureName = 'fixture-' + [guid]::NewGuid().ToString('N')
        }
    }
    $FixtureRoot = Join-Path $RunRoot $FixtureName
    $BundleDir = Join-Path $FixtureRoot 'bundle'
    if (-not $ManifestId) {
        New-Item -ItemType Directory -Path $FixtureRoot | Out-Null
        $ManifestId = node src/workspace/seed-fixture.mjs (Join-Path $FixtureRoot 'checkouts') $BundleDir
        if ($LASTEXITCODE -ne 0) { throw 'Fixture build failed.' }
    }
    if ($ManifestId -notmatch '^[a-f0-9]{64}$') { throw 'Expected a valid fixture manifest digest.' }
    $ManifestFile = Join-Path $BundleDir "manifests/$ManifestId.json"
    if (-not (Test-Path -LiteralPath $ManifestFile -PathType Leaf) -or
        (Get-FileHash -LiteralPath $ManifestFile -Algorithm SHA256).Hash -ne $ManifestId) {
        throw 'The fixture manifest is missing or its digest differs from its name.'
    }
    $Manifest = Get-Content -LiteralPath $ManifestFile -Raw | ConvertFrom-Json
    $SourceIds = ($Manifest.sources.id | Sort-Object) -join ','
    if ($SourceIds -ne 'fixture/accounting,fixture/ecommerce,fixture/warehouse') {
        throw 'The bundle must contain the three synthetic Order Fulfilment sources.'
    }
    if (-not (Test-Path -LiteralPath $FixtureState)) {
        @{ directory = $FixtureName; manifestId = $ManifestId } | ConvertTo-Json |
            Set-Content -LiteralPath "$FixtureState.tmp" -Encoding UTF8
        Move-Item -LiteralPath "$FixtureState.tmp" -Destination $FixtureState
    }

    $InstallArgs = $Preparation.InstallArgs
    $Deployment = $Preparation.Deployment
    $Installed = if (Test-Path -LiteralPath $Deployment) { Get-Content -LiteralPath $Deployment -Raw | ConvertFrom-Json }
    if ($Redeploy -or $Installed.commit -ne $Preparation.ReleaseCommit -or $Installed.activeManifest -ne $ManifestId -or
        $Installed.publicHost -ne $InstallArgs.PublicHost -or $Installed.embeddingEndpoint -ne $InstallArgs.EmbeddingEndpoint -or
        $Installed.embeddingModel -ne $InstallArgs.EmbeddingModel -or $Installed.embeddingRevision -ne $InstallArgs.EmbeddingRevision) {
        $CurrentRelease = gh api "repos/$Repo/commits/$ReleaseRef" --jq '.sha'
        if ($LASTEXITCODE -ne 0 -or $CurrentRelease -ne $Preparation.ReleaseCommit) {
            throw 'The release ref changed during preparation. Rerun the script.'
        }
        & $Preparation.Installer @InstallArgs -BundleDir $BundleDir -ManifestId $ManifestId
        if ($LASTEXITCODE -ne 0) { throw 'Fixture installation failed.' }
        $Installed = Get-Content -LiteralPath $Deployment -Raw | ConvertFrom-Json
        if ($Installed.commit -ne $Preparation.ReleaseCommit -or $Installed.activeManifest -ne $ManifestId) {
            throw 'Installer evidence does not match the selected release and synthetic snapshot.'
        }
    }

    foreach ($Role in @('MANAGER', 'READER')) {
        $UsernameKey = "WORKSPACE_VERIFY_${Role}_USERNAME"
        $PasswordKey = "WORKSPACE_VERIFY_${Role}_PASSWORD"
        if (-not [Environment]::GetEnvironmentVariable($UsernameKey)) {
            $Username = if ($Role -eq 'MANAGER') { 'manageknowledgegraph' } else { 'readknowledgegraph' }
            [Environment]::SetEnvironmentVariable($UsernameKey, $Username)
        }
        if (-not [Environment]::GetEnvironmentVariable($PasswordKey)) {
            $Username = [Environment]::GetEnvironmentVariable($UsernameKey)
            $Password = [System.Net.NetworkCredential]::new('', (Read-Host "Password for $Username" -AsSecureString)).Password
            [Environment]::SetEnvironmentVariable($PasswordKey, $Password)
            $PromptedPasswords += $PasswordKey
            Remove-Variable Password
        }
    }

    $Evidence = Join-Path $RunRoot ("evidence-" + [guid]::NewGuid())
    $BrowserArgs = @('deploy/workspace/acceptance-home-lab.mjs', '--deployment', $Deployment,
        '--snapshot', $ManifestId, '--evidence', $Evidence, '--operator', $Operator)
    if ($LocalIngress) { $BrowserArgs += '--local-ingress' }
    node @BrowserArgs
    if ($LASTEXITCODE -ne 0) { throw "Home-lab acceptance failed. See the exception above and $Evidence." }
} finally {
    foreach ($Key in $PromptedPasswords) { [Environment]::SetEnvironmentVariable($Key, $null) }
    Pop-Location
    $Lock.Dispose()
}
