# Build full IntelligenceX release artifacts (plugins + portable + installer).

[CmdletBinding()] param(
    [ValidateSet('win-x64','win-arm64')]
    [string] $Runtime = 'win-x64',

    [ValidateSet('Debug','Release')]
    [string] $Configuration = 'Release',

    [ValidateSet('host','app')]
    [string] $Frontend = 'app',

    [string] $Framework = 'net10.0-windows',
    [string] $AppFramework = 'net10.0-windows10.0.26100.0',
    [string] $ReleaseId,
    [string] $OutDir,
    [switch] $ClearOut,
    [string] $TestimoXRoot,

    [switch] $IncludeService,
    [switch] $IncludeSymbols,
    [switch] $SkipWorkspaceBuild,
    [switch] $SkipTests,
    [switch] $SkipHarness,
    [switch] $SkipPluginPackages,
    [switch] $SkipPortable,
    [switch] $SkipInstaller,
    [switch] $SkipChecksums,
    [switch] $Publish,
    [switch] $PublishNuget,
    [switch] $PublishGitHub,

    [switch] $SignInstaller,
    [string] $SignToolPath = 'signtool.exe',
    [string] $SignThumbprint,
    [string] $SignSubjectName,
    [ValidateSet('Warn','Fail','Skip')]
    [string] $SignOnMissingTool,
    [ValidateSet('Warn','Fail','Skip')]
    [string] $SignOnFailure,
    [string] $SignTimestampUrl = 'http://timestamp.digicert.com',
    [string] $SignDescription = 'IntelligenceX Chat',
    [string] $SignUrl,
    [string] $SignCsp,
    [string] $SignKeyContainer,
    [bool] $UseTestimoXSignThumbprintFallback = $true
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:RepoRoot = (Get-Item (Split-Path -Parent $MyInvocation.MyCommand.Path)).Parent.FullName
. (Join-Path $script:RepoRoot 'Build\Internal\Build.ScriptSupport.ps1')

$script:BoundCliParameters = @{}
foreach ($entry in $PSBoundParameters.GetEnumerator()) {
    $script:BoundCliParameters[$entry.Key] = $entry.Value
}

function Has-BoundNonEmptyOption {
    param([string] $Name)

    return $script:BoundCliParameters.ContainsKey($Name) -and -not [string]::IsNullOrWhiteSpace([string] $script:BoundCliParameters[$Name])
}

if ([string]::IsNullOrWhiteSpace($ReleaseId)) {
    $ReleaseId = Get-Date -Format 'yyyyMMdd-HHmmss'
}
if ([string]::IsNullOrWhiteSpace($OutDir)) {
    $OutDir = Join-Path $script:RepoRoot ("Artifacts\UploadReady\{0}" -f $ReleaseId)
}
if ($ClearOut -and (Test-Path -LiteralPath $OutDir)) {
    Remove-Item -LiteralPath $OutDir -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$buildProjectScript = Join-Path $script:RepoRoot 'Build\Build-Project.ps1'
$frontendNormalized = $Frontend.ToLowerInvariant()

if ($frontendNormalized -ne 'app') {
    throw "Build-Release.ps1 only supports -Frontend app on the unified path. Use Build\\Advanced\\Package-Portable.ps1 / Build\\Advanced\\Build-Installer.ps1 for host mode until Build\\powerforge.dotnetpublish.json models that release flow."
}

if ($IncludeService -and $frontendNormalized -eq 'app') {
    Write-Step 'Unified app release already includes the service sidecar; -IncludeService is now a no-op.'
}

if ($PSBoundParameters.ContainsKey('Framework') -and $Framework -ne 'net10.0-windows') {
    Write-Warn "Ignoring -Framework '$Framework'. The unified app release uses the frameworks declared in Build\\powerforge.dotnetpublish.json."
}

$toolOutputs = [System.Collections.Generic.List[string]]::new()
if (-not $SkipPortable) {
    $toolOutputs.Add('Portable')
}
if (-not $SkipInstaller) {
    $toolOutputs.Add('Installer')
}

$parameters = @{
    Configuration = $Configuration
    Targets = @('IntelligenceX.Chat.App')
    Runtimes = @($Runtime)
    Frameworks = @($AppFramework)
    Styles = @('PortableCompat')
    StageRoot = $OutDir
}

if ($toolOutputs.Count -gt 0) {
    $parameters['ToolOutputs'] = @($toolOutputs)
}
if ($SkipWorkspaceBuild) {
    $parameters['SkipWorkspaceBuild'] = $true
}
if ($SkipTests) {
    $parameters['SkipTests'] = $true
}
if ($SkipHarness) {
    $parameters['SkipHarness'] = $true
}
if ($SkipChecksums) {
    $parameters['SkipChecksums'] = $true
}
if ($IncludeSymbols) {
    $parameters['IncludeSymbols'] = $true
}
if ($SkipPluginPackages) {
    if ($toolOutputs.Count -gt 0) {
        $parameters['ToolsOnly'] = $true
    } else {
        throw 'No release assets selected. Remove -SkipPluginPackages or keep at least one of portable/installer enabled.'
    }
} elseif ($toolOutputs.Count -eq 0) {
    $parameters['PackagesOnly'] = $true
}
if ($Publish -or $PublishNuget) {
    $parameters['PublishNuget'] = $true
}
if ($Publish -or $PublishGitHub) {
    $parameters['PublishProjectGitHub'] = $true
}
if ($SignInstaller) {
    $parameters['SignInstaller'] = $true
    $parameters['UseTestimoXSignThumbprintFallback'] = $UseTestimoXSignThumbprintFallback
}
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

if ($enableSigning -and -not [string]::IsNullOrWhiteSpace($SignToolPath)) {
    $parameters['SignToolPath'] = $SignToolPath
}
if ($enableSigning -and -not [string]::IsNullOrWhiteSpace($SignThumbprint)) {
    $parameters['SignThumbprint'] = $SignThumbprint
}
if ($enableSigning -and -not [string]::IsNullOrWhiteSpace($SignSubjectName)) {
    $parameters['SignSubjectName'] = $SignSubjectName
}
if ($enableSigning -and -not [string]::IsNullOrWhiteSpace($SignOnMissingTool)) {
    $parameters['SignOnMissingTool'] = $SignOnMissingTool
}
if ($enableSigning -and -not [string]::IsNullOrWhiteSpace($SignOnFailure)) {
    $parameters['SignOnFailure'] = $SignOnFailure
}
if ($enableSigning -and -not [string]::IsNullOrWhiteSpace($SignTimestampUrl)) {
    $parameters['SignTimestampUrl'] = $SignTimestampUrl
}
if ($enableSigning -and -not [string]::IsNullOrWhiteSpace($SignDescription)) {
    $parameters['SignDescription'] = $SignDescription
}
if ($enableSigning -and -not [string]::IsNullOrWhiteSpace($SignUrl)) {
    $parameters['SignUrl'] = $SignUrl
}
if ($enableSigning -and -not [string]::IsNullOrWhiteSpace($SignCsp)) {
    $parameters['SignCsp'] = $SignCsp
}
if ($enableSigning -and -not [string]::IsNullOrWhiteSpace($SignKeyContainer)) {
    $parameters['SignKeyContainer'] = $SignKeyContainer
}
if (-not [string]::IsNullOrWhiteSpace($TestimoXRoot)) {
    $parameters['TestimoXRoot'] = $TestimoXRoot
}

Write-Header 'Build Release'
Write-Step "Frontend: $frontendNormalized"
Write-Step "Release ID: $ReleaseId"
Write-Step "Runtime: $Runtime"
Write-Step "App framework: $AppFramework"
Write-Step "Output root: $OutDir"
Write-Step "Include symbols: $([bool]$IncludeSymbols)"
Write-Step "Packages: $(-not $SkipPluginPackages)"
Write-Step "Portable: $(-not $SkipPortable)"
Write-Step "Installer: $(-not $SkipInstaller)"
Write-Step "Publish GitHub: $([bool]($Publish -or $PublishGitHub))"
Write-Step "Publish NuGet: $([bool]($Publish -or $PublishNuget))"

Invoke-ScriptFile -ScriptPath $buildProjectScript -Parameters $parameters -FailureContext 'Unified release build failed.' -FailureHint 'Use Build-Project.ps1 -Plan to inspect the release graph, or rerun Build-Project.ps1 directly for a narrower repro.'

Write-Ok "Release artifacts ready: $OutDir"
