# Create a portable chat bundle (single user-facing app + plugin folders + optional service).

[CmdletBinding()] param(
    [ValidateSet('win-x64','win-arm64')]
    [string] $Runtime = 'win-x64',

    [ValidateSet('Debug','Release')]
    [string] $Configuration = 'Release',

    [string] $Framework = 'net10.0-windows',
    [switch] $SelfContained = $true,
    [switch] $SingleFile = $true,
    [switch] $Trim,
    [switch] $NoBuild,

    [ValidateSet('public','private','all')]
    [string] $PluginMode = 'public',

    [switch] $IncludeService,
    [switch] $IncludePrivateToolPacks,
    [string] $TestimoXRoot,
    [switch] $IncludeSymbols,

    [string] $OutDir,
    [string] $BundleName,
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

function Publish-Project {
    param(
        [Parameter(Mandatory)]
        [string] $ProjectPath,
        [Parameter(Mandatory)]
        [string] $OutputPath
    )

    New-Item -ItemType Directory -Path $OutputPath -Force | Out-Null
    $args = @(
        'publish',
        $ProjectPath,
        '-c',
        $Configuration,
        '-f',
        $Framework,
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
    if ($SingleFile) {
        $args += '/p:PublishSingleFile=true'
    }
    if ($Trim) {
        $args += '/p:PublishTrimmed=true'
    }
    if ($NoBuild) {
        # Ensure RID-specific assets exist before publish --no-build.
        Invoke-DotNet -Args @('restore', $ProjectPath, '-r', $Runtime) -WorkingDirectory $script:RepoRoot
        $args += '--no-build'
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

function Remove-BundleSymbols {
    param(
        [Parameter(Mandatory)]
        [string] $BundleRoot
    )

    if ($IncludeSymbols) {
        return
    }

    Get-ChildItem -Path $BundleRoot -Recurse -File -Filter '*.pdb' -ErrorAction SilentlyContinue |
        Remove-Item -Force -ErrorAction SilentlyContinue
}

function New-PortableLauncherScripts {
    param(
        [Parameter(Mandatory)]
        [string] $BundleRoot
    )

        $launcherPs1 = @'
param(
    [string[]] $AllowRoot,
    [string[]] $ExtraArgs
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$hostExe = Join-Path $PSScriptRoot 'IntelligenceX.Chat.Host.exe'
if (-not (Test-Path $hostExe)) {
    throw "Host executable not found: $hostExe"
}

if (-not $AllowRoot -or $AllowRoot.Count -eq 0) {
    $AllowRoot = @($PSScriptRoot)
}

$args = @()
foreach ($root in $AllowRoot) {
    if (-not [string]::IsNullOrWhiteSpace($root)) {
        $args += @('--allow-root', $root)
    }
}
if ($ExtraArgs -and $ExtraArgs.Count -gt 0) {
    $args += $ExtraArgs
}

& $hostExe @args
exit $LASTEXITCODE
'@

    $launcherCmd = @'
@echo off
setlocal
set "SCRIPT_DIR=%~dp0"
set "HOST_EXE=%SCRIPT_DIR%IntelligenceX.Chat.Host.exe"
if not exist "%HOST_EXE%" (
  echo Host executable not found: %HOST_EXE%
  exit /b 1
)
"%HOST_EXE%" --allow-root "%SCRIPT_DIR%" %*
'@

    Set-Content -Path (Join-Path $BundleRoot 'run-chat.ps1') -Value $launcherPs1 -Encoding UTF8
    Set-Content -Path (Join-Path $BundleRoot 'run-chat.cmd') -Value $launcherCmd -Encoding ASCII
}

function New-PortableReadme {
    param(
        [Parameter(Mandatory)]
        [string] $BundleRoot,
        [Parameter(Mandatory)]
        [string] $RuntimeValue,
        [Parameter(Mandatory)]
        [string] $FrameworkValue,
        [Parameter(Mandatory)]
        [string] $PluginModeValue
    )

    $lines = @(
        '# IntelligenceX Chat Portable Bundle',
        '',
        'This bundle is ready to run without installing separate host/service apps.',
        '',
        '## Start',
        '',
        'PowerShell:',
        '```powershell',
        '.\run-chat.ps1',
        '```',
        '',
        'CMD:',
        '```cmd',
        '.\run-chat.cmd',
        '```',
        '',
        'Direct executable:',
        '```powershell',
        '.\IntelligenceX.Chat.Host.exe --allow-root .',
        '```',
        '',
        '## Bundle layout',
        '',
        '- `IntelligenceX.Chat.Host.exe` (primary app)',
        '- `plugins\` (folder-based plugin packs)',
        '- `service\` (optional advanced service payload, only when packaged with `-IncludeService`)',
        '',
        '## Build metadata',
        '',
        "- Runtime: $RuntimeValue",
        "- Framework: $FrameworkValue",
        "- Plugin mode: $PluginModeValue"
    )

    Set-Content -Path (Join-Path $BundleRoot 'README.md') -Value ($lines -join "`r`n") -Encoding UTF8
}

$script:RepoRoot = (Get-Item (Split-Path -Parent $MyInvocation.MyCommand.Path)).Parent.FullName

if ([string]::IsNullOrWhiteSpace($OutDir)) {
    $OutDir = Join-Path $script:RepoRoot ("Artifacts\Portable\{0}" -f $Runtime)
}
if ([string]::IsNullOrWhiteSpace($BundleName)) {
    $BundleName = "IntelligenceX.Chat-Portable-$Runtime"
}

$bundleRoot = Join-Path $OutDir $BundleName
$serviceOut = Join-Path $bundleRoot 'service'
$pluginsOut = Join-Path $bundleRoot 'plugins'

if ($ClearOut -and (Test-Path $bundleRoot)) {
    Write-Step "Clearing bundle directory: $bundleRoot"
    Remove-Item -Recurse -Force $bundleRoot
}

New-Item -ItemType Directory -Path $bundleRoot -Force | Out-Null
New-Item -ItemType Directory -Path $pluginsOut -Force | Out-Null

$hostProject = Join-Path $script:RepoRoot 'IntelligenceX.Chat\IntelligenceX.Chat.Host\IntelligenceX.Chat.Host.csproj'
$serviceProject = Join-Path $script:RepoRoot 'IntelligenceX.Chat\IntelligenceX.Chat.Service\IntelligenceX.Chat.Service.csproj'
$exportScript = Join-Path $script:RepoRoot 'Build\Export-PluginFolders.ps1'

Write-Header 'Package Portable Chat Bundle'
Write-Step "Runtime: $Runtime"
Write-Step "Framework: $Framework"
Write-Step "Plugin mode: $PluginMode"
Write-Step "Bundle root: $bundleRoot"
Write-Step "Include symbols: $([bool]$IncludeSymbols)"

Write-Header 'Publish Host (primary app)'
Publish-Project -ProjectPath $hostProject -OutputPath $bundleRoot

if ($IncludeService) {
    Write-Header 'Publish Service (optional advanced mode)'
    Publish-Project -ProjectPath $serviceProject -OutputPath $serviceOut
}

Write-Header 'Export Plugin Folders'
$exportArgs = @(
    '-NoLogo',
    '-NoProfile',
    '-File',
    $exportScript,
    '-Mode',
    $PluginMode,
    '-Configuration',
    $Configuration,
    '-Framework',
    $Framework,
    '-OutDir',
    $pluginsOut
)
if ($IncludeSymbols) {
    $exportArgs += '-IncludeSymbols'
}
if (-not [string]::IsNullOrWhiteSpace($TestimoXRoot)) {
    $exportArgs += @('-TestimoXRoot', $TestimoXRoot)
}
& pwsh @exportArgs
if ($LASTEXITCODE -ne 0) {
    throw "Plugin export failed with exit code $LASTEXITCODE."
}

Write-Header 'Generate Portable Launchers'
New-PortableLauncherScripts -BundleRoot $bundleRoot
New-PortableReadme -BundleRoot $bundleRoot -RuntimeValue $Runtime -FrameworkValue $Framework -PluginModeValue $PluginMode
Remove-BundleSymbols -BundleRoot $bundleRoot

$bundleMetadata = [ordered]@{
    schemaVersion = 1
    bundleName = $BundleName
    runtime = $Runtime
    framework = $Framework
    configuration = $Configuration
    pluginMode = $PluginMode
    includeService = [bool]$IncludeService
    includePrivateToolPacks = [bool]$IncludePrivateToolPacks
    includeSymbols = [bool]$IncludeSymbols
    createdUtc = (Get-Date).ToUniversalTime().ToString('o')
}
$bundleMetadata | ConvertTo-Json -Depth 5 | Set-Content -Path (Join-Path $bundleRoot 'portable-bundle.json') -Encoding UTF8

if ($Zip) {
    Write-Header 'Create Portable Zip'
    $timestamp = Get-Date -Format 'yyyyMMdd-HHmm'
    $zipPath = Join-Path $OutDir ("{0}-{1}.zip" -f $BundleName, $timestamp)
    Write-Step "Zip -> $zipPath"
    New-ZipFromFolder -FolderPath $bundleRoot -ZipPath $zipPath
}

Write-Ok "Portable bundle ready: $bundleRoot"



