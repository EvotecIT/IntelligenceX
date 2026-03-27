<#
.SYNOPSIS
Runs smoke scenarios against a packaged Chat.Host bundle.

.DESCRIPTION
Validates a portable Chat bundle by running one or more scenario files directly
against the packaged host executable. You can provide explicit scenario paths
or use a named preset for the common release suites.

.PARAMETER ScenarioPreset
Named smoke suite to run instead of spelling out scenario paths manually.
Supported values:
  - runtime-only
  - runtime-and-toolful

.EXAMPLE
pwsh ./Build/Chat/Test-PortableChatBundle.ps1 `
  -BundleRoot ./Artifacts/Portable/win-x64/IntelligenceX.Chat-Portable-win-x64 `
  -ScenarioPreset runtime-and-toolful `
  -AllowRoot C:\Support\GitHub
#>
[CmdletBinding()] param(
    [Parameter(Mandatory)]
    [string] $BundleRoot,

    [string[]] $ScenarioFile,
    [ValidateSet('runtime-only','runtime-and-toolful')]
    [string] $ScenarioPreset,

    [string] $ScenarioOutput,
    [string[]] $AllowRoot,
    [int] $TurnTimeoutSeconds = 120,
    [int] $ToolTimeoutSeconds = 60,
    [string[]] $ExtraArgs
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Header($text) { Write-Host "`n=== $text ===" -ForegroundColor Cyan }
function Write-Step($text)   { Write-Host "[+] $text" -ForegroundColor Yellow }
function Write-Ok($text)     { Write-Host "[OK] $text" -ForegroundColor Green }

$bundleRootFull = [System.IO.Path]::GetFullPath($BundleRoot)
$script:RepoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
. (Join-Path $script:RepoRoot 'Build\Internal\Resolve-ChatSmokeScenarioPreset.ps1')
$ScenarioFile = @(
    @(Resolve-ChatSmokeScenarioPreset -RepoRoot $script:RepoRoot -PresetName $ScenarioPreset) +
    @($ScenarioFile | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
)

if (-not (Test-Path $bundleRootFull)) {
    throw "Bundle root not found: $bundleRootFull"
}
if (-not $ScenarioFile -or $ScenarioFile.Count -eq 0) {
    throw 'At least one scenario file must be provided.'
}

$hostExecutable = Join-Path $bundleRootFull 'IntelligenceX.Chat.Host.exe'
if (-not (Test-Path $hostExecutable)) {
    throw "Portable host executable not found: $hostExecutable"
}

if (-not $AllowRoot -or $AllowRoot.Count -eq 0) {
    $AllowRoot = @($bundleRootFull)
}

if ([string]::IsNullOrWhiteSpace($ScenarioOutput)) {
    $ScenarioOutput = Join-Path $bundleRootFull 'artifacts\portable-smoke'
}

for ($i = 0; $i -lt $ScenarioFile.Count; $i++) {
    $scenarioPath = $ScenarioFile[$i]
    $scenarioFileFull = [System.IO.Path]::GetFullPath($scenarioPath)
    if (-not (Test-Path $scenarioFileFull)) {
        throw "Scenario file not found: $scenarioFileFull"
    }

    $scenarioOutputPath = $ScenarioOutput
    if ($ScenarioFile.Count -gt 1) {
        $scenarioName = [System.IO.Path]::GetFileNameWithoutExtension($scenarioFileFull)
        $scenarioOutputPath = Join-Path $ScenarioOutput $scenarioName
    }

    $args = @()
    foreach ($root in $AllowRoot) {
        if (-not [string]::IsNullOrWhiteSpace($root)) {
            $args += @('--allow-root', [System.IO.Path]::GetFullPath($root))
        }
    }
    $args += @(
        '--parallel-tools',
        '--echo-tool-outputs',
        '--turn-timeout-seconds',
        $TurnTimeoutSeconds.ToString(),
        '--tool-timeout-seconds',
        $ToolTimeoutSeconds.ToString(),
        '--scenario-file',
        $scenarioFileFull,
        '--scenario-output',
        ([System.IO.Path]::GetFullPath($scenarioOutputPath))
    )
    if ($ExtraArgs -and $ExtraArgs.Count -gt 0) {
        $args += $ExtraArgs
    }

    Write-Header 'Portable Bundle Smoke Test'
    Write-Step "Bundle: $bundleRootFull"
    if (-not [string]::IsNullOrWhiteSpace($ScenarioPreset)) {
        Write-Step "Scenario preset: $ScenarioPreset"
    }
    Write-Step "Scenario: $scenarioFileFull"
    Write-Step ("Allow roots: {0}" -f ($AllowRoot -join '; '))
    Write-Step "Output: $scenarioOutputPath"

    Push-Location $bundleRootFull
    try {
        & $hostExecutable @args
        if ($LASTEXITCODE -ne 0) {
            throw "Portable bundle smoke scenario failed with exit code $LASTEXITCODE."
        }
    } finally {
        Pop-Location
    }

    Write-Ok "Portable bundle smoke scenario passed: $scenarioFileFull"
}
