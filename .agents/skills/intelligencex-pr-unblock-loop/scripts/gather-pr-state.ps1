param(
    [Parameter(Mandatory = $true)]
    [string] $PrNumber
)

$ErrorActionPreference = "Stop"

$repo = "EvotecIT/IntelligenceX"
$repoRoot = (git rev-parse --show-toplevel).Trim()
$artifactDir = Join-Path $repoRoot ("artifacts/pr-" + $PrNumber)
New-Item -ItemType Directory -Force -Path $artifactDir | Out-Null

gh pr view $PrNumber --repo $repo --json number,title,headRefName,baseRefName,state,mergeable,mergeStateStatus,url | Set-Content -Path (Join-Path $artifactDir "pr.json") -Encoding UTF8

try {
    gh pr checks $PrNumber --repo $repo | Set-Content -Path (Join-Path $artifactDir "checks.txt") -Encoding UTF8
} catch {
    # Keep snapshot flow resilient, matching the shell script behavior.
}

gh pr view $PrNumber --repo $repo --comments --json comments | Set-Content -Path (Join-Path $artifactDir "comments.json") -Encoding UTF8

Write-Host "Saved PR snapshot to: $artifactDir"
