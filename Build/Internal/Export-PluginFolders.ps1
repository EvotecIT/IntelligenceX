[CmdletBinding()] param(
    [ValidateSet('public','private','all')]
    [string] $Mode = 'public',

    [ValidateSet('Debug','Release')]
    [string] $Configuration = 'Release',

    [string] $Framework = 'net10.0-windows',
    [string] $OutDir,
    [string] $TestimoXRoot,
    [switch] $IncludeSymbols,
    [string] $ConfigPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:RepoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
. (Join-Path $script:RepoRoot 'Build\Internal\Build.ScriptSupport.ps1')
. (Join-Path $script:RepoRoot 'Build\Internal\Resolve-PowerForgeCli.ps1')
. (Join-Path $script:RepoRoot 'Build\Internal\Resolve-TestimoXRoot.ps1')

if ([string]::IsNullOrWhiteSpace($OutDir)) {
    $OutDir = Join-Path $script:RepoRoot 'Artifacts\Plugins'
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
    'public' { @('public') }
    'private' { @('private') }
    'all' { @('public', 'private') }
    default { throw "Unexpected mode: $Mode" }
}

$cli = Resolve-PowerForgeCliInvocation -RepoRoot $script:RepoRoot
$pluginArgs = [System.Collections.Generic.List[string]]::new()
$pluginArgs.AddRange([string[]] $cli.Prefix)
$pluginArgs.Add('plugin')
$pluginArgs.Add('export')
$pluginArgs.Add('--config')
$pluginArgs.Add($ConfigPath)
$pluginArgs.Add('--configuration')
$pluginArgs.Add($Configuration)
$pluginArgs.Add('--framework')
$pluginArgs.Add($Framework)
$pluginArgs.Add('--output-root')
$pluginArgs.Add($OutDir)
$pluginArgs.Add('--group')
$pluginArgs.Add(($groups -join ','))
if ($IncludeSymbols) {
    $pluginArgs.Add('--keep-symbols')
}

$needsPrivateRoot = $groups -contains 'private'
$resolvedTestimoXRoot = $null
if ($needsPrivateRoot) {
    $resolvedTestimoXRoot = Resolve-TestimoXRoot -Provided $TestimoXRoot -RepoRoot $script:RepoRoot
}

Write-Header 'Export Plugin Folders'
Write-Step "Mode: $Mode"
Write-Step "Groups: $($groups -join ', ')"
Write-Step "Framework: $Framework"
Write-Step "Config: $ConfigPath"
Write-Step "Output: $OutDir"
Write-Step "Include symbols: $([bool] $IncludeSymbols)"
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
    throw "PowerForge plugin export failed with exit code ${LASTEXITCODE}."
}

Write-Ok 'Plugin folder export complete.'
