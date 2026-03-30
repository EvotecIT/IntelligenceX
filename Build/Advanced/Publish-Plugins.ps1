[CmdletBinding()] param(
    [ValidateSet('public','private','all')]
    [string] $Mode = 'public',

    [ValidateSet('Debug','Release')]
    [string] $Configuration = 'Release',

    [string] $OutDir,
    [string] $VersionSuffix,
    [string] $PackageVersion,
    [switch] $NoBuild,
    [switch] $IncludeSymbols,
    [string] $TestimoXRoot,
    [string] $ConfigPath,

    [switch] $Push,
    [string] $Source,
    [string] $ApiKey,
    [switch] $SkipDuplicate = $true
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:RepoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
. (Join-Path $script:RepoRoot 'Build\Internal\Build.ScriptSupport.ps1')
. (Join-Path $script:RepoRoot 'Build\Internal\Resolve-PowerForgeCli.ps1')
. (Join-Path $script:RepoRoot 'Build\Internal\Resolve-TestimoXRoot.ps1')

if ([string]::IsNullOrWhiteSpace($OutDir)) {
    $OutDir = Join-Path $script:RepoRoot 'Artifacts\NuGet'
}
if ([string]::IsNullOrWhiteSpace($ConfigPath)) {
    $ConfigPath = Join-Path $script:RepoRoot 'Build\powerforge.plugins.json'
}

$OutDir = [System.IO.Path]::GetFullPath($OutDir)
$ConfigPath = [System.IO.Path]::GetFullPath($ConfigPath)
if (-not (Test-Path -LiteralPath $ConfigPath)) {
    throw "Plugin catalog config not found: $ConfigPath"
}

$groups = switch ($Mode) {
    'public' { @('pack-public') }
    'private' { @('pack-private') }
    'all' { @('pack-public', 'pack-private') }
    default { throw "Unexpected mode: $Mode" }
}

$cli = Resolve-PowerForgeCliInvocation -RepoRoot $script:RepoRoot
$pluginArgs = [System.Collections.Generic.List[string]]::new()
$pluginArgs.AddRange([string[]] $cli.Prefix)
$pluginArgs.Add('plugin')
$pluginArgs.Add('pack')
$pluginArgs.Add('--config')
$pluginArgs.Add($ConfigPath)
$pluginArgs.Add('--configuration')
$pluginArgs.Add($Configuration)
$pluginArgs.Add('--output-root')
$pluginArgs.Add($OutDir)
$pluginArgs.Add('--group')
$pluginArgs.Add(($groups -join ','))
if ($NoBuild) {
    $pluginArgs.Add('--no-build')
}
if ($IncludeSymbols) {
    $pluginArgs.Add('--include-symbols')
}
if (-not [string]::IsNullOrWhiteSpace($PackageVersion)) {
    $pluginArgs.Add('--package-version')
    $pluginArgs.Add($PackageVersion)
} elseif (-not [string]::IsNullOrWhiteSpace($VersionSuffix)) {
    $pluginArgs.Add('--version-suffix')
    $pluginArgs.Add($VersionSuffix)
}
if ($Push) {
    if ([string]::IsNullOrWhiteSpace($Source)) {
        throw "When -Push is specified, provide -Source."
    }
    if ([string]::IsNullOrWhiteSpace($ApiKey)) {
        throw "When -Push is specified, provide -ApiKey."
    }

    $pluginArgs.Add('--push')
    $pluginArgs.Add('--source')
    $pluginArgs.Add($Source)
    $pluginArgs.Add('--api-key')
    $pluginArgs.Add($ApiKey)
    if ($SkipDuplicate) {
        $pluginArgs.Add('--skip-duplicate')
    }
}

$needsPrivateRoot = $groups -contains 'pack-private'
$resolvedTestimoXRoot = $null
if ($needsPrivateRoot) {
    $resolvedTestimoXRoot = Resolve-TestimoXRoot -Provided $TestimoXRoot -RepoRoot $script:RepoRoot
}

Write-Header 'Pack Plugins'
Write-Step "Mode: $Mode"
Write-Step "Groups: $($groups -join ', ')"
Write-Step "Config: $ConfigPath"
Write-Step "Output: $OutDir"
Write-Step "No build: $([bool] $NoBuild)"
Write-Step "Include symbols: $([bool] $IncludeSymbols)"
if (-not [string]::IsNullOrWhiteSpace($PackageVersion)) {
    Write-Step "PackageVersion: $PackageVersion"
} elseif (-not [string]::IsNullOrWhiteSpace($VersionSuffix)) {
    Write-Step "VersionSuffix: $VersionSuffix"
}
if ($Push) {
    Write-Step "Push source: $Source"
    Write-Step "Skip duplicate: $([bool] $SkipDuplicate)"
}
if (-not [string]::IsNullOrWhiteSpace($resolvedTestimoXRoot)) {
    Write-Step "TestimoXRoot: $resolvedTestimoXRoot"
}
Write-Step ("PowerForge: " + (Format-CommandLine -Command $cli.Command -Arguments $pluginArgs))

if ($needsPrivateRoot) {
    $previousTestimoXRoot = $env:TESTIMOX_ROOT
    $previousLegacyTestimoXRoot = $env:TestimoXRoot
    $env:TESTIMOX_ROOT = $resolvedTestimoXRoot
    $env:TestimoXRoot = $resolvedTestimoXRoot
    try {
        & $cli.Command @pluginArgs
    } finally {
        $env:TESTIMOX_ROOT = $previousTestimoXRoot
        $env:TestimoXRoot = $previousLegacyTestimoXRoot
    }
} else {
    & $cli.Command @pluginArgs
}

if ($LASTEXITCODE -ne 0) {
    throw "PowerForge plugin pack failed with exit code ${LASTEXITCODE}."
}

Write-Ok 'Plugin packaging complete.'
