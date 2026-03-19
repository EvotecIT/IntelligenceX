# Publish the IntelligenceX tray app as a standalone Windows desktop artifact.

[CmdletBinding()] param(
    [ValidateSet('win-x64','win-arm64')]
    [string] $Runtime = 'win-x64',

    [ValidateSet('Debug','Release')]
    [string] $Configuration = 'Release',

    [string] $Framework = 'net10.0-windows',
    [switch] $SelfContained = $true,
    [switch] $SingleFile,
    [switch] $Trim,
    [string] $OutDir,
    [switch] $ClearOut,
    [switch] $Zip = $true
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Header($text) { Write-Host "`n=== $text ===" -ForegroundColor Cyan }
function Write-Step($text)   { Write-Host "[+] $text" -ForegroundColor Yellow }
function Write-Ok($text)     { Write-Host "[OK] $text" -ForegroundColor Green }

function Invoke-DotNet {
    param(
        [Parameter(Mandatory)]
        [string[]] $Args,
        [string] $WorkingDirectory
    )

    if ([string]::IsNullOrWhiteSpace($WorkingDirectory)) {
        $WorkingDirectory = $script:RepoRoot
    }

    Push-Location $WorkingDirectory
    try {
        & dotnet @Args
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet command failed with exit code ${LASTEXITCODE}: dotnet $($Args -join ' ')"
        }
    } finally {
        Pop-Location
    }
}

function New-ZipFromFolder {
    param(
        [Parameter(Mandatory)]
        [string] $FolderPath,
        [Parameter(Mandatory)]
        [string] $ZipPath
    )

    if (Test-Path $ZipPath) {
        Remove-Item -Force $ZipPath
    }

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::CreateFromDirectory($FolderPath, $ZipPath)
}

$script:RepoRoot = (Get-Item (Split-Path -Parent $MyInvocation.MyCommand.Path)).Parent.FullName
$trayProject = Join-Path $script:RepoRoot 'IntelligenceX.Tray\IntelligenceX.Tray.csproj'

if ([string]::IsNullOrWhiteSpace($OutDir)) {
    $OutDir = Join-Path $script:RepoRoot ("Artifacts\IntelligenceX.Tray\{0}" -f $Runtime)
}

if ($ClearOut -and (Test-Path $OutDir)) {
    Write-Step "Clearing output: $OutDir"
    Remove-Item -Recurse -Force $OutDir
}

New-Item -ItemType Directory -Path $OutDir -Force | Out-Null

$publishArgs = @(
    'publish',
    $trayProject,
    '-c',
    $Configuration,
    '-f',
    $Framework,
    '-r',
    $Runtime,
    '-o',
    $OutDir
)

if ($SelfContained) {
    $publishArgs += '--self-contained'
} else {
    $publishArgs += '--no-self-contained'
}

if ($SingleFile) {
    $publishArgs += '/p:PublishSingleFile=true'
}

if ($Trim) {
    $publishArgs += '/p:PublishTrimmed=true'
}

Write-Header 'Publish Tray App'
Write-Step "Runtime: $Runtime"
Write-Step "Framework: $Framework"
Write-Step "Output root: $OutDir"
Invoke-DotNet -Args $publishArgs -WorkingDirectory $script:RepoRoot

if ($Zip) {
    $timestamp = Get-Date -Format 'yyyyMMdd-HHmm'
    $zipRoot = Split-Path -Parent $OutDir
    $zipPath = Join-Path $zipRoot ("IntelligenceX.Tray-{0}-{1}-{2}.zip" -f $Framework, $Runtime, $timestamp)
    Write-Step "Zip tray artifact -> $zipPath"
    New-ZipFromFolder -FolderPath $OutDir -ZipPath $zipPath
}

Write-Ok "Published tray artifacts to $OutDir"
