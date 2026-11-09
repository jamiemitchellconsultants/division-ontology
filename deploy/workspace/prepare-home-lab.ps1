[CmdletBinding()]
param(
    [Parameter(Mandatory)] [string] $RunRoot,
    [string] $WorkspaceRoot = 'C:\mcp\division-workspace',
    [string] $ReleaseRef = 'main'
)

$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false
$Repo = 'jamiemitchellconsultants/division-ontology'
$LocalAiRepo = 'jamiemitchellconsultants/LocalAI'

function Invoke-Checked {
    param([string] $Command, [string[]] $Arguments, [string] $Failure)
    $Result = & $Command @Arguments 2>$null
    if ($LASTEXITCODE -ne 0) { throw $Failure }
    $Result -join "`n"
}

function Get-ReleaseRuns {
    $Arguments = @('run', 'list', '--repo', $Repo, '--workflow', 'workspace-images.yml',
        '--commit', $ReleaseCommit, '--limit', '20', '--json', 'databaseId,headSha,status,conclusion,url')
    $Result = Invoke-Checked 'gh' $Arguments 'Could not check workspace image publication.'
    ($Result | ConvertFrom-Json) | Where-Object headSha -eq $ReleaseCommit
}

Get-Command gh, git, node, npm, docker -ErrorAction Stop | Out-Null
$null = Invoke-Checked 'gh' @('auth', 'status', '--hostname', 'github.com') 'Run gh auth login --hostname github.com, then retry.'
$null = Invoke-Checked 'docker' @('compose', 'version') 'Docker Compose is unavailable.'
$null = Invoke-Checked 'docker' @('info', '--format', '{{.ServerVersion}}') 'The Docker engine is unavailable.'
if (-not $env:MCP_GITHUB_TOKEN) {
    $env:MCP_GITHUB_TOKEN = [Environment]::GetEnvironmentVariable('MCP_GITHUB_TOKEN', 'Machine')
}
if (-not $env:MCP_GITHUB_TOKEN) { throw 'The LocalAI installer needs its existing MCP_GITHUB_TOKEN.' }

$Deployment = Join-Path $WorkspaceRoot 'deployment-evidence.json'
$Installed = if (Test-Path -LiteralPath $Deployment) {
    Get-Content -LiteralPath $Deployment -Raw | ConvertFrom-Json
} else {
    @{}
}
$Existing = [ordered]@{
    WORKSPACE_PUBLIC_HOST = $Installed.publicHost
    WORKSPACE_EMBEDDING_ENDPOINT = $Installed.embeddingEndpoint
    WORKSPACE_EMBEDDING_MODEL = $Installed.embeddingModel
    WORKSPACE_EMBEDDING_REVISION = $Installed.embeddingRevision
}
foreach ($Key in @($Existing.Keys)) {
    $Configured = [Environment]::GetEnvironmentVariable($Key)
    if ($Configured) { $Existing[$Key] = $Configured }
}
$Missing = @($Existing.Keys | Where-Object { [string]::IsNullOrWhiteSpace($Existing[$_]) })
if ($Missing.Count) {
    throw "Missing existing workspace settings: $($Missing -join ', '). Supply those settings in the operator environment."
}
if ($Existing.WORKSPACE_EMBEDDING_REVISION -notmatch '^(sha256:)?[a-f0-9]{64}$') {
    Write-Warning 'The configured Qwen revision is a label. The walkthrough can run and will record the missing model digest.'
}

if ($ReleaseRef -ne 'main') {
    if ($ReleaseRef -notmatch '^workspace-v[\w.-]+$') { throw 'Select main or a lightweight workspace-v* release tag.' }
    $TagType = Invoke-Checked 'gh' @('api', "repos/$Repo/git/ref/tags/$ReleaseRef", '--jq', '.object.type') 'Could not resolve the release tag.'
    if ($TagType -ne 'commit') { throw 'Select a lightweight release tag or main.' }
}
$ReleaseCommit = Invoke-Checked 'gh' @('api', "repos/$Repo/commits/$ReleaseRef", '--jq', '.sha') 'Could not resolve the workspace release.'
if ($ReleaseCommit -notmatch '^[a-f0-9]{40}$') { throw 'GitHub did not return a valid workspace commit.' }

$Runs = @(Get-ReleaseRuns)
if (-not ($Runs | Where-Object conclusion -eq 'success')) {
    $Build = $Runs | Where-Object status -ne 'completed' | Select-Object -First 1
    if (-not $Build) {
        $SeenIds = @($Runs | ForEach-Object databaseId)
        $null = Invoke-Checked 'gh' @('workflow', 'run', 'workspace-images.yml', '--repo', $Repo, '--ref', $ReleaseRef) 'Could not start image publication.'
        for ($Attempt = 0; $Attempt -lt 12 -and -not $Build; $Attempt++) {
            Start-Sleep -Seconds 5
            $Build = Get-ReleaseRuns | Where-Object { $_.databaseId -notin $SeenIds } | Select-Object -First 1
        }
        if (-not $Build) { throw 'No image build appeared for the selected commit. Check the workspace image workflow.' }
    }
    $null = Invoke-Checked 'gh' @('run', 'watch', [string]$Build.databaseId, '--repo', $Repo, '--exit-status') "Workspace image publication failed. $($Build.url)"
}

$LocalAiCommit = Invoke-Checked 'gh' @('api', "repos/$LocalAiRepo/commits/main", '--jq', '.sha') 'Could not resolve LocalAI main.'
if ($LocalAiCommit -notmatch '^[a-f0-9]{40}$') { throw 'GitHub did not return a valid LocalAI commit.' }
$Installer = Join-Path $RunRoot 'setup-division-workspace-windows.ps1'
$InstallerSource = Invoke-Checked 'gh' @('api', '--method', 'GET', '-H', 'Accept: application/vnd.github.raw+json',
    "repos/$LocalAiRepo/contents/docs/setup-division-workspace-windows.ps1", '-f', "ref=$LocalAiCommit") 'Could not fetch the LocalAI installer.'
$InstallerSource | Set-Content -LiteralPath $Installer -Encoding UTF8

[pscustomobject]@{
    Deployment = $Deployment
    ReleaseCommit = $ReleaseCommit
    LocalAiCommit = $LocalAiCommit
    Installer = $Installer
    ComposeArgs = @('compose', '-p', 'runtime-knowledge-workspace',
        '-f', (Join-Path $WorkspaceRoot 'src/deploy/workspace/home-lab/compose.yml'),
        '--env-file', (Join-Path $WorkspaceRoot 'workspace.env'))
    InstallArgs = @{
        WorkDir = $WorkspaceRoot
        PublicHost = $Existing.WORKSPACE_PUBLIC_HOST
        GitRef = $ReleaseRef
        EmbeddingEndpoint = $Existing.WORKSPACE_EMBEDDING_ENDPOINT
        EmbeddingModel = $Existing.WORKSPACE_EMBEDDING_MODEL
        EmbeddingRevision = $Existing.WORKSPACE_EMBEDDING_REVISION
    }
}
