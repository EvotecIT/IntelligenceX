[CmdletBinding()] param(
    [string] $BundleRoot = (Get-Location).Path,

    [ValidateSet('public','private','all')]
    [string] $PluginMode = 'public',

    [ValidateSet('Debug','Release')]
    [string] $Configuration = 'Release',

    [ValidateSet('host','app')]
    [string] $Frontend = 'app',

    [string] $Framework = 'net10.0-windows',
    [string] $AppFramework = 'net10.0-windows10.0.26100.0',
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

Add-Type -AssemblyName System.IO.Compression.FileSystem

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

function New-ZipFromFolder {
    param(
        [Parameter(Mandatory)]
        [string] $FolderPath,
        [Parameter(Mandatory)]
        [string] $ZipPath
    )

    if (Test-Path -LiteralPath $ZipPath) {
        Remove-Item -LiteralPath $ZipPath -Force
    }

    [System.IO.Compression.ZipFile]::CreateFromDirectory($FolderPath, $ZipPath)
}

function Reset-Directory {
    param(
        [Parameter(Mandatory)]
        [string] $Path
    )

    if (Test-Path -LiteralPath $Path) {
        Remove-Item -LiteralPath $Path -Recurse -Force
    }

    New-Item -ItemType Directory -Path $Path -Force | Out-Null
}

$script:RepoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
. (Join-Path $script:RepoRoot 'Build\Internal\Build.ScriptSupport.ps1')
. (Join-Path $script:RepoRoot 'Build\Internal\Resolve-ReleaseDefaults.ps1')
. (Join-Path $script:RepoRoot 'Build\Internal\Resolve-PowerForgeCli.ps1')

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

$pluginExportRoot = $pluginsOut
$pluginScratchRoot = $null
$preArchivedPlugins = $false

if ($LeanBundle) {
    Reset-Directory -Path $pluginsOut
    $pluginScratchRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("ix-chat-plugins-{0}" -f ([System.Guid]::NewGuid().ToString('N')))
    $pluginExportRoot = Join-Path $pluginScratchRoot 'plugins'
    New-Item -ItemType Directory -Path $pluginExportRoot -Force | Out-Null
}

$exportScript = Join-Path $script:RepoRoot 'Build\Internal\Export-PluginFolders.ps1'
$exportParameters = @{
    Mode = $PluginMode
    Configuration = $Configuration
    Framework = $Framework
    OutDir = $pluginExportRoot
}
if ($IncludeSymbols) {
    $exportParameters.IncludeSymbols = $true
}
if (-not [string]::IsNullOrWhiteSpace($TestimoXRoot)) {
    $exportParameters.TestimoXRoot = $TestimoXRoot
}

Write-Header 'Export Plugin Folders'
try {
    Invoke-ScriptFile -ScriptPath $exportScript -Parameters $exportParameters -FailureContext 'Plugin export failed.' -FailureHint 'Check POWERFORGE_ROOT if you are testing against a local PSPublishModule worktree.'

    if ($LeanBundle -and (Test-Path -LiteralPath $pluginExportRoot)) {
        Write-Header 'Archive Plugin Folders'
        $pluginDirectories = Get-ChildItem -LiteralPath $pluginExportRoot -Directory -ErrorAction SilentlyContinue | Sort-Object Name
        foreach ($pluginDirectory in @($pluginDirectories)) {
            $pluginZipPath = Join-Path $pluginsOut ("{0}.ix-plugin.zip" -f $pluginDirectory.Name)
            Write-Step ("Archive plugin -> {0}" -f $pluginZipPath)
            New-ZipFromFolder -FolderPath $pluginDirectory.FullName -ZipPath $pluginZipPath
        }
        $preArchivedPlugins = $true
    }

    if ($IncludePortableHelpers) {
        New-PortableLauncherScripts -BundleRootPath $bundleRootFull -PrimaryExecutableName $PrimaryExecutable
        New-PortableReadme -BundleRootPath $bundleRootFull -FrontendValue $frontendNormalized -PrimaryExecutableName $PrimaryExecutable -RuntimeValue $Runtime -FrameworkValue $Framework -PluginModeValue $PluginMode -ServiceIncluded ([bool]$IncludeService)
    }

    if ($LeanBundle -or (-not $IncludeSymbols) -or $IncludeBundleMetadata) {
        $powerForgeCli = Resolve-PowerForgeCliInvocation -RepoRoot $script:RepoRoot
        $postProcessArgs = @($powerForgeCli.Prefix) + @(
            'dotnet',
            'bundle-postprocess',
            '--config',
            (Join-Path $script:RepoRoot 'Build\powerforge.dotnetpublish.json'),
            '--bundle',
            'IntelligenceX.Chat.Portable',
            '--bundle-root',
            $bundleRootFull,
            '--target',
            ($(if ($frontendNormalized -eq 'app') { 'IntelligenceX.Chat.App' } else { 'IntelligenceX.Chat.Host' })),
            '--rid',
            $Runtime,
            '--framework',
            $Framework,
            '--style',
            'PortableCompat',
            '--configuration',
            $Configuration,
            '--token',
            "bundleName=$BundleName",
            '--token',
            "frontend=$frontendNormalized",
            '--token',
            "primaryExecutable=$PrimaryExecutable",
            '--token',
            "appFramework=$AppFramework",
            '--token',
            "pluginMode=$PluginMode",
            '--token',
            "includeService=$([bool]$IncludeService)",
            '--token',
            "includePrivateToolPacks=$([bool]$IncludePrivateToolPacks)",
            '--token',
            "includeSymbols=$([bool]$IncludeSymbols)",
            '--token',
            "leanBundle=$LeanBundle",
            '--token',
            "includePortableHelpers=$([bool]$IncludePortableHelpers)"
        )

        if ((-not $LeanBundle) -or $preArchivedPlugins) {
            $postProcessArgs += '--skip-archive'
        }
        if (-not $IncludeBundleMetadata) {
            $postProcessArgs += '--skip-metadata'
        }
        if (-not $IncludeSymbols) {
            $postProcessArgs += @('--delete-pattern', '**/*.pdb')
        }
        if ($LeanBundle) {
            $postProcessArgs += @('--delete-pattern', '**/createdump.exe')
        }

        Write-Header 'Bundle Post-Process'
        Write-Step (Format-CommandLine -Command $powerForgeCli.Command -Arguments $postProcessArgs)
        & $powerForgeCli.Command @postProcessArgs
        if ($LASTEXITCODE -ne 0) {
            throw "PowerForge bundle post-process failed with exit code $LASTEXITCODE."
        }
    }
} finally {
    if (-not [string]::IsNullOrWhiteSpace($pluginScratchRoot) -and (Test-Path -LiteralPath $pluginScratchRoot)) {
        Remove-Item -LiteralPath $pluginScratchRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
}
