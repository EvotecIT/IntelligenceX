# Publish IntelligenceX.Reviewer for a specific runtime

[CmdletBinding()] param(
    [ValidateSet('win-x64','win-arm64','linux-x64','linux-arm64','osx-x64','osx-arm64')]
    [string] $Runtime = 'win-x64',

    [ValidateSet('Debug','Release')]
    [string] $Configuration = 'Release',

    [string] $Framework = 'net8.0',

    [switch] $SelfContained = $true,
    [switch] $SingleFile = $true,
    [switch] $Trim,

    # Destination folder for the published output
    [string] $OutDir,

    # If set, clears destination folder before publishing
    [switch] $ClearOut,

    # Also create a zip alongside the folder
    [switch] $Zip = $true
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Header($t) { Write-Host "`n=== $t ===" -ForegroundColor Cyan }
function Write-Ok($t)     { Write-Host "[OK] $t" -ForegroundColor Green }
function Write-Step($t)   { Write-Host "[+] $t" -ForegroundColor Yellow }

$repo = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$proj = Join-Path $repo 'IntelligenceX.Reviewer/IntelligenceX.Reviewer.csproj'

if (-not (Test-Path $proj)) { throw "Project not found: $proj" }

if (-not $OutDir) {
    $OutDir = Join-Path $repo ("Artifacts/IntelligenceX.Reviewer/{0}" -f $Runtime)
}

if ($ClearOut -and (Test-Path $OutDir)) {
    Write-Step "Clearing $OutDir"
    Get-ChildItem -Path $OutDir -Recurse -Force -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
}

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

Write-Header 'Publish IntelligenceX.Reviewer'
Write-Step ("dotnet publish {0} ({1}/{2}/{3})" -f $Runtime, $Configuration, $Framework, $([bool]$SelfContained))

$publishArgs = @(
    'publish',
    $proj,
    '-c', $Configuration,
    '-f', $Framework,
    '-r', $Runtime,
    '-o', $OutDir
)

if ($SelfContained) { $publishArgs += '--self-contained' } else { $publishArgs += '--no-self-contained' }
if ($SingleFile) { $publishArgs += '/p:PublishSingleFile=true' }
if ($Trim) { $publishArgs += '/p:PublishTrimmed=true' }

& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE)" }

if ($Zip) {
    $zipName = "IntelligenceX.Reviewer-{0}-{1}-{2}.zip" -f $Framework, $Runtime, (Get-Date -Format 'yyyyMMdd-HHmm')
    $zipPath = Join-Path (Split-Path -Parent $OutDir) $zipName
    if (Test-Path $zipPath) { Remove-Item -Force $zipPath }
    Write-Step ("Create zip -> {0}" -f $zipPath)
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::CreateFromDirectory($OutDir, $zipPath)
}

Write-Ok ("Published -> {0}" -f $OutDir)
