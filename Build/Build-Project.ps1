param(
    [string] $ConfigPath = "$PSScriptRoot\release.json",
    [switch] $Plan,
    [switch] $Validate,
    [switch] $PublishNuget,
    [switch] $PublishProjectGitHub,
    [switch] $PublishToolGitHub,
    [switch] $SkipWorkspaceBuild,
    [ValidateSet('oss', 'full-private')]
    [string] $WorkspaceProfile,
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',
    [switch] $SkipTests,
    [switch] $SkipHarness,
    [switch] $IncludePublicTools = $true,
    [switch] $IncludeChat,
    [switch] $PackagesOnly,
    [switch] $ToolsOnly,
    [string] $StageRoot,
    [string] $ManifestJsonPath,
    [string] $ChecksumsPath,
    [switch] $SkipChecksums,
    [switch] $IncludeSymbols,
    [switch] $SignInstaller,
    [string] $SignToolPath,
    [string] $SignThumbprint,
    [string] $SignSubjectName,
    [ValidateSet('Warn', 'Fail', 'Skip')]
    [string] $SignOnMissingTool,
    [ValidateSet('Warn', 'Fail', 'Skip')]
    [string] $SignOnFailure,
    [string] $SignTimestampUrl,
    [string] $SignDescription,
    [string] $SignUrl,
    [string] $SignCsp,
    [string] $SignKeyContainer,
    [string[]] $Targets,
    [string[]] $Runtimes,
    [string[]] $Frameworks,
    [string[]] $Styles,
    [string] $TestimoXRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Get-Item (Split-Path -Parent $MyInvocation.MyCommand.Path)).Parent.FullName
. (Join-Path $repoRoot 'Build\Internal\Resolve-PowerForgeCli.ps1')
. (Join-Path $repoRoot 'Build\Internal\Resolve-ReleaseDefaults.ps1')

if ($Plan -and $Validate) {
    throw 'Use either -Plan or -Validate, not both.'
}

$cli = Resolve-PowerForgeCliInvocation -RepoRoot $repoRoot
$releaseArgs = [System.Collections.Generic.List[string]]::new()
$releaseArgs.AddRange([string[]] $cli.Prefix)
$releaseArgs.Add('release')
$releaseArgs.Add('--config')
$releaseArgs.Add($ConfigPath)
$releaseArgs.Add('--configuration')
$releaseArgs.Add($Configuration)

function Add-Flag {
    param([string] $Name, [bool] $Enabled)

    if ($Enabled) {
        $releaseArgs.Add($Name)
    }
}

function Add-Option {
    param([string] $Name, [string] $Value)

    if (-not [string]::IsNullOrWhiteSpace($Value)) {
        $releaseArgs.Add($Name)
        $releaseArgs.Add($Value)
    }
}

function Add-CsvOption {
    param([string] $Name, [string[]] $Values)

    $effective = @($Values | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if ($effective.Count -gt 0) {
        $releaseArgs.Add($Name)
        $releaseArgs.Add(($effective -join ','))
    }
}

Add-Flag '--plan' $Plan
Add-Flag '--validate' $Validate
Add-Flag '--publish-nuget' $PublishNuget
Add-Flag '--publish-project-github' $PublishProjectGitHub
Add-Flag '--publish-tool-github' $PublishToolGitHub
Add-Flag '--packages-only' $PackagesOnly
Add-Flag '--tools-only' $ToolsOnly
Add-Flag '--keep-symbols' $IncludeSymbols
Add-Flag '--sign' $SignInstaller
Add-Flag '--skip-workspace-validation' $SkipWorkspaceBuild

Add-Option '--stage-root' $StageRoot
Add-Option '--manifest-json' $ManifestJsonPath
Add-Option '--checksums-path' $ChecksumsPath
Add-Flag '--skip-release-checksums' $SkipChecksums
Add-Option '--workspace-profile' $WorkspaceProfile
Add-Option '--sign-tool-path' $SignToolPath
Add-Option '--sign-subject-name' $SignSubjectName
Add-Option '--sign-on-missing-tool' $SignOnMissingTool
Add-Option '--sign-on-failure' $SignOnFailure
Add-Option '--sign-timestamp-url' $SignTimestampUrl
Add-Option '--sign-description' $SignDescription
Add-Option '--sign-url' $SignUrl
Add-Option '--sign-csp' $SignCsp
Add-Option '--sign-key-container' $SignKeyContainer

if ($SignInstaller) {
    $resolvedSignThumbprint = Resolve-DefaultSignThumbprint -RepoRoot $repoRoot -ExplicitThumbprint $SignThumbprint -UseTestimoXFallback $true
    if (-not [string]::IsNullOrWhiteSpace($resolvedSignThumbprint)) {
        $SignThumbprint = $resolvedSignThumbprint
    }
}
Add-Option '--sign-thumbprint' $SignThumbprint

if ($SkipTests) {
    Add-Option '--workspace-disable-feature' 'tests'
}
if ($SkipHarness) {
    Add-Option '--workspace-disable-feature' 'harness'
}
if (-not $IncludePublicTools) {
    Add-Option '--workspace-disable-feature' 'public-tools'
}
if ($IncludeChat) {
    Add-Option '--workspace-enable-feature' 'chat'
}

Add-CsvOption '--target' $Targets
Add-CsvOption '--rid' $Runtimes
Add-CsvOption '--framework' $Frameworks
Add-CsvOption '--style' $Styles

if (-not [string]::IsNullOrWhiteSpace($TestimoXRoot)) {
    $resolvedTestimoXRoot = [System.IO.Path]::GetFullPath($TestimoXRoot)
    $previousTestimoXRoot = $env:TESTIMOX_ROOT
    $previousLegacyTestimoXRoot = $env:TestimoXRoot
        $env:TESTIMOX_ROOT = $resolvedTestimoXRoot
        $env:TestimoXRoot = $resolvedTestimoXRoot
    try {
        Add-Option '--workspace-testimox-root' $resolvedTestimoXRoot
        & $cli.Command @releaseArgs
    } finally {
        $env:TESTIMOX_ROOT = $previousTestimoXRoot
        $env:TestimoXRoot = $previousLegacyTestimoXRoot
    }
} else {
    & $cli.Command @releaseArgs
}

if ($LASTEXITCODE -ne 0) {
    throw "PowerForge release failed with exit code ${LASTEXITCODE}."
}
