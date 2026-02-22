# Publish chat artifacts with a one-app default (WinUI App) and optional service payload.

[CmdletBinding()] param(
    [ValidateSet('win-x64','win-arm64')]
    [string] $Runtime = 'win-x64',

    [ValidateSet('Debug','Release')]
    [string] $Configuration = 'Release',

    [ValidateSet('host','app')]
    [string] $Frontend = 'app',

    [string] $Framework = 'net10.0-windows',
    [string] $AppFramework = 'net8.0-windows10.0.26100.0',
    [switch] $SelfContained = $true,
    [switch] $SingleFile = $true,
    [switch] $Trim,

    [string] $OutDir,
    [switch] $ClearOut,
    [switch] $IncludeService,
    [switch] $Zip = $true,
    [switch] $IncludePrivateToolPacks,
    [string] $TestimoXRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Header($text) { Write-Host "`n=== $text ===" -ForegroundColor Cyan }
function Write-Step($text)   { Write-Host "[+] $text" -ForegroundColor Yellow }
function Write-Ok($text)     { Write-Host "[OK] $text" -ForegroundColor Green }
function Write-Warn($text)   { Write-Host "[!] $text" -ForegroundColor DarkYellow }

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

function Publish-Project {
    param(
        [Parameter(Mandatory)]
        [string] $ProjectPath,
        [Parameter(Mandatory)]
        [string] $OutputPath,
        [string] $FrameworkOverride,
        [switch] $DisableSingleFile,
        [string[]] $AdditionalArgs
    )

    New-Item -ItemType Directory -Path $OutputPath -Force | Out-Null
    $effectiveFramework = if ([string]::IsNullOrWhiteSpace($FrameworkOverride)) { $Framework } else { $FrameworkOverride }
    $args = @(
        'publish',
        $ProjectPath,
        '-c',
        $Configuration,
        '-f',
        $effectiveFramework,
        '-r',
        $Runtime,
        '-o',
        $OutputPath
    )
    if ($SelfContained) {
        $args += '--self-contained'
    } else {
        $args += '--no-self-contained'
    }
    if ($SingleFile -and -not $DisableSingleFile) {
        $args += '/p:PublishSingleFile=true'
    } elseif ($SingleFile -and $DisableSingleFile) {
        Write-Warn "PublishSingleFile was requested but disabled for project: $ProjectPath"
    }
    if ($Trim) {
        $args += '/p:PublishTrimmed=true'
    }
    if ($IncludePrivateToolPacks) {
        $args += '/p:IncludePrivateToolPacks=true'
        if (-not [string]::IsNullOrWhiteSpace($TestimoXRoot)) {
            $resolved = [System.IO.Path]::GetFullPath($TestimoXRoot)
            if (-not $resolved.EndsWith([System.IO.Path]::DirectorySeparatorChar)) {
                $resolved += [System.IO.Path]::DirectorySeparatorChar
            }
            $args += "/p:TestimoXRoot=$resolved"
        }
    }
    if ($AdditionalArgs -and $AdditionalArgs.Count -gt 0) {
        $args += $AdditionalArgs
    }
    Invoke-DotNet -Args $args -WorkingDirectory $script:RepoRoot
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
$hostProject = Join-Path $script:RepoRoot 'IntelligenceX.Chat\IntelligenceX.Chat.Host\IntelligenceX.Chat.Host.csproj'
$appProject = Join-Path $script:RepoRoot 'IntelligenceX.Chat\IntelligenceX.Chat.App\IntelligenceX.Chat.App.csproj'
$serviceProject = Join-Path $script:RepoRoot 'IntelligenceX.Chat\IntelligenceX.Chat.Service\IntelligenceX.Chat.Service.csproj'

if ([string]::IsNullOrWhiteSpace($OutDir)) {
    $OutDir = Join-Path $script:RepoRoot ("Artifacts\IntelligenceX.Chat\{0}" -f $Runtime)
}

if ($ClearOut -and (Test-Path $OutDir)) {
    Write-Step "Clearing output: $OutDir"
    Remove-Item -Recurse -Force $OutDir
}

$frontendNormalized = $Frontend.ToLowerInvariant()
$frontendOut = if ($frontendNormalized -eq 'app') { Join-Path $OutDir 'App' } else { Join-Path $OutDir 'Host' }
$serviceOut = Join-Path $OutDir 'Service'
$appServiceOut = Join-Path $frontendOut 'service'
New-Item -ItemType Directory -Path $OutDir -Force | Out-Null

Write-Header 'Publish Chat'
Write-Step "Frontend: $frontendNormalized"
Write-Step "Runtime: $Runtime"
Write-Step "Framework: $Framework"
Write-Step "App framework: $AppFramework"
Write-Step "Output root: $OutDir"

if ($frontendNormalized -eq 'app') {
    Write-Header 'Publish WinUI App (default user-facing app)'
    Publish-Project -ProjectPath $appProject -OutputPath $frontendOut -FrameworkOverride $AppFramework -DisableSingleFile -AdditionalArgs @('/p:SkipChatServiceSidecarBuild=true', '/p:WarningsNotAsErrors=NU1510')
    Write-Header 'Publish Service Sidecar (required for WinUI app runtime)'
    Publish-Project -ProjectPath $serviceProject -OutputPath $appServiceOut -FrameworkOverride $Framework -DisableSingleFile -AdditionalArgs @('/p:WarningsNotAsErrors=NU1510')
    if ($IncludeService) {
        Write-Warn 'IncludeService was requested, but app publish already includes service sidecar under App\\service.'
    }
} else {
    Write-Header 'Publish Host (default user-facing app)'
    Publish-Project -ProjectPath $hostProject -OutputPath $frontendOut

    if ($IncludeService) {
        Write-Header 'Publish Service (optional advanced mode)'
        Publish-Project -ProjectPath $serviceProject -OutputPath $serviceOut
    }
}

if ($Zip) {
    Write-Header 'Create Zip Artifacts'
    $timestamp = Get-Date -Format 'yyyyMMdd-HHmm'
    $frontendLabel = if ($frontendNormalized -eq 'app') { 'IntelligenceX.Chat.App' } else { 'IntelligenceX.Chat.Host' }
    $frontendZip = Join-Path $OutDir ("{0}-{1}-{2}-{3}.zip" -f $frontendLabel, $Framework, $Runtime, $timestamp)
    Write-Step "Zip frontend -> $frontendZip"
    New-ZipFromFolder -FolderPath $frontendOut -ZipPath $frontendZip

    if ($frontendNormalized -ne 'app' -and $IncludeService) {
        $serviceZip = Join-Path $OutDir ("IntelligenceX.Chat.Service-{0}-{1}-{2}.zip" -f $Framework, $Runtime, $timestamp)
        Write-Step "Zip service -> $serviceZip"
        New-ZipFromFolder -FolderPath $serviceOut -ZipPath $serviceZip
    }
}

Write-Ok "Published chat artifacts to $OutDir"
