# Publish IntelligenceX CLI for a specific runtime

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
    [switch] $Zip = $true,

    # Signing (Windows only)
    [switch] $DoNotSign,
    [string] $SignToolPath = 'signtool.exe',
    [string] $SignThumbprint,
    [string] $SignSubjectName,
    [string] $SignTimestampUrl = 'https://timestamp.digicert.com',
    [string] $SignDescription = 'IntelligenceX CLI',
    [string] $SignUrl,
    [string] $SignCsp,
    [string] $SignKeyContainer
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Header($t) { Write-Host "`n=== $t ===" -ForegroundColor Cyan }
function Write-Ok($t)     { Write-Host "[OK] $t" -ForegroundColor Green }
function Write-Step($t)   { Write-Host "[+] $t" -ForegroundColor Yellow }

function Resolve-SignToolPath {
    param([string] $Path)
    if (-not [string]::IsNullOrWhiteSpace($Path)) {
        $cmd = Get-Command $Path -ErrorAction SilentlyContinue
        if ($cmd) { return $cmd.Source }
    }
    $kitsRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    if (Test-Path $kitsRoot) {
        $versions = Get-ChildItem -Path $kitsRoot -Directory -ErrorAction SilentlyContinue |
            Sort-Object Name -Descending
        foreach ($ver in $versions) {
            foreach ($arch in @('x64','x86')) {
                $candidate = Join-Path $ver.FullName (Join-Path $arch 'signtool.exe')
                if (Test-Path $candidate) { return $candidate }
            }
        }
    }
    return $null
}

$repo = (Get-Item (Split-Path -Parent $MyInvocation.MyCommand.Path)).Parent.FullName
$proj = Join-Path $repo 'IntelligenceX.Cli/IntelligenceX.Cli.csproj'

if (-not (Test-Path $proj)) { throw "Project not found: $proj" }

if ([string]::IsNullOrWhiteSpace($OutDir)) {
    $OutDir = Join-Path $repo ("Artifacts/IntelligenceX.Cli/{0}" -f $Runtime)
}

if ($ClearOut -and (Test-Path $OutDir)) {
    Write-Step "Clearing $OutDir"
    Get-ChildItem -Path $OutDir -Recurse -Force -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
}

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

Write-Header 'Publish IntelligenceX CLI'
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

if (-not $DoNotSign -and $Runtime -like 'win-*') {
    $resolvedSignTool = Resolve-SignToolPath -Path $SignToolPath
    if (-not $resolvedSignTool) {
        throw "SignTool not found. Install the Windows SDK or pass -SignToolPath."
    }
    $SignToolPath = $resolvedSignTool
    $exe = Get-ChildItem -Path $OutDir -Filter '*.exe' -File | Where-Object { $_.Name -like 'intelligencex*.exe' } | Select-Object -First 1
    if ($exe) {
        Write-Step "Signing -> $($exe.FullName)"
        $signArgs = @('sign', '/fd', 'sha256', '/v')
        if ($SignThumbprint) { $signArgs += @('/sha1', $SignThumbprint) }
        if ($SignSubjectName) { $signArgs += @('/n', $SignSubjectName) }
        if ($SignTimestampUrl) { $signArgs += @('/tr', $SignTimestampUrl, '/td', 'sha256') }
        if ($SignDescription) { $signArgs += @('/d', $SignDescription) }
        if ($SignUrl) { $signArgs += @('/du', $SignUrl) }
        if ($SignCsp) { $signArgs += @('/csp', $SignCsp) }
        if ($SignKeyContainer) { $signArgs += @('/kc', $SignKeyContainer) }
        & $SignToolPath @signArgs $exe.FullName | Out-Null
    }
}

if ($Zip) {
    $zipName = "IntelligenceX.Cli-{0}-{1}-{2}.zip" -f $Framework, $Runtime, (Get-Date -Format 'yyyyMMdd-HHmm')
    $zipPath = Join-Path (Split-Path -Parent $OutDir) $zipName
    if (Test-Path $zipPath) { Remove-Item -Force $zipPath }
    Write-Step ("Create zip -> {0}" -f $zipPath)
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::CreateFromDirectory($OutDir, $zipPath)
}

Write-Ok ("Published -> {0}" -f $OutDir)
