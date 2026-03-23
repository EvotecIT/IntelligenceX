[CmdletBinding()] param(
    [string] $BundleRoot = (Get-Location).Path,

    [ValidateSet('public','private','all')]
    [string] $PluginMode = 'public',

    [ValidateSet('Debug','Release')]
    [string] $Configuration = 'Release',

    [ValidateSet('host','app')]
    [string] $Frontend = 'app',

    [string] $Framework = 'net10.0-windows',
    [string] $AppFramework = 'net8.0-windows10.0.26100.0',
    [string] $Runtime = 'win-x64',
    [string] $PrimaryExecutable,
    [switch] $IncludeService,
    [switch] $IncludePrivateToolPacks,
    [string] $TestimoXRoot,
    [switch] $IncludeSymbols,
    [bool] $LeanBundle = $true,
    [switch] $IncludePortableHelpers,
    [switch] $IncludeBundleMetadata,
    [string] $BundleName
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Convert-PluginFoldersToArchives {
    param(
        [Parameter(Mandatory)]
        [string] $PluginsRoot
    )

    if (-not (Test-Path $PluginsRoot)) {
        return
    }

    $pluginDirs = Get-ChildItem -Path $PluginsRoot -Directory -ErrorAction SilentlyContinue |
        Sort-Object Name
    foreach ($pluginDir in $pluginDirs) {
        $archivePath = Join-Path $PluginsRoot ($pluginDir.Name + '.ix-plugin.zip')
        if (Test-Path $archivePath) {
            Remove-Item -Force $archivePath
        }

        [System.IO.Compression.ZipFile]::CreateFromDirectory($pluginDir.FullName, $archivePath)
        Remove-Item -Recurse -Force $pluginDir.FullName
    }
}

function Remove-BundleSymbols {
    param(
        [Parameter(Mandatory)]
        [string] $BundleRootPath
    )

    if ($IncludeSymbols) {
        return
    }

    Get-ChildItem -Path $BundleRootPath -Recurse -File -Filter '*.pdb' -ErrorAction SilentlyContinue |
        Remove-Item -Force -ErrorAction SilentlyContinue
}

function Remove-BundleDiagnostics {
    param(
        [Parameter(Mandatory)]
        [string] $BundleRootPath
    )

    if (-not $LeanBundle) {
        return
    }

    Get-ChildItem -Path $BundleRootPath -Recurse -File -Filter 'createdump.exe' -ErrorAction SilentlyContinue |
        Remove-Item -Force -ErrorAction SilentlyContinue
}

function New-PortableLauncherScripts {
    param(
        [Parameter(Mandatory)]
        [string] $BundleRootPath,
        [Parameter(Mandatory)]
        [string] $PrimaryExecutableName
    )

    $launcherPs1 = @'
param(
    [string[]] $AllowRoot,
    [string[]] $ExtraArgs
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$primaryExe = Join-Path $PSScriptRoot '__PRIMARY_EXE__'
if (-not (Test-Path $primaryExe)) {
    throw "Primary executable not found: $primaryExe"
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

& $primaryExe @args
exit $LASTEXITCODE
'@

    $launcherCmd = @'
@echo off
setlocal
set "SCRIPT_DIR=%~dp0"
set "PRIMARY_EXE=%SCRIPT_DIR%__PRIMARY_EXE__"
if not exist "%PRIMARY_EXE%" (
  echo Primary executable not found: %PRIMARY_EXE%
  exit /b 1
)
"%PRIMARY_EXE%" --allow-root "%SCRIPT_DIR%" %*
'@

    $launcherPs1 = $launcherPs1.Replace('__PRIMARY_EXE__', $PrimaryExecutableName)
    $launcherCmd = $launcherCmd.Replace('__PRIMARY_EXE__', $PrimaryExecutableName)

    Set-Content -Path (Join-Path $BundleRootPath 'run-chat.ps1') -Value $launcherPs1 -Encoding UTF8
    Set-Content -Path (Join-Path $BundleRootPath 'run-chat.cmd') -Value $launcherCmd -Encoding ASCII
}

function New-PortableReadme {
    param(
        [Parameter(Mandatory)]
        [string] $BundleRootPath,
        [Parameter(Mandatory)]
        [string] $FrontendValue,
        [Parameter(Mandatory)]
        [string] $PrimaryExecutableName,
        [Parameter(Mandatory)]
        [string] $RuntimeValue,
        [Parameter(Mandatory)]
        [string] $FrameworkValue,
        [Parameter(Mandatory)]
        [string] $PluginModeValue,
        [Parameter(Mandatory)]
        [bool] $ServiceIncluded
    )

    $serviceLine = if ($ServiceIncluded) {
        '- `service\` (required runtime sidecar payload)'
    } else {
        '- `service\` (optional advanced service payload, only when packaged with `-IncludeService`)'
    }

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
        ".\$PrimaryExecutableName --allow-root .",
        '```',
        '',
        '## Bundle layout',
        '',
        "- `$PrimaryExecutableName` (primary app)",
        '- `plugins\` (folder-based plugin packs)',
        $serviceLine,
        '',
        '## Build metadata',
        '',
        "- Frontend: $FrontendValue",
        "- Runtime: $RuntimeValue",
        "- Framework: $FrameworkValue",
        "- Plugin mode: $PluginModeValue"
    )

    Set-Content -Path (Join-Path $BundleRootPath 'README.md') -Value ($lines -join "`r`n") -Encoding UTF8
}

function Resolve-PrimaryExecutableName {
    param([Parameter(Mandatory)][string] $FrontendName)

    if ($FrontendName -eq 'app') {
        return 'IntelligenceX.Chat.App.exe'
    }

    return 'IntelligenceX.Chat.Host.exe'
}

$script:RepoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$bundleRootFull = [System.IO.Path]::GetFullPath($BundleRoot)
$pluginsOut = Join-Path $bundleRootFull 'plugins'
$frontendNormalized = $Frontend.ToLowerInvariant()

if ([string]::IsNullOrWhiteSpace($PrimaryExecutable)) {
    $PrimaryExecutable = Resolve-PrimaryExecutableName -FrontendName $frontendNormalized
}
if ([string]::IsNullOrWhiteSpace($BundleName)) {
    $BundleName = [System.IO.Path]::GetFileName($bundleRootFull)
}

New-Item -ItemType Directory -Path $bundleRootFull -Force | Out-Null
New-Item -ItemType Directory -Path $pluginsOut -Force | Out-Null

$exportScript = Join-Path $script:RepoRoot 'Build\Internal\Export-PluginFolders.ps1'
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

if ($LeanBundle) {
    Convert-PluginFoldersToArchives -PluginsRoot $pluginsOut
}

if ($IncludePortableHelpers) {
    New-PortableLauncherScripts -BundleRootPath $bundleRootFull -PrimaryExecutableName $PrimaryExecutable
    New-PortableReadme -BundleRootPath $bundleRootFull -FrontendValue $frontendNormalized -PrimaryExecutableName $PrimaryExecutable -RuntimeValue $Runtime -FrameworkValue $Framework -PluginModeValue $PluginMode -ServiceIncluded ([bool]$IncludeService)
}

Remove-BundleSymbols -BundleRootPath $bundleRootFull
Remove-BundleDiagnostics -BundleRootPath $bundleRootFull

if ($IncludeBundleMetadata) {
    $bundleMetadata = [ordered]@{
        schemaVersion = 1
        bundleName = $BundleName
        frontend = $frontendNormalized
        primaryExecutable = $PrimaryExecutable
        runtime = $Runtime
        framework = $Framework
        appFramework = $AppFramework
        configuration = $Configuration
        pluginMode = $PluginMode
        includeService = [bool]$IncludeService
        includePrivateToolPacks = [bool]$IncludePrivateToolPacks
        includeSymbols = [bool]$IncludeSymbols
        leanBundle = $LeanBundle
        includePortableHelpers = [bool]$IncludePortableHelpers
        createdUtc = (Get-Date).ToUniversalTime().ToString('o')
    }
    $bundleMetadata | ConvertTo-Json -Depth 5 | Set-Content -Path (Join-Path $bundleRootFull 'portable-bundle.json') -Encoding UTF8
}
