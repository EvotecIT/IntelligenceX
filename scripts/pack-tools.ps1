# Pack IntelligenceX.Tools NuGet packages.

[CmdletBinding()] param(
    [ValidateSet('Portable','All')]
    [string] $Profile = 'Portable',

    [ValidateSet('Debug','Release')]
    [string] $Configuration = 'Release',

    [string] $OutDir,

    [switch] $ClearOut
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Header($t) { Write-Host "`n=== $t ===" -ForegroundColor Cyan }
function Write-Ok($t)     { Write-Host "[OK] $t" -ForegroundColor Green }
function Write-Step($t)   { Write-Host "[+] $t" -ForegroundColor Yellow }

$repo = (Get-Item (Split-Path -Parent $PSScriptRoot)).FullName

if (-not $OutDir) {
    $OutDir = Join-Path $repo 'Artifacts/IntelligenceX.Tools/packages'
}

if ($ClearOut -and (Test-Path $OutDir)) {
    Write-Step "Clearing $OutDir"
    Get-ChildItem -Path $OutDir -Recurse -Force -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
}

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$portableProjects = @(
    'IntelligenceX.Tools/IntelligenceX.Tools.Common/IntelligenceX.Tools.Common.csproj',
    'IntelligenceX.Tools/IntelligenceX.Tools.Email/IntelligenceX.Tools.Email.csproj',
    'IntelligenceX.Tools/IntelligenceX.Tools.ReviewerSetup/IntelligenceX.Tools.ReviewerSetup.csproj'
)

$engineDependentProjects = @(
    'IntelligenceX.Tools/IntelligenceX.Tools.FileSystem/IntelligenceX.Tools.FileSystem.csproj',
    'IntelligenceX.Tools/IntelligenceX.Tools.System/IntelligenceX.Tools.System.csproj',
    'IntelligenceX.Tools/IntelligenceX.Tools.PowerShell/IntelligenceX.Tools.PowerShell.csproj',
    'IntelligenceX.Tools/IntelligenceX.Tools.EventLog/IntelligenceX.Tools.EventLog.csproj',
    'IntelligenceX.Tools/IntelligenceX.Tools.ActiveDirectory/IntelligenceX.Tools.ActiveDirectory.csproj',
    'IntelligenceX.Tools/IntelligenceX.Tools.TestimoX/IntelligenceX.Tools.TestimoX.csproj'
)

$projects = @()
$projects += $portableProjects
if ($Profile -eq 'All') {
    $projects += $engineDependentProjects
}

Write-Header "Pack IntelligenceX.Tools ($Profile profile)"

foreach ($relativeProject in $projects) {
    $project = Join-Path $repo $relativeProject
    if (-not (Test-Path $project)) {
        throw "Project not found: $project"
    }

    Write-Step ("dotnet pack {0}" -f $relativeProject)
    & dotnet pack $project -c $Configuration -o $OutDir
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet pack failed ($LASTEXITCODE) for $relativeProject"
    }
}

Write-Ok ("Packages -> {0}" -f $OutDir)

if ($Profile -eq 'Portable') {
    Write-Host "Portable profile excludes engine-dependent packs (ComputerX/TestimoX/ADPlayground/EventViewerX)." -ForegroundColor Yellow
}

