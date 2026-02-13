# Cleanup helper for worktrees and common local artifacts.

[CmdletBinding(SupportsShouldProcess = $true)] param(
    [string[]] $RepoPaths,
    [switch] $IncludeKnownSiblingRepos,
    [switch] $FetchPrune,
    [switch] $CleanArtifacts,
    [switch] $RemoveOrphanedWorktreeDirs
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Header($text) { Write-Host "`n=== $text ===" -ForegroundColor Cyan }
function Write-Step($text)   { Write-Host "[+] $text" -ForegroundColor Yellow }
function Write-Ok($text)     { Write-Host "[OK] $text" -ForegroundColor Green }
function Write-Warn($text)   { Write-Host "[!] $text" -ForegroundColor DarkYellow }

function Invoke-Git {
    param(
        [Parameter(Mandatory)]
        [string] $RepoPath,
        [Parameter(Mandatory)]
        [string[]] $Args
    )

    & git -C $RepoPath @Args
    if ($LASTEXITCODE -ne 0) {
        throw "git command failed with exit code ${LASTEXITCODE} in ${RepoPath}: git $($Args -join ' ')"
    }
}

$script:RepoRoot = (Get-Item (Split-Path -Parent $MyInvocation.MyCommand.Path)).Parent.FullName
$workspaceRoot = Split-Path -Parent $script:RepoRoot

$candidateRepos = [System.Collections.Generic.List[string]]::new()
if ($RepoPaths -and $RepoPaths.Count -gt 0) {
    foreach ($path in $RepoPaths) {
        if (-not [string]::IsNullOrWhiteSpace($path)) {
            $candidateRepos.Add($path)
        }
    }
} else {
    $candidateRepos.Add($script:RepoRoot)
}

if ($IncludeKnownSiblingRepos) {
    foreach ($name in @('IntelligenceX', 'IntelligenceX.Chat', 'IntelligenceX.Tools', 'TestimoX', 'PSEventViewer', 'Mailozaurr', 'DbaClientX')) {
        $candidate = Join-Path $workspaceRoot $name
        if (Test-Path (Join-Path $candidate '.git')) {
            $candidateRepos.Add($candidate)
        }
    }
}

$targetRepos = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
foreach ($candidate in $candidateRepos) {
    if ([string]::IsNullOrWhiteSpace($candidate)) {
        continue
    }

    $full = [System.IO.Path]::GetFullPath($candidate)
    if (-not (Test-Path (Join-Path $full '.git'))) {
        Write-Warn "Skipping non-git path: $full"
        continue
    }
    $null = $targetRepos.Add($full)
}

if ($targetRepos.Count -eq 0) {
    throw 'No valid git repositories selected for cleanup.'
}

Write-Header 'Workspace Cleanup'
Write-Step "Target repositories: $($targetRepos.Count)"

foreach ($repoPath in $targetRepos) {
    Write-Header "Repository: $repoPath"
    Write-Step 'git worktree prune'
    Invoke-Git -RepoPath $repoPath -Args @('worktree', 'prune')

    if ($FetchPrune) {
        Write-Step 'git fetch --all --prune'
        Invoke-Git -RepoPath $repoPath -Args @('fetch', '--all', '--prune')
    }

    Write-Step 'Current worktrees'
    Invoke-Git -RepoPath $repoPath -Args @('worktree', 'list')

    if ($CleanArtifacts) {
        foreach ($relative in @('Artifacts', 'TestResults')) {
            $fullPath = Join-Path $repoPath $relative
            if (-not (Test-Path $fullPath)) {
                continue
            }

            if ($PSCmdlet.ShouldProcess($fullPath, 'Remove artifact directory')) {
                Write-Step "Removing: $fullPath"
                Remove-Item -Recurse -Force $fullPath
            }
        }
    }
}

if ($RemoveOrphanedWorktreeDirs) {
    Write-Header 'Orphaned Worktree Folder Scan'
    foreach ($folderName in @('_wt', '_worktrees')) {
        $worktreeRoot = Join-Path $workspaceRoot $folderName
        if (-not (Test-Path $worktreeRoot)) {
            continue
        }

        Write-Step "Scanning $worktreeRoot"
        $children = Get-ChildItem -Path $worktreeRoot -Directory -ErrorAction SilentlyContinue
        foreach ($child in $children) {
            $gitPointer = Join-Path $child.FullName '.git'
            if (Test-Path $gitPointer) {
                continue
            }

            if ($PSCmdlet.ShouldProcess($child.FullName, 'Remove orphaned worktree folder (missing .git pointer)')) {
                Write-Step "Removing orphaned folder: $($child.FullName)"
                Remove-Item -Recurse -Force $child.FullName
            }
        }
    }
}

Write-Ok 'Cleanup complete.'
