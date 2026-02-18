param()

$ErrorActionPreference = "Stop"

function Fail([string] $Message) {
    Write-Error $Message
    exit 1
}

try {
    $repoRoot = (git rev-parse --show-toplevel 2>$null).Trim()
} catch {
    $repoRoot = ""
}

if ([string]::IsNullOrWhiteSpace($repoRoot)) {
    Fail "ERROR: not inside a git repository"
}

Set-Location -LiteralPath $repoRoot

foreach ($tool in @("git", "dotnet", "rg", "gh")) {
    if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) {
        Fail "ERROR: required tool not found: $tool"
    }
}

$branch = (git branch --show-current).Trim()
if (-not $branch.StartsWith("codex/")) {
    Fail "ERROR: branch must start with codex/ (current: $branch)"
}

$allowDirty = ("" + $env:ALLOW_DIRTY).Trim()
if ($allowDirty -ne "1") {
    $dirty = git status --porcelain
    if (-not [string]::IsNullOrWhiteSpace(($dirty | Out-String))) {
        Fail "ERROR: working tree is not clean (set ALLOW_DIRTY=1 to override)"
    }
}

Write-Host "OK: preflight passed"
Write-Host "repo=$repoRoot"
Write-Host "branch=$branch"
