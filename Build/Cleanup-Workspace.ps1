# Cleanup helper for worktrees and common local artifacts.

[CmdletBinding(SupportsShouldProcess = $true)] param(
    [string[]] $RepoPaths,
    [switch] $IncludeKnownSiblingRepos,
    [switch] $FetchPrune,
    [switch] $CleanArtifacts,
    [switch] $RemoveOrphanedWorktreeDirs,
    [switch] $RemoveMergedWorktrees,
    [string] $MergedIntoRef = 'origin/master',
    [string] $MergedBranchPrefix = 'codex/',
    [string[]] $SkipMergedBranchPrefixes = @(),
    [switch] $DeleteMergedLocalBranches
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
        [string[]] $Args,
        [switch] $AllowFailure,
        [switch] $Quiet
    )

    $output = & git -C $RepoPath @Args 2>&1
    $exitCode = $LASTEXITCODE
    if ($output -and -not $Quiet) {
        foreach ($line in $output) {
            Write-Host $line
        }
    }

    if (-not $AllowFailure -and $exitCode -ne 0) {
        $joined = ($output -join [Environment]::NewLine).Trim()
        if ([string]::IsNullOrWhiteSpace($joined)) {
            $joined = '(no output)'
        }
        throw "git command failed with exit code ${exitCode} in ${RepoPath}: git $($Args -join ' ')`n${joined}"
    }

    return [pscustomobject]@{
        ExitCode = $exitCode
        Output   = @($output)
    }
}

