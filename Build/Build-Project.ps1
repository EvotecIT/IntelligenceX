param(
    [string] $ConfigPath = "$PSScriptRoot\release.json",
    [switch] $Plan,
    [switch] $Validate,
    [switch] $PublishNuget,
    [switch] $PublishProjectGitHub,
    [switch] $PublishToolGitHub,
    [switch] $SkipWorkspaceBuild,
    [switch] $SkipRestore,
    [switch] $SkipBuild,
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
    [string] $OutputRoot,
    [string] $StageRoot,
    [string] $ManifestJsonPath,
    [switch] $AllowOutputOutsideProjectRoot,
    [switch] $AllowManifestOutsideProjectRoot,
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
    [bool] $UseTestimoXSignThumbprintFallback = $true,
    [string[]] $Targets,
    [string[]] $Runtimes,
    [string[]] $Frameworks,
    [string[]] $Styles,
    [ValidateSet('Tool', 'Portable', 'Installer', 'Store')]
    [string[]] $ToolOutputs,
    [ValidateSet('Tool', 'Portable', 'Installer', 'Store')]
    [string[]] $SkipToolOutputs,
    [string[]] $InstallerProperties,
    [string] $TestimoXRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = (Get-Item (Split-Path -Parent $MyInvocation.MyCommand.Path)).Parent.FullName
. (Join-Path $repoRoot 'Build\Internal\Resolve-PowerForgeCli.ps1')
. (Join-Path $repoRoot 'Build\Internal\Resolve-ReleaseDefaults.ps1')

$script:BoundCliParameters = @{}
foreach ($entry in $PSBoundParameters.GetEnumerator()) {
    $script:BoundCliParameters[$entry.Key] = $entry.Value
}

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

function Has-BoundNonEmptyOption {
    param([string] $Name)

    return $script:BoundCliParameters.ContainsKey($Name) -and -not [string]::IsNullOrWhiteSpace([string] $script:BoundCliParameters[$Name])
}

Add-Flag '--plan' $Plan
Add-Flag '--validate' $Validate
Add-Flag '--publish-nuget' $PublishNuget
Add-Flag '--publish-project-github' $PublishProjectGitHub
Add-Flag '--publish-tool-github' $PublishToolGitHub
Add-Flag '--packages-only' $PackagesOnly
Add-Flag '--tools-only' $ToolsOnly
Add-Flag '--keep-symbols' $IncludeSymbols
Add-Flag '--skip-workspace-validation' $SkipWorkspaceBuild
Add-Flag '--skip-restore' $SkipRestore
Add-Flag '--skip-build' $SkipBuild

Add-Option '--stage-root' $StageRoot
Add-Option '--output-root' $OutputRoot
Add-Option '--manifest-json' $ManifestJsonPath
Add-Flag '--allow-output-outside-project-root' $AllowOutputOutsideProjectRoot
Add-Flag '--allow-manifest-outside-project-root' $AllowManifestOutsideProjectRoot
Add-Option '--checksums-path' $ChecksumsPath
Add-Flag '--skip-release-checksums' $SkipChecksums
Add-Option '--workspace-profile' $WorkspaceProfile
$hasExplicitSigningOverride = @(
    'SignToolPath'
    'SignThumbprint'
    'SignSubjectName'
    'SignOnMissingTool'
    'SignOnFailure'
    'SignTimestampUrl'
    'SignDescription'
    'SignUrl'
    'SignCsp'
    'SignKeyContainer'
) | Where-Object { Has-BoundNonEmptyOption $_ } | Select-Object -First 1
$enableSigning = $SignInstaller -or $hasExplicitSigningOverride

Add-Flag '--sign' $enableSigning

if ($enableSigning) {
    Add-Option '--sign-tool-path' $SignToolPath
    Add-Option '--sign-subject-name' $SignSubjectName
    Add-Option '--sign-on-missing-tool' $SignOnMissingTool
    Add-Option '--sign-on-failure' $SignOnFailure
    Add-Option '--sign-timestamp-url' $SignTimestampUrl
    Add-Option '--sign-description' $SignDescription
    Add-Option '--sign-url' $SignUrl
    Add-Option '--sign-csp' $SignCsp
    Add-Option '--sign-key-container' $SignKeyContainer
}

if ($SignInstaller) {
    $resolvedSignThumbprint = Resolve-DefaultSignThumbprint -RepoRoot $repoRoot -ExplicitThumbprint $SignThumbprint -UseTestimoXFallback $UseTestimoXSignThumbprintFallback
    if (-not [string]::IsNullOrWhiteSpace($resolvedSignThumbprint)) {
        $SignThumbprint = $resolvedSignThumbprint
    }
}
if ($enableSigning) {
    Add-Option '--sign-thumbprint' $SignThumbprint
}

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
Add-CsvOption '--tool-output' $ToolOutputs
Add-CsvOption '--skip-tool-output' $SkipToolOutputs
Add-CsvOption '--installer-property' $InstallerProperties

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
