param(
    [string] $Repo = "",
    [ValidateSet("setup", "update-secret", "cleanup")]
    [string] $Mode = "setup",
    [ValidateSet("enabled", "disabled")]
    [string] $Analysis = "enabled",
    [string] $Packs = "all-50",
    [switch] $ExplicitSecrets,
    [string] $OutDir = "artifacts/bootstrap-dry-run",
    [string] $Token = "",
    [string] $Branch = ""
)

$ErrorActionPreference = "Stop"

function Fail([string] $Message) {
    Write-Error $Message
    exit 1
}

function Extract-Section([string] $Content, [string] $SectionName) {
    $startMarker = "--- $SectionName ---"
    $lines = $Content -split "`r?`n"
    $capture = $false
    $result = [System.Collections.Generic.List[string]]::new()
    foreach ($line in $lines) {
        if (-not $capture) {
            if ($line -eq $startMarker) {
                $capture = $true
            }
            continue
        }

        if ($line.StartsWith("--- ")) {
            break
        }
        $result.Add($line)
    }
    return ($result -join [Environment]::NewLine).TrimEnd()
}

if ([string]::IsNullOrWhiteSpace($Repo)) {
    Fail "ERROR: --repo is required"
}

if ([string]::IsNullOrWhiteSpace($Token)) {
    $Token = ("" + $env:INTELLIGENCEX_GITHUB_TOKEN).Trim()
}
if ([string]::IsNullOrWhiteSpace($Token)) {
    $Token = ("" + $env:GITHUB_TOKEN).Trim()
}
if ([string]::IsNullOrWhiteSpace($Token)) {
    $Token = ("" + $env:GH_TOKEN).Trim()
}
if ([string]::IsNullOrWhiteSpace($Token)) {
    try {
        $Token = (gh auth token 2>$null).Trim()
    } catch {
        $Token = ""
    }
}
if ([string]::IsNullOrWhiteSpace($Token)) {
    Fail "ERROR: GitHub token not found. Provide --Token or set GITHUB_TOKEN/GH_TOKEN/INTELLIGENCEX_GITHUB_TOKEN."
}

$root = (git rev-parse --show-toplevel).Trim()
Set-Location -LiteralPath $root

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$safeRepo = $Repo.Replace("/", "_")
$runDir = Join-Path $OutDir ("$safeRepo-$Mode-$stamp")
New-Item -ItemType Directory -Force -Path $runDir | Out-Null

$rawLog = Join-Path $runDir "setup-dry-run.txt"
$reviewerJson = Join-Path $runDir "reviewer.generated.json"
$workflowYaml = Join-Path $runDir "workflow.generated.yml"

$cmd = [System.Collections.Generic.List[string]]::new()
$cmd.AddRange([string[]]@(
    "run", "--project", "IntelligenceX.Cli/IntelligenceX.Cli.csproj", "--framework", "net8.0", "--",
    "setup",
    "--repo", $Repo,
    "--github-token", $Token,
    "--dry-run"
))

if (-not [string]::IsNullOrWhiteSpace($Branch)) {
    $cmd.AddRange([string[]]@("--branch", $Branch))
}

switch ($Mode) {
    "setup" {
        $cmd.AddRange([string[]]@("--with-config", "--skip-secret"))
        if ($Analysis -eq "enabled") {
            $cmd.AddRange([string[]]@("--analysis-enabled", "true", "--analysis-packs", $Packs))
        } else {
            $cmd.AddRange([string[]]@("--analysis-enabled", "false"))
        }
        if ($ExplicitSecrets) {
            $cmd.Add("--explicit-secrets")
        }
    }
    "update-secret" {
        $cmd.Add("--update-secret")
    }
    "cleanup" {
        $cmd.AddRange([string[]]@("--cleanup", "--keep-secret"))
    }
}

$output = & dotnet $cmd 2>&1
if ($LASTEXITCODE -ne 0) {
    $output | Tee-Object -FilePath $rawLog
    Fail "ERROR: setup dry-run command failed."
}
$output | Tee-Object -FilePath $rawLog | Out-Host
$rawContent = ($output -join [Environment]::NewLine)

$reviewerSection = Extract-Section -Content $rawContent -SectionName ".intelligencex/reviewer.json"
$workflowSection = Extract-Section -Content $rawContent -SectionName ".github/workflows/review-intelligencex.yml"

Set-Content -LiteralPath $reviewerJson -Value $reviewerSection -Encoding UTF8
Set-Content -LiteralPath $workflowYaml -Value $workflowSection -Encoding UTF8

if ($Mode -eq "setup") {
    $reviewerContent = if (Test-Path -LiteralPath $reviewerJson) {
        Get-Content -LiteralPath $reviewerJson -Raw
    } else {
        [string]::Empty
    }
    $reviewerSkippedNoChanges = $rawContent -match "(?is)File:\s+\.intelligencex[\\/]+reviewer\.json\b[^\r\n]*(?:\bskip\b|\bunchanged\b|\bno\s*changes?\b)"
    if ([string]::IsNullOrWhiteSpace($reviewerContent) -and -not $reviewerSkippedNoChanges) {
        Fail "ERROR: setup mode expected reviewer config block in dry-run output."
    }
    if (-not [string]::IsNullOrWhiteSpace($reviewerContent)) {
        if ($reviewerContent -notmatch '"review"\s*:\s*\{') {
            Fail "ERROR: generated reviewer config missing review block."
        }
        if ($Analysis -eq "enabled" -and $reviewerContent -notmatch '"analysis"\s*:\s*\{') {
            Fail "ERROR: analysis requested but generated reviewer config has no analysis block."
        }
    }
}

if ($Mode -ne "update-secret") {
    if (-not (Test-Path -LiteralPath $workflowYaml) -or [string]::IsNullOrWhiteSpace((Get-Content -LiteralPath $workflowYaml -Raw))) {
        Fail "ERROR: expected workflow block in dry-run output."
    }

    $workflowContent = Get-Content -LiteralPath $workflowYaml -Raw
    if ($workflowContent -notmatch "(?m)^\s*# INTELLIGENCEX:BEGIN\s*$") {
        Fail "ERROR: generated workflow missing INTELLIGENCEX:BEGIN marker."
    }
    if ($workflowContent -notmatch "(?m)^\s*# INTELLIGENCEX:END\s*$") {
        Fail "ERROR: generated workflow missing INTELLIGENCEX:END marker."
    }
    if ($workflowContent -notmatch "(?m)^\s*uses:\s+(?:\./\.github/workflows/review-intelligencex-(?:core|reusable)\.yml|.+/\.github/workflows/review-intelligencex-(?:core|reusable)\.yml@.+)\s*$") {
        Fail "ERROR: generated workflow missing reusable review workflow reference."
    }
    if ($workflowContent -notmatch "(?m)^\s*if:\s+\$\{\{.+needs-ai-review.+\}\}\s*$") {
        Fail "ERROR: generated workflow missing fork/dependabot safety gate."
    }
}

Write-Host "OK: bootstrap dry-run checks passed"
Write-Host "run_dir=$runDir"
Write-Host "raw_log=$rawLog"
if (-not [string]::IsNullOrWhiteSpace((Get-Content -LiteralPath $reviewerJson -Raw))) {
    Write-Host "reviewer_json=$reviewerJson"
}
if (-not [string]::IsNullOrWhiteSpace((Get-Content -LiteralPath $workflowYaml -Raw))) {
    Write-Host "workflow_yaml=$workflowYaml"
}