function Remove-DirectoryRobust {
    param(
        [Parameter(Mandatory)]
        [string] $Path
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        return
    }

    try {
        Remove-Item -LiteralPath $Path -Recurse -Force -ErrorAction Stop
        return
    } catch {
        if (-not $IsWindows) {
            throw
        }
    }

    $prefixed = if ($Path.StartsWith('\\?\', [System.StringComparison]::Ordinal)) { $Path } else { "\\?\$Path" }
    & cmd /c "rd /s /q `"$prefixed`""
    if ($LASTEXITCODE -ne 0 -and (Test-Path -LiteralPath $Path)) {
        throw "Failed to remove directory using long-path fallback: $Path"
    }
}

function Test-PathIsSameOrChild {
    param(
        [Parameter(Mandatory)]
        [string] $BasePath,
        [Parameter(Mandatory)]
        [string] $CandidatePath
    )

    $base = [System.IO.Path]::GetFullPath($BasePath).TrimEnd('\', '/')
    $candidate = [System.IO.Path]::GetFullPath($CandidatePath).TrimEnd('\', '/')
    if ([string]::Equals($base, $candidate, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $true
    }

    $prefixA = $base + [System.IO.Path]::DirectorySeparatorChar
    $prefixB = $base + [System.IO.Path]::AltDirectorySeparatorChar
    return $candidate.StartsWith($prefixA, [System.StringComparison]::OrdinalIgnoreCase) -or
        $candidate.StartsWith($prefixB, [System.StringComparison]::OrdinalIgnoreCase)
}

function Get-RepoTopLevel {
    param(
        [Parameter(Mandatory)]
        [string] $RepoPath
    )

    $result = Invoke-Git -RepoPath $RepoPath -Args @('rev-parse', '--show-toplevel') -Quiet
    if ($result.Output.Count -eq 0) {
        throw "Unable to resolve repository top-level path for: $RepoPath"
    }

    return [System.IO.Path]::GetFullPath($result.Output[-1].ToString().Trim())
}

function Get-BranchShortName {
    param([string] $BranchRef)

    if ([string]::IsNullOrWhiteSpace($BranchRef)) {
        return $null
    }

    if ($BranchRef.StartsWith('refs/heads/', [System.StringComparison]::OrdinalIgnoreCase)) {
        return $BranchRef.Substring('refs/heads/'.Length)
    }

    return $BranchRef
}

function Get-WorktreeEntries {
    param(
        [Parameter(Mandatory)]
        [string] $RepoPath
    )

    $result = Invoke-Git -RepoPath $RepoPath -Args @('worktree', 'list', '--porcelain') -Quiet
    $entries = [System.Collections.Generic.List[object]]::new()

    $path = $null
    $head = $null
    $branch = $null

    foreach ($lineObj in $result.Output) {
        $line = $lineObj.ToString()
        if ([string]::IsNullOrWhiteSpace($line)) {
            if (-not [string]::IsNullOrWhiteSpace($path)) {
                $entries.Add([pscustomobject]@{
                    Path      = [System.IO.Path]::GetFullPath($path)
                    Head      = $head
                    BranchRef = $branch
                })
            }
            $path = $null
            $head = $null
            $branch = $null
            continue
        }

        if ($line.StartsWith('worktree ', [System.StringComparison]::Ordinal)) {
            $path = $line.Substring('worktree '.Length).Trim()
            continue
        }

        if ($line.StartsWith('HEAD ', [System.StringComparison]::Ordinal)) {
            $head = $line.Substring('HEAD '.Length).Trim()
            continue
        }

        if ($line.StartsWith('branch ', [System.StringComparison]::Ordinal)) {
            $branch = $line.Substring('branch '.Length).Trim()
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($path)) {
        $entries.Add([pscustomobject]@{
            Path      = [System.IO.Path]::GetFullPath($path)
            Head      = $head
            BranchRef = $branch
        })
    }

    return $entries
}

function Test-RevisionExists {
    param(
        [Parameter(Mandatory)]
        [string] $RepoPath,
        [Parameter(Mandatory)]
        [string] $Revision
    )

    $result = Invoke-Git -RepoPath $RepoPath -Args @('rev-parse', '--verify', '--quiet', "$Revision`^{commit}") -AllowFailure -Quiet
    return $result.ExitCode -eq 0
}

function Test-LocalBranchMergedInto {
    param(
        [Parameter(Mandatory)]
        [string] $RepoPath,
        [Parameter(Mandatory)]
        [string] $BranchName,
        [Parameter(Mandatory)]
        [string] $IntoRef
    )

    if (-not (Test-RevisionExists -RepoPath $RepoPath -Revision "refs/heads/$BranchName")) {
        return $false
    }

    if (-not (Test-RevisionExists -RepoPath $RepoPath -Revision $IntoRef)) {
        throw "Merge target '$IntoRef' does not exist in $RepoPath. Run with -FetchPrune or choose a different -MergedIntoRef."
    }

    $result = Invoke-Git -RepoPath $RepoPath -Args @('merge-base', '--is-ancestor', "refs/heads/$BranchName", $IntoRef) -AllowFailure -Quiet
    if ($result.ExitCode -eq 0) {
        return $true
    }
    if ($result.ExitCode -eq 1) {
        return $false
    }

    $joined = ($result.Output -join [Environment]::NewLine).Trim()
    if ([string]::IsNullOrWhiteSpace($joined)) {
        $joined = '(no output)'
    }
    throw "Unable to determine merge status for branch '$BranchName': $joined"
}

function Test-WorktreeGitPointerHealthy {
    param(
        [Parameter(Mandatory)]
        [string] $WorktreePath
    )

    $gitPointer = Join-Path $WorktreePath '.git'
    if (-not (Test-Path -LiteralPath $gitPointer)) {
        return $false
    }

    $item = Get-Item -LiteralPath $gitPointer -Force -ErrorAction SilentlyContinue
    if ($null -eq $item) {
        return $false
    }

    if ($item.PSIsContainer) {
        return $true
    }

    try {
        $line = (Get-Content -LiteralPath $gitPointer -TotalCount 1 -ErrorAction Stop)
    } catch {
        return $false
    }

    $text = ($line ?? '').ToString().Trim()
    if ($text -notmatch '^\s*gitdir:\s*(.+)\s*$') {
        return $false
    }

    $gitDirRaw = $Matches[1].Trim()
    if ([string]::IsNullOrWhiteSpace($gitDirRaw)) {
        return $false
    }

    $gitDirPath = if ([System.IO.Path]::IsPathRooted($gitDirRaw)) {
        $gitDirRaw
    } else {
        Join-Path $WorktreePath $gitDirRaw
    }

    try {
        $resolved = [System.IO.Path]::GetFullPath($gitDirPath)
    } catch {
        return $false
    }

    return Test-Path -LiteralPath $resolved
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

$summary = [ordered]@{
    MergedWorktreesRemoved     = 0
    MergedBranchesDeleted      = 0
    ArtifactDirectoriesRemoved = 0
    OrphanedDirectoriesRemoved = 0
}

Write-Header 'Workspace Cleanup'
Write-Step "Target repositories: $($targetRepos.Count)"

foreach ($repoPath in $targetRepos) {
    Write-Header "Repository: $repoPath"
    Write-Step 'git worktree prune'
    $null = Invoke-Git -RepoPath $repoPath -Args @('worktree', 'prune')

    if ($FetchPrune) {
        Write-Step 'git fetch --all --prune'
        $null = Invoke-Git -RepoPath $repoPath -Args @('fetch', '--all', '--prune')
    }

    Write-Step 'Current worktrees'
    $null = Invoke-Git -RepoPath $repoPath -Args @('worktree', 'list')

    if ($RemoveMergedWorktrees) {
        Write-Step "Scanning for merged worktrees (branch prefix: '$MergedBranchPrefix', target: $MergedIntoRef)"
        if (-not (Test-RevisionExists -RepoPath $repoPath -Revision $MergedIntoRef)) {
            Write-Warn "Merge target '$MergedIntoRef' does not exist in $repoPath. Skipping merged-worktree cleanup for this repo."
        } else {
            $repoTopLevel = Get-RepoTopLevel -RepoPath $repoPath
            $worktrees = Get-WorktreeEntries -RepoPath $repoPath
            $currentWorkingDir = [System.IO.Path]::GetFullPath((Get-Location).ProviderPath)

            foreach ($entry in $worktrees) {
                if ([string]::Equals($entry.Path, $repoTopLevel, [System.StringComparison]::OrdinalIgnoreCase)) {
                    continue
                }

                if (Test-PathIsSameOrChild -BasePath $entry.Path -CandidatePath $currentWorkingDir) {
                    Write-Warn "Skipping active worktree (current directory is inside it): $($entry.Path)"
                    continue
                }

                $branchName = Get-BranchShortName -BranchRef $entry.BranchRef
                if ([string]::IsNullOrWhiteSpace($branchName)) {
                    continue
                }

                if (-not [string]::IsNullOrWhiteSpace($MergedBranchPrefix) -and
                    -not $branchName.StartsWith($MergedBranchPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
                    continue
                }

                $skipBranch = $false
                foreach ($skipPrefix in $SkipMergedBranchPrefixes) {
                    if ([string]::IsNullOrWhiteSpace($skipPrefix)) {
                        continue
                    }

                    if ($branchName.StartsWith($skipPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
                        $skipBranch = $true
                        break
                    }
                }
                if ($skipBranch) {
                    continue
                }

                $isMerged = Test-LocalBranchMergedInto -RepoPath $repoPath -BranchName $branchName -IntoRef $MergedIntoRef
                if (-not $isMerged) {
                    continue
                }

                $worktreeLabel = "$($entry.Path) ($branchName)"
                if (-not $PSCmdlet.ShouldProcess($worktreeLabel, "Remove merged worktree (merged into $MergedIntoRef)")) {
                    continue
                }

                Write-Step "Removing merged worktree: $worktreeLabel"
                $removeResult = Invoke-Git -RepoPath $repoPath -Args @('worktree', 'remove', '--force', $entry.Path) -AllowFailure
                if ($removeResult.ExitCode -ne 0) {
                    $failureText = ($removeResult.Output -join [Environment]::NewLine)
                    if ($IsWindows -and $failureText.Contains('Filename too long', [System.StringComparison]::OrdinalIgnoreCase)) {
                        Write-Warn "Long path encountered, using fallback directory removal: $($entry.Path)"
                        Remove-DirectoryRobust -Path $entry.Path
                        $null = Invoke-Git -RepoPath $repoPath -Args @('worktree', 'prune')
                    } else {
                        if ([string]::IsNullOrWhiteSpace($failureText)) {
                            $failureText = '(no output)'
                        }
                        throw "Unable to remove merged worktree '$worktreeLabel': $failureText"
                    }
                }

                $summary.MergedWorktreesRemoved++

                if ($DeleteMergedLocalBranches -and $PSCmdlet.ShouldProcess($branchName, 'Delete merged local branch')) {
                    $deleteResult = Invoke-Git -RepoPath $repoPath -Args @('branch', '-D', $branchName) -AllowFailure
                    if ($deleteResult.ExitCode -ne 0) {
                        $deleteText = ($deleteResult.Output -join [Environment]::NewLine).Trim()
                        if ([string]::IsNullOrWhiteSpace($deleteText)) {
                            $deleteText = '(no output)'
                        }
                        Write-Warn "Failed to delete local branch '$branchName': $deleteText"
                    } else {
                        $summary.MergedBranchesDeleted++
                    }
                }
            }
        }
    }

    if ($CleanArtifacts) {
        foreach ($relative in @('Artifacts', 'TestResults')) {
            $fullPath = Join-Path $repoPath $relative
            if (-not (Test-Path $fullPath)) {
                continue
            }

            if ($PSCmdlet.ShouldProcess($fullPath, 'Remove artifact directory')) {
                Write-Step "Removing: $fullPath"
                Remove-DirectoryRobust -Path $fullPath
                $summary.ArtifactDirectoriesRemoved++
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
            if (Test-WorktreeGitPointerHealthy -WorktreePath $child.FullName) {
                continue
            }

            if ($PSCmdlet.ShouldProcess($child.FullName, 'Remove orphaned worktree folder (missing or broken .git pointer)')) {
                Write-Step "Removing orphaned folder: $($child.FullName)"
                Remove-DirectoryRobust -Path $child.FullName
                $summary.OrphanedDirectoriesRemoved++
            }
        }
    }
}

Write-Header 'Summary'
Write-Step "Merged worktrees removed: $($summary.MergedWorktreesRemoved)"
Write-Step "Merged local branches deleted: $($summary.MergedBranchesDeleted)"
Write-Step "Artifact directories removed: $($summary.ArtifactDirectoriesRemoved)"
Write-Step "Orphaned worktree directories removed: $($summary.OrphanedDirectoriesRemoved)"
Write-Ok 'Cleanup complete.'
