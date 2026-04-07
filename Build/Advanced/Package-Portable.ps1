<#
.SYNOPSIS
Creates a portable Chat bundle with optional smoke validation.

.DESCRIPTION
Publishes the selected frontend, exports plugin folders, validates bundled host
artifacts, and can smoke-test the finished bundle with explicit scenario files
or a named preset release suite.

.PARAMETER SmokeScenarioPreset
Named smoke suite to run after the bundle is built. Supported values:
  - runtime-only
  - runtime-and-toolful

.EXAMPLE
pwsh ./Build/Advanced/Package-Portable.ps1 `
  -Frontend host `
  -Runtime win-x64 `
  -PluginMode all `
  -IncludePrivateToolPacks `
  -TestimoXRoot C:\Support\GitHub\TestimoX `
  -IncludePortableHelpers `
  -SmokeScenarioPreset runtime-and-toolful `
  -SmokeScenarioOutput ./artifacts/chat-live-portable-bundle-preset
#>

[CmdletBinding()] param(
    [ValidateSet('win-x64','win-arm64')]
    [string] $Runtime = 'win-x64',

    [ValidateSet('Debug','Release')]
    [string] $Configuration = 'Release',

    [ValidateSet('host','app')]
    [string] $Frontend = 'app',

    [string] $Framework = 'net10.0-windows',
    [string] $AppFramework = 'net10.0-windows10.0.26100.0',
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
    [bool] $LeanBundle = $true,
    [switch] $IncludePortableHelpers,
    [switch] $IncludeBundleMetadata,
    [string[]] $SmokeScenarioFile,
    [ValidateSet('runtime-only','runtime-and-toolful')]
    [string] $SmokeScenarioPreset,
    [string] $SmokeScenarioOutput,
    [string[]] $SmokeAllowRoot,
    [int] $SmokeTurnTimeoutSeconds = 120,
    [int] $SmokeToolTimeoutSeconds = 60,

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

$script:RepoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
. (Join-Path $script:RepoRoot 'Build\Internal\Resolve-ReleaseDefaults.ps1')
. (Join-Path $script:RepoRoot 'Build\Internal\Resolve-ChatSmokeScenarioPreset.ps1')

if ([string]::IsNullOrWhiteSpace($OutDir)) {
    $OutDir = Join-Path $script:RepoRoot ("Artifacts\Portable\{0}" -f $Runtime)
}
if ([string]::IsNullOrWhiteSpace($BundleName)) {
    $BundleName = "IntelligenceX.Chat-Portable-$Runtime"
}

$bundleRoot = Join-Path $OutDir $BundleName
$serviceOut = Join-Path $bundleRoot 'service'
$pluginsOut = Join-Path $bundleRoot 'plugins'
$frontendNormalized = $Frontend.ToLowerInvariant()
$primaryExecutable = Resolve-PrimaryExecutableName -FrontendName $frontendNormalized
$serviceIncluded = [bool]$IncludeService

if ($frontendNormalized -eq 'host' -and $SingleFile) {
    Write-Warn 'Single-file packaging is not supported for Chat.Host bundles; disabling PublishSingleFile and keeping loose tool-pack/runtime assemblies.'
    $SingleFile = $false
}

if ($ClearOut -and (Test-Path $bundleRoot)) {
    Write-Step "Clearing bundle directory: $bundleRoot"
    Remove-Item -Recurse -Force $bundleRoot
}

New-Item -ItemType Directory -Path $bundleRoot -Force | Out-Null
New-Item -ItemType Directory -Path $pluginsOut -Force | Out-Null

$hostProject = Join-Path $script:RepoRoot 'IntelligenceX.Chat\IntelligenceX.Chat.Host\IntelligenceX.Chat.Host.csproj'
$appProject = Join-Path $script:RepoRoot 'IntelligenceX.Chat\IntelligenceX.Chat.App\IntelligenceX.Chat.App.csproj'
$serviceProject = Join-Path $script:RepoRoot 'IntelligenceX.Chat\IntelligenceX.Chat.Service\IntelligenceX.Chat.Service.csproj'
$exportScript = Join-Path $script:RepoRoot 'Build\Internal\Export-PluginFolders.ps1'
$completeBundleScript = Join-Path $script:RepoRoot 'Build\Internal\Complete-PortableBundle.ps1'
$smokeBundleScript = Join-Path $script:RepoRoot 'Build\Chat\Test-PortableChatBundle.ps1'
$publishHostScript = Join-Path $script:RepoRoot 'Build\Chat\Publish-ChatHost.ps1'

Write-Header 'Package Portable Chat Bundle'
Write-Step "Frontend: $frontendNormalized"
Write-Step "Runtime: $Runtime"
Write-Step "Framework: $Framework"
Write-Step "App framework: $AppFramework"
Write-Step "Plugin mode: $PluginMode"
Write-Step "Bundle root: $bundleRoot"
Write-Step "Include symbols: $([bool]$IncludeSymbols)"
Write-Step "Lean bundle: $LeanBundle"
Write-Step "Include portable helpers: $([bool]$IncludePortableHelpers)"
Write-Step "Include bundle metadata: $([bool]$IncludeBundleMetadata)"

if ($frontendNormalized -eq 'app') {
    Write-Header 'Publish WinUI App (primary app)'
    Publish-Project -ProjectPath $appProject -OutputPath $bundleRoot -FrameworkOverride $AppFramework -DisableSingleFile -AdditionalArgs @('/p:SkipChatServiceSidecarBuild=true', '/p:WarningsNotAsErrors=NU1510')
    if (-not $IncludeService) {
        Write-Warn 'IncludeService was not specified. Publishing service payload anyway because WinUI app requires local sidecar for default runtime mode.'
    }
    $serviceIncluded = $true
    Write-Header 'Publish Service (required for WinUI app runtime)'
    Publish-Project -ProjectPath $serviceProject -OutputPath $serviceOut -FrameworkOverride $Framework -DisableSingleFile -AdditionalArgs @('/p:WarningsNotAsErrors=NU1510')
} else {
    Write-Header 'Publish Host (primary app)'
    $publishHostArgs = @(
        '-NoLogo',
        '-NoProfile',
        '-File',
        $publishHostScript,
        '-Runtime',
        $Runtime,
        '-Configuration',
        $Configuration,
        '-Framework',
        $Framework,
        '-OutDir',
        $bundleRoot
    )
    if ($SelfContained) {
        $publishHostArgs += '-SelfContained'
    }
    if ($NoBuild) {
        $publishHostArgs += '-NoBuild'
    }
    if ($IncludePrivateToolPacks) {
        $publishHostArgs += '-IncludePrivateToolPacks'
    }
    if (-not [string]::IsNullOrWhiteSpace($TestimoXRoot)) {
        $publishHostArgs += @('-TestimoXRoot', $TestimoXRoot)
    }

    & pwsh @publishHostArgs
    if ($LASTEXITCODE -ne 0) {
        throw "Host publish helper failed with exit code $LASTEXITCODE."
    }

    if ($IncludeService) {
        Write-Header 'Publish Service (optional advanced mode)'
        Publish-Project -ProjectPath $serviceProject -OutputPath $serviceOut
        $serviceIncluded = $true
    }
}

if (-not (Test-Path (Join-Path $bundleRoot $primaryExecutable))) {
    throw "Primary executable missing from portable bundle: $(Join-Path $bundleRoot $primaryExecutable)"
}

Write-Header 'Finalize Portable Bundle'
$completeArgs = @(
    '-NoLogo',
    '-NoProfile',
    '-File',
    $completeBundleScript,
    '-BundleRoot',
    $bundleRoot,
    '-PluginMode',
    $PluginMode,
    '-Configuration',
    $Configuration,
    '-Frontend',
    $frontendNormalized,
    '-Framework',
    $Framework,
    '-AppFramework',
    $AppFramework,
    '-Runtime',
    $Runtime,
    '-PrimaryExecutable',
    $primaryExecutable,
    '-BundleName',
    $BundleName
)
if ($serviceIncluded) {
    $completeArgs += '-IncludeService'
}
if ($IncludePrivateToolPacks) {
    $completeArgs += '-IncludePrivateToolPacks'
}
if ($IncludeSymbols) {
    $completeArgs += '-IncludeSymbols'
}
if ($IncludePortableHelpers) {
    $completeArgs += '-IncludePortableHelpers'
}
if ($IncludeBundleMetadata) {
    $completeArgs += '-IncludeBundleMetadata'
}
if (-not [string]::IsNullOrWhiteSpace($TestimoXRoot)) {
    $completeArgs += @('-TestimoXRoot', $TestimoXRoot)
}
& pwsh @completeArgs
if ($LASTEXITCODE -ne 0) {
    throw "Portable bundle finishing failed with exit code $LASTEXITCODE."
}

if ($Zip) {
    Write-Header 'Create Portable Zip'
    $timestamp = Get-Date -Format 'yyyyMMdd-HHmm'
    $zipPath = Join-Path $OutDir ("{0}-{1}.zip" -f $BundleName, $timestamp)
    Write-Step "Zip -> $zipPath"
    New-ZipFromFolder -FolderPath $bundleRoot -ZipPath $zipPath
}

$resolvedSmokeScenarioFiles = @(
    @(Resolve-ChatSmokeScenarioPreset -RepoRoot $script:RepoRoot -PresetName $SmokeScenarioPreset) +
    @($SmokeScenarioFile | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
)

if ($resolvedSmokeScenarioFiles.Count -gt 0) {
    Write-Header 'Smoke Test Portable Bundle'
    $smokeArgs = @{
        BundleRoot = $bundleRoot
        ScenarioFile = $resolvedSmokeScenarioFiles
        TurnTimeoutSeconds = $SmokeTurnTimeoutSeconds
        ToolTimeoutSeconds = $SmokeToolTimeoutSeconds
    }
    if (-not [string]::IsNullOrWhiteSpace($SmokeScenarioOutput)) {
        $smokeArgs.ScenarioOutput = $SmokeScenarioOutput
    }
    if ($SmokeAllowRoot -and $SmokeAllowRoot.Count -gt 0) {
        $smokeArgs.AllowRoot = @($SmokeAllowRoot | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    }

    & $smokeBundleScript @smokeArgs
}

Write-Ok "Portable bundle ready: $bundleRoot"



