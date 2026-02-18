param(
    [string] $Repo = "",
    [string] $Pr = "",
    [string] $OutDir = "artifacts/first-pr-rollout",
    [switch] $RequireConfig,
    [switch] $RequireAnalysisSections
)

$ErrorActionPreference = "Stop"

function Fail([string] $Message) {
    Write-Error $Message
    exit 1
}

function Ensure-Tool([string] $Tool) {
    if (-not (Get-Command $Tool -ErrorAction SilentlyContinue)) {
        Fail "ERROR: $Tool is required"
    }
}

function Invoke-GhJson([string[]] $Args) {
    $output = & gh @Args
    if ($LASTEXITCODE -ne 0) {
        throw "gh command failed: gh $($Args -join ' ')"
    }
    return $output
}

if ([string]::IsNullOrWhiteSpace($Repo) -or [string]::IsNullOrWhiteSpace($Pr)) {
    Fail "ERROR: --Repo and --Pr are required"
}

Ensure-Tool "gh"
Ensure-Tool "rg"

& gh auth status *> $null
if ($LASTEXITCODE -ne 0) {
    Fail "ERROR: gh auth status failed; login required"
}

$root = ""
try {
    $root = (git rev-parse --show-toplevel 2>$null).Trim()
} catch {
    $root = ""
}
if ([string]::IsNullOrWhiteSpace($root)) {
    $root = (Get-Location).Path
}
Set-Location -LiteralPath $root

$stamp = Get-Date -Format "yyyyMMdd-HHmmss"
$safeRepo = $Repo.Replace("/", "_")
$runDir = Join-Path $OutDir ("$safeRepo-pr$Pr-$stamp")
New-Item -ItemType Directory -Force -Path $runDir | Out-Null

$prJsonPath = Join-Path $runDir "pr.json"
$checksPath = Join-Path $runDir "checks.txt"
$commentsPath = Join-Path $runDir "comments.txt"
$workflowPath = Join-Path $runDir "workflow.review-intelligencex.yml"
$reviewerPath = Join-Path $runDir "reviewer.json"

$prJson = Invoke-GhJson @("pr", "view", $Pr, "--repo", $Repo, "--json", "number,url,state,mergeable,mergeStateStatus,statusCheckRollup")
$prJson | Set-Content -LiteralPath $prJsonPath -Encoding UTF8

try {
    $checks = & gh pr checks $Pr --repo $Repo
    $checks | Set-Content -LiteralPath $checksPath -Encoding UTF8
} catch {
    if ($null -ne $checks) {
        $checks | Set-Content -LiteralPath $checksPath -Encoding UTF8
    }
}

$comments = Invoke-GhJson @("pr", "view", $Pr, "--repo", $Repo, "--json", "comments", "--jq", ".comments[].body")
$comments | Set-Content -LiteralPath $commentsPath -Encoding UTF8

$defaultBranch = (Invoke-GhJson @("repo", "view", $Repo, "--json", "defaultBranchRef", "--jq", ".defaultBranchRef.name")).Trim()
if ([string]::IsNullOrWhiteSpace($defaultBranch) -or $defaultBranch -eq "null") {
    Fail "ERROR: failed to resolve default branch"
}

$workflowContentB64 = Invoke-GhJson @("api", "repos/$Repo/contents/.github/workflows/review-intelligencex.yml?ref=$defaultBranch", "--jq", ".content")
$workflowBytes = [Convert]::FromBase64String(($workflowContentB64 -replace "\s", ""))
[System.IO.File]::WriteAllBytes($workflowPath, $workflowBytes)

try {
    $reviewerContentB64 = Invoke-GhJson @("api", "repos/$Repo/contents/.intelligencex/reviewer.json?ref=$defaultBranch", "--jq", ".content")
    $reviewerBytes = [Convert]::FromBase64String(($reviewerContentB64 -replace "\s", ""))
    [System.IO.File]::WriteAllBytes($reviewerPath, $reviewerBytes)
} catch {
    # reviewer.json may be absent in some modes; validation below controls requirement.
}

$validator = Join-Path $root ".agents/skills/intelligencex-reviewer-bootstrap/scripts/verify-managed-workflow.ps1"
if (Test-Path -LiteralPath $validator -PathType Leaf) {
    & $validator $workflowPath
    if ($LASTEXITCODE -ne 0) {
        Fail "ERROR: workflow managed block validation failed"
    }
} else {
    $wf = Get-Content -LiteralPath $workflowPath -Raw
    if ($wf -notmatch "(?m)^# INTELLIGENCEX:BEGIN\s*$") {
        Fail "ERROR: workflow missing INTELLIGENCEX:BEGIN"
    }
    if ($wf -notmatch "(?m)^# INTELLIGENCEX:END\s*$") {
        Fail "ERROR: workflow missing INTELLIGENCEX:END"
    }
}

if ($RequireConfig) {
    if (-not (Test-Path -LiteralPath $reviewerPath -PathType Leaf) -or [string]::IsNullOrWhiteSpace((Get-Content -LiteralPath $reviewerPath -Raw))) {
        Fail "ERROR: reviewer config required but missing on default branch"
    }
}

function Require-CheckSuccess([string] $Name) {
    $conclusion = (Invoke-GhJson @("pr", "view", $Pr, "--repo", $Repo, "--json", "statusCheckRollup", "--jq", ".statusCheckRollup[] | select(.name == `"$Name`") | .conclusion")) | Select-Object -First 1
    if ([string]::IsNullOrWhiteSpace($conclusion)) {
        Fail "ERROR: required check not found: $Name"
    }
    if ($conclusion.Trim().ToUpperInvariant() -ne "SUCCESS") {
        Fail "ERROR: check '$Name' is not SUCCESS (got: $conclusion)"
    }
}

Require-CheckSuccess "Static Analysis Gate"
Require-CheckSuccess "AI Review (Fail-Open)"
Require-CheckSuccess "Ubuntu"

$commentsRaw = Get-Content -LiteralPath $commentsPath -Raw
if ($commentsRaw -notmatch "<!-- intelligencex:summary -->") {
    Fail "ERROR: reviewer sticky summary marker not found"
}
if ($commentsRaw -notmatch "Reviewed commit:") {
    Fail "ERROR: reviewed commit label not found in comments"
}
if ($commentsRaw -notmatch "Diff range:") {
    Fail "ERROR: diff range label not found in comments"
}

if ($RequireAnalysisSections) {
    if ($commentsRaw -notmatch "### Static Analysis Policy") {
        Fail "ERROR: Static Analysis Policy section not found"
    }
    if ($commentsRaw -notmatch "### Static Analysis") {
        Fail "ERROR: Static Analysis section not found"
    }
}

Write-Host "OK: first PR rollout verification passed"
Write-Host "repo=$Repo"
Write-Host "pr=$Pr"
Write-Host "default_branch=$defaultBranch"
Write-Host "snapshot_dir=$runDir"
Write-Host "pr_json=$prJsonPath"
Write-Host "checks_txt=$checksPath"
Write-Host "comments_txt=$commentsPath"
Write-Host "workflow_yaml=$workflowPath"
if (Test-Path -LiteralPath $reviewerPath -PathType Leaf) {
    Write-Host "reviewer_json=$reviewerPath"
} else {
    Write-Host "reviewer_json=missing"
}
