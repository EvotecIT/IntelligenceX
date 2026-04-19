param(
    [string] $WorkflowPath = ".github/workflows/review-intelligencex.yml"
)

$ErrorActionPreference = "Stop"

function Fail([string] $Message) {
    Write-Error $Message
    exit 1
}

if (-not (Test-Path -LiteralPath $WorkflowPath -PathType Leaf)) {
    Fail "ERROR: workflow file not found: $WorkflowPath"
}

$content = Get-Content -LiteralPath $WorkflowPath -Raw

if ($content -notmatch "(?m)^\s*# INTELLIGENCEX:BEGIN\s*$") {
    Fail "ERROR: missing INTELLIGENCEX:BEGIN marker"
}
if ($content -notmatch "(?m)^\s*# INTELLIGENCEX:END\s*$") {
    Fail "ERROR: missing INTELLIGENCEX:END marker"
}
if ($content -notmatch "(?m)^\s*review:\s*$") {
    Fail "ERROR: missing review job in workflow"
}
if ($content -notmatch "(?m)^\s*uses:\s+(?:\./\.github/workflows/review-intelligencex-(?:core|reusable)\.yml|.+/\.github/workflows/review-intelligencex-(?:core|reusable)\.yml@.+)\s*$") {
    Fail "ERROR: missing reusable review workflow reference"
}
if ($content -notmatch "(?m)^\s*if:\s+\$\{\{.+needs-ai-review.+\}\}\s*$") {
    Fail "ERROR: missing fork/dependabot safety gate in managed block"
}
if ($content -notmatch "(?m)^\s*provider:\s+") {
    Fail "ERROR: missing provider input in managed block"
}
if ($content -notmatch "(?m)^\s*model:\s+") {
    Fail "ERROR: missing model input in managed block"
}

if ($content -match "(?m)^\s*secrets:\s*inherit\s*$") {
    Write-Host "OK: workflow uses inherited secrets"
} elseif ($content -match "INTELLIGENCEX_AUTH_B64") {
    Write-Host "OK: workflow uses explicit secrets block"
} else {
    Fail "ERROR: workflow has neither secrets: inherit nor explicit INTELLIGENCEX secrets"
}

Write-Host "OK: managed workflow validation passed ($WorkflowPath)"
