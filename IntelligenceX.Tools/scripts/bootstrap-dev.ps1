param(
    [string] $Root = "",
    [switch] $IncludeOptional
)

$ErrorActionPreference = "Stop"

function Resolve-RepoRoot {
    param([string] $Path)
    return (Resolve-Path -LiteralPath $Path).Path
}

function Ensure-Git {
    if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
        throw "git is required but was not found on PATH."
    }
}

function Ensure-Repo {
    param(
        [Parameter(Mandatory = $true)][string] $Path,
        [Parameter(Mandatory = $true)][string] $Url
    )

    if (Test-Path -LiteralPath $Path) {
        Write-Host "OK: $Path"
        return
    }

    Write-Host "Cloning $Url -> $Path"
    git clone $Url $Path | Out-Host
}

Ensure-Git

# Default to the parent directory of this repo (so sibling checkouts land next to it).
$repoRoot = Resolve-RepoRoot (Join-Path $PSScriptRoot "..")

if ([string]::IsNullOrWhiteSpace($Root)) {
    $Root = Split-Path -Parent $repoRoot
} else {
    $Root = Resolve-RepoRoot $Root
}

Write-Host "Bootstrap root: $Root"

Ensure-Repo -Path (Join-Path $Root "IntelligenceX") -Url "https://github.com/EvotecIT/IntelligenceX.git"
Ensure-Repo -Path (Join-Path $Root "TestimoX-master") -Url "https://github.com/EvotecIT/TestimoX.git"
Ensure-Repo -Path (Join-Path $Root "PSEventViewer") -Url "https://github.com/EvotecIT/PSEventViewer.git"

if ($IncludeOptional) {
    Ensure-Repo -Path (Join-Path $Root "Mailozaurr") -Url "https://github.com/EvotecIT/Mailozaurr.git"
}

Write-Host "Done."

