[CmdletBinding()]
param(
    [string] $ConfigPath = "$PSScriptRoot\workspace.validation.json",
    [ValidateSet('oss', 'full-private')]
    [string] $Profile = 'oss',
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',
    [switch] $SkipTests,
    [switch] $SkipHarness,
    [switch] $IncludePublicTools = $true,
    [switch] $IncludeChat,
    [string] $TestimoXRoot,
    [switch] $Plan,
    [switch] $Validate,
    [switch] $ListProfiles
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Get-Item (Split-Path -Parent $MyInvocation.MyCommand.Path)).Parent.FullName
. (Join-Path $repoRoot 'Build\Internal\Resolve-PowerForgeCli.ps1')

$cli = Resolve-PowerForgeCliInvocation -RepoRoot $repoRoot
$args = [System.Collections.Generic.List[string]]::new()
$args.AddRange([string[]] $cli.Prefix)
$args.Add('workspace')
$args.Add('validate')
$args.Add('--config')
$args.Add($ConfigPath)

if ($ListProfiles) {
    $args.Add('--list')
} else {
    $args.Add('--profile')
    $args.Add($Profile)
    $args.Add('--configuration')
    $args.Add($Configuration)
}

if ($Plan) {
    $args.Add('--plan')
}
if ($Validate) {
    $args.Add('--validate')
}
if ($SkipTests) {
    $args.Add('--disable-feature')
    $args.Add('tests')
}
if ($SkipHarness) {
    $args.Add('--disable-feature')
    $args.Add('harness')
}
if (-not $IncludePublicTools) {
    $args.Add('--disable-feature')
    $args.Add('public-tools')
}
if ($IncludeChat) {
    $args.Add('--enable-feature')
    $args.Add('chat')
}

if (-not [string]::IsNullOrWhiteSpace($TestimoXRoot)) {
    $resolvedTestimoXRoot = [System.IO.Path]::GetFullPath($TestimoXRoot)
    $previousTestimoXRoot = $env:TESTIMOX_ROOT
    $previousLegacyTestimoXRoot = $env:TestimoXRoot
    $env:TESTIMOX_ROOT = $resolvedTestimoXRoot
    $env:TestimoXRoot = $resolvedTestimoXRoot
    try {
        & $cli.Command @args
    } finally {
        $env:TESTIMOX_ROOT = $previousTestimoXRoot
        $env:TestimoXRoot = $previousLegacyTestimoXRoot
    }
} else {
    & $cli.Command @args
}

if ($LASTEXITCODE -ne 0) {
    throw "PowerForge workspace validation failed with exit code ${LASTEXITCODE}."
}
