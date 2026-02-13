# Publish IntelligenceX.Chat host/service for a specific runtime.

[CmdletBinding()] param(
    [ValidateSet('win-x64','win-arm64','linux-x64','linux-arm64','osx-x64','osx-arm64')]
    [string] $Runtime = 'win-x64',

    [ValidateSet('Host','Service','Both')]
    [string] $Mode = 'Host',

    [ValidateSet('Debug','Release')]
    [string] $Configuration = 'Release',

    [string] $Framework,

    [switch] $SelfContained = $true,
    [switch] $SingleFile = $true,
    [switch] $Trim,

    # Destination folder for the published output.
    [string] $OutDir,

    # If set, clears destination folder before publishing.
    [switch] $ClearOut,

    # Copy launcher scripts to output root.
    [switch] $IncludeLaunchScripts = $true,

    # Also create a zip alongside the folder.
    [switch] $Zip = $true
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Header($t) { Write-Host "`n=== $t ===" -ForegroundColor Cyan }
function Write-Ok($t)     { Write-Host "[OK] $t" -ForegroundColor Green }
function Write-Step($t)   { Write-Host "[+] $t" -ForegroundColor Yellow }

function Resolve-HostFramework([string] $runtime, [string] $frameworkOverride) {
    if (-not [string]::IsNullOrWhiteSpace($frameworkOverride)) {
        return $frameworkOverride
    }

    if ($runtime -like 'win-*') {
        return 'net10.0-windows'
    }

    return 'net10.0'
}

function Resolve-ServiceFramework([string] $frameworkOverride) {
    if (-not [string]::IsNullOrWhiteSpace($frameworkOverride)) {
        return $frameworkOverride
    }

    return 'net10.0-windows'
}

function Publish-Project {
    param(
        [Parameter(Mandatory = $true)][string] $ProjectPath,
        [Parameter(Mandatory = $true)][string] $TargetFramework,
        [Parameter(Mandatory = $true)][string] $TargetRuntime,
        [Parameter(Mandatory = $true)][string] $TargetOutDir,
        [Parameter(Mandatory = $true)][string] $TargetConfiguration
    )

    Write-Step ("dotnet publish {0} ({1}/{2}/{3})" -f $ProjectPath, $TargetConfiguration, $TargetFramework, $TargetRuntime)

    $publishArgs = @(
        'publish',
        $ProjectPath,
        '-c', $TargetConfiguration,
        '-f', $TargetFramework,
        '-r', $TargetRuntime,
        '-o', $TargetOutDir
    )

    if ($SelfContained) { $publishArgs += '--self-contained' } else { $publishArgs += '--no-self-contained' }
    if ($SingleFile) { $publishArgs += '/p:PublishSingleFile=true' }
    if ($Trim) { $publishArgs += '/p:PublishTrimmed=true' }

    & dotnet @publishArgs
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed ($LASTEXITCODE) for $ProjectPath" }
}

$repo = (Get-Item (Split-Path -Parent $PSScriptRoot)).FullName
$hostProject = Join-Path $repo 'IntelligenceX.Chat/IntelligenceX.Chat.Host/IntelligenceX.Chat.Host.csproj'
$serviceProject = Join-Path $repo 'IntelligenceX.Chat/IntelligenceX.Chat.Service/IntelligenceX.Chat.Service.csproj'
$chatRoot = Join-Path $repo 'IntelligenceX.Chat'

if (-not (Test-Path $hostProject)) { throw "Project not found: $hostProject" }
if (-not (Test-Path $serviceProject)) { throw "Project not found: $serviceProject" }

if (($Mode -eq 'Service' -or $Mode -eq 'Both') -and $Runtime -notlike 'win-*') {
    throw "Service mode requires a Windows runtime because IntelligenceX.Chat.Service currently targets net10.0-windows."
}

if (-not $OutDir) {
    $OutDir = Join-Path $repo ("Artifacts/IntelligenceX.Chat/{0}/{1}" -f $Runtime, $Mode.ToLowerInvariant())
}

if ($ClearOut -and (Test-Path $OutDir)) {
    Write-Step "Clearing $OutDir"
    Get-ChildItem -Path $OutDir -Recurse -Force -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
}

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

Write-Header 'Publish IntelligenceX.Chat'

switch ($Mode) {
    'Host' {
        $hostFramework = Resolve-HostFramework -runtime $Runtime -frameworkOverride $Framework
        Publish-Project -ProjectPath $hostProject -TargetFramework $hostFramework -TargetRuntime $Runtime -TargetOutDir $OutDir -TargetConfiguration $Configuration
    }
    'Service' {
        $serviceFramework = Resolve-ServiceFramework -frameworkOverride $Framework
        Publish-Project -ProjectPath $serviceProject -TargetFramework $serviceFramework -TargetRuntime $Runtime -TargetOutDir $OutDir -TargetConfiguration $Configuration
    }
    'Both' {
        $hostFramework = Resolve-HostFramework -runtime $Runtime -frameworkOverride $Framework
        $serviceFramework = Resolve-ServiceFramework -frameworkOverride $Framework

        $hostOut = Join-Path $OutDir 'host'
        $serviceOut = Join-Path $OutDir 'service'

        New-Item -ItemType Directory -Force -Path $hostOut | Out-Null
        New-Item -ItemType Directory -Force -Path $serviceOut | Out-Null

        Publish-Project -ProjectPath $hostProject -TargetFramework $hostFramework -TargetRuntime $Runtime -TargetOutDir $hostOut -TargetConfiguration $Configuration
        Publish-Project -ProjectPath $serviceProject -TargetFramework $serviceFramework -TargetRuntime $Runtime -TargetOutDir $serviceOut -TargetConfiguration $Configuration
    }
}

if ($IncludeLaunchScripts) {
    foreach ($scriptName in @('run-host.ps1', 'run-service.ps1')) {
        $src = Join-Path $chatRoot $scriptName
        if (Test-Path $src) {
            Copy-Item -Path $src -Destination (Join-Path $OutDir $scriptName) -Force
        }
    }
}

if ($Zip) {
    $zipName = "IntelligenceX.Chat-{0}-{1}-{2}.zip" -f $Mode.ToLowerInvariant(), $Runtime, (Get-Date -Format 'yyyyMMdd-HHmm')
    $zipPath = Join-Path (Split-Path -Parent $OutDir) $zipName
    if (Test-Path $zipPath) { Remove-Item -Force $zipPath }
    Write-Step ("Create zip -> {0}" -f $zipPath)
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::CreateFromDirectory($OutDir, $zipPath)
}

Write-Ok ("Published -> {0}" -f $OutDir)

