param(
    [string] $ConfigPath,
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
    [int] $SignTimeoutSeconds,
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
$focusedPackageParameters = @(
    'PackagesOnly',
    'SkipBuild',
    'PublishNuget',
    'PublishProjectGitHub',
    'Plan',
    'Configuration',
    'Verbose',
    'Debug',
    'ErrorAction',
    'WarningAction',
    'InformationAction',
    'ProgressAction',
    'ErrorVariable',
    'WarningVariable',
    'InformationVariable',
    'OutVariable',
    'OutBuffer',
    'PipelineVariable'
)
$hasWiderReleaseArguments = @($PSBoundParameters.Keys | Where-Object { $_ -notin $focusedPackageParameters }).Count -gt 0
$hasExplicitCliRoute = -not [string]::IsNullOrWhiteSpace($env:POWERFORGE_CLI_PATH) -or
    -not [string]::IsNullOrWhiteSpace($env:POWERFORGE_ROOT)
$hasProjectBuildModule = @(Get-Module -ListAvailable -Name PSPublishModule).Count -gt 0

if ($PackagesOnly -and
    -not $hasWiderReleaseArguments -and
    $Configuration -eq 'Release' -and
    -not $hasExplicitCliRoute -and
    $hasProjectBuildModule) {
    $moduleParameters = @{
        Name = 'PSPublishModule'
        Force = $true
        ErrorAction = 'Stop'
    }
    if ($PublishNuget) {
        $moduleParameters.MinimumVersion = '3.0.126'
    }
    Import-Module @moduleParameters

    $packageBuildParameters = @{
        ConfigPath       = Join-Path $PSScriptRoot 'project.build.json'
        Build            = -not $SkipBuild
        PublishNuget     = [bool] $PublishNuget
        PublishGitHub    = [bool] $PublishProjectGitHub
        Plan             = [bool] $Plan
    }

    Invoke-ProjectBuild @packageBuildParameters
    return
}

. (Join-Path $repoRoot 'Build\Internal\Resolve-PowerForgeCli.ps1')
. (Join-Path $repoRoot 'Build\Internal\Resolve-ReleaseDefaults.ps1')

if ([string]::IsNullOrWhiteSpace($ConfigPath)) {
    $ConfigPath = Join-Path $PSScriptRoot 'release.json'
}

function Resolve-RepoRelativePath {
    param([string] $PathValue)

    if ([string]::IsNullOrWhiteSpace($PathValue)) {
        return $PathValue
    }

    if ([System.IO.Path]::IsPathRooted($PathValue)) {
        return [System.IO.Path]::GetFullPath($PathValue)
    }

    return [System.IO.Path]::GetFullPath((Join-Path $repoRoot $PathValue))
}

$defaultConfigPath = Resolve-RepoRelativePath (Join-Path $PSScriptRoot 'release.json')
$configPathWasExplicit = $PSBoundParameters.ContainsKey('ConfigPath')
$resolvedConfigPath = Resolve-RepoRelativePath $ConfigPath
if (-not $configPathWasExplicit -and $PackagesOnly -and [string]::Equals($resolvedConfigPath, $defaultConfigPath, [System.StringComparison]::OrdinalIgnoreCase)) {
    $packagesConfigPath = Join-Path $PSScriptRoot 'release.packages.json'
    if (Test-Path -LiteralPath $packagesConfigPath) {
        $ConfigPath = $packagesConfigPath
    }
}
$ConfigPath = Resolve-RepoRelativePath $ConfigPath

$script:BoundCliParameters = @{}
foreach ($entry in $PSBoundParameters.GetEnumerator()) {
    $script:BoundCliParameters[$entry.Key] = $entry.Value
}

if ($Plan -and $Validate) {
    throw 'Use either -Plan or -Validate, not both.'
}

$cli = Resolve-PowerForgeCliInvocation -RepoRoot $repoRoot

function Assert-DependencyOrderCapableCli {
    param([hashtable] $Cli)

    $versionArgs = [System.Collections.Generic.List[string]]::new()
    $versionArgs.AddRange([string[]] $Cli.Prefix)
    $versionArgs.Add('--version')
    $versionOutput = & $Cli.Command @versionArgs 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "Unable to verify the PowerForge CLI version required for dependency-safe NuGet publication."
    }

    $versionMatch = [regex]::Match(($versionOutput -join "`n"), '(?<!\d)(?<Version>\d+\.\d+\.\d+)(?!\d)')
    if (-not $versionMatch.Success -or
        [version] $versionMatch.Groups['Version'].Value -lt [version] '3.0.126') {
        throw "NuGet publication requires PowerForge CLI 3.0.126 or newer so repository packages are published in dependency order."
    }
}

if ($PublishNuget) {
    Assert-DependencyOrderCapableCli -Cli $cli
}

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

Add-Option '--stage-root' (Resolve-RepoRelativePath $StageRoot)
Add-Option '--output-root' (Resolve-RepoRelativePath $OutputRoot)
Add-Option '--manifest-json' (Resolve-RepoRelativePath $ManifestJsonPath)
Add-Flag '--allow-output-outside-project-root' $AllowOutputOutsideProjectRoot
Add-Flag '--allow-manifest-outside-project-root' $AllowManifestOutsideProjectRoot
Add-Option '--checksums-path' (Resolve-RepoRelativePath $ChecksumsPath)
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
if (-not $hasExplicitSigningOverride -and
    $script:BoundCliParameters.ContainsKey('SignTimeoutSeconds') -and
    $SignTimeoutSeconds -gt 0) {
    $hasExplicitSigningOverride = 'SignTimeoutSeconds'
}
$enableSigning = $SignInstaller -or $hasExplicitSigningOverride

Add-Flag '--sign' $enableSigning

if ($enableSigning) {
    Add-Option '--sign-tool-path' $SignToolPath
    Add-Option '--sign-subject-name' $SignSubjectName
    Add-Option '--sign-on-missing-tool' $SignOnMissingTool
    Add-Option '--sign-on-failure' $SignOnFailure
    if ($SignTimeoutSeconds -gt 0) {
        Add-Option '--sign-timeout-seconds' ([string] $SignTimeoutSeconds)
    }
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
