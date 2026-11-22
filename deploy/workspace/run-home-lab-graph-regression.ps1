# Run on the home-lab operator machine with Node dependencies and Chromium installed.
[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $FixtureRunRoot,
    [Parameter(Mandatory)] [ValidatePattern('^[a-f0-9]{40}$')] [string] $ExpectedRevision,
    [Parameter(Mandatory)] [ValidatePattern('^[a-f0-9]{64}$')] [string] $Snapshot,
    [string] $WorkspaceRoot = 'C:\mcp\division-workspace',
    [string] $Origin = 'https://division.tqaentry.com',
    [string] $Reader = 'readknowledgegraph',
    [string] $Manager = 'manageknowledgegraph',
    [string] $Operator = [Environment]::UserName,
    [ValidatePattern('^B([1-9]|1[0-5])$')] [string[]] $Behaviour,
    [switch] $AllowFixtureWrites,
    [switch] $UseSystemDns
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$FixtureRunRoot = (Resolve-Path -LiteralPath $FixtureRunRoot).Path
$Deployment = Join-Path $WorkspaceRoot 'deployment-evidence.json'
$Installed = Get-Content -LiteralPath $Deployment -Raw | ConvertFrom-Json
if ($Installed.commit -cne $ExpectedRevision) {
    $ActualRevision = if ($Installed.commit -cmatch '^[a-f0-9]{40}$') { $Installed.commit } else { 'invalid deployment evidence' }
    throw "FAIL | deployed revision | expected: $ExpectedRevision | actual: $ActualRevision"
}
$Target = [uri] $Origin
if (-not $Target.IsAbsoluteUri -or $Target.Scheme -ne 'https' -or $Target.Host -ne $Installed.publicHost -or
    $Origin -cne $Target.GetLeftPart([UriPartial]::Authority)) {
    throw 'FAIL | workspace host | expected: HTTPS origin matching deployment evidence | actual: mismatch'
}
$Fixture = Get-Content -LiteralPath (Join-Path $FixtureRunRoot 'fixture-state.json') -Raw | ConvertFrom-Json
if ($Fixture.manifestId -cne $Snapshot) {
    throw 'FAIL | retained fixture | expected: the requested snapshot | actual: fixture-state.json differs'
}

$NeedsManager = -not $Behaviour -or @($Behaviour | Where-Object { $_ -notin @('B1', 'B2', 'B6', 'B9') }).Count -gt 0
if ($NeedsManager -and -not $AllowFixtureWrites) {
    throw 'FAIL | write approval | expected: -AllowFixtureWrites for cases with temporary records | actual: missing'
}
$Roles = if ($NeedsManager) { @('READER', 'MANAGER') } else { @('READER') }
$Accounts = @{ READER = $Reader; MANAGER = $Manager }

function Set-GraphFixturePassword([string] $Role) {
    $Name = $Accounts[$Role]
    $Secure = Read-Host "Keycloak password for the existing fixture account $Name" -AsSecureString
    try {
        if ($Secure.Length -eq 0) { throw "A password is required for $Name." }
        [Environment]::SetEnvironmentVariable(
            "WORKSPACE_VERIFY_${Role}_PASSWORD", [Net.NetworkCredential]::new('', $Secure).Password, 'Process')
    } finally {
        $Secure.Dispose()
    }
}

$Runner = Join-Path $PSScriptRoot 'graph-regression/run.mjs'
$Checkout = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
if (-not (Test-Path -LiteralPath (Join-Path $Checkout 'node_modules/playwright/package.json'))) {
    throw 'FAIL | browser dependencies | expected: locked Playwright package | actual: missing. Run npm ci in the checkout.'
}
$Arguments = @($Runner, '--deployment', $Deployment, '--expected-revision', $ExpectedRevision,
    '--fixture-root', $FixtureRunRoot, '--snapshot', $Snapshot,
    '--origin', $Origin, '--evidence-dir', $FixtureRunRoot, '--operator', $Operator)
foreach ($Id in $Behaviour) { $Arguments += @('--case', $Id.ToUpperInvariant()) }
if ($AllowFixtureWrites) { $Arguments += '--allow-fixture-writes' }
if (-not $UseSystemDns) { $Arguments += '--local-ingress' }

$LockPath = Join-Path $FixtureRunRoot 'acceptance.lock'
try {
    $Lock = [IO.File]::Open($LockPath, 'OpenOrCreate', 'ReadWrite', 'None')
} catch {
    throw 'Could not lock this fixture directory. Check for an active acceptance run.'
}
try {
    foreach ($Role in $Roles) {
        $UsernameKey = "WORKSPACE_VERIFY_${Role}_USERNAME"
        $PasswordKey = "WORKSPACE_VERIFY_${Role}_PASSWORD"
        $CachedAccount = [Environment]::GetEnvironmentVariable($UsernameKey, 'Process')
        [Environment]::SetEnvironmentVariable($UsernameKey, $Accounts[$Role], 'Process')
        if (($CachedAccount -and $CachedAccount -cne $Accounts[$Role]) -or
            -not [Environment]::GetEnvironmentVariable($PasswordKey, 'Process')) {
            Set-GraphFixturePassword $Role
        }
    }

    $Retried = @()
    do {
        node @Arguments
        $ExitCode = $LASTEXITCODE
        if ($ExitCode -eq 0) { break }
        $Rejected = switch ($ExitCode) {
            21 { 'READER' }
            22 { 'MANAGER' }
        }
        if (-not $Rejected -or $Rejected -in $Retried) {
            throw "Graph Explorer regression failed (exit $ExitCode). See the result lines and run.json."
        }
        $Retried += $Rejected
        Set-GraphFixturePassword $Rejected
    } while ($true)
} finally {
    $Lock.Dispose()
}
