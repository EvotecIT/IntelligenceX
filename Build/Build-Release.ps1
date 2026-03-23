# Build full IntelligenceX release artifacts (plugins + portable + installer).

[CmdletBinding()] param(
    [ValidateSet('win-x64','win-arm64')]
    [string] $Runtime = 'win-x64',

    [ValidateSet('Debug','Release')]
    [string] $Configuration = 'Release',

    [ValidateSet('host','app')]
    [string] $Frontend = 'app',

    [string] $Framework = 'net10.0-windows',
    [string] $AppFramework = 'net8.0-windows10.0.26100.0',
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

function Invoke-BuildProjectRelease {
    param(
        [Parameter(Mandatory)]
        [string] $ScriptPath
    )

    $parameters = @{
        Configuration = $Configuration
        Targets = @('IntelligenceX.Chat.App')
        Runtimes = @($Runtime)
        Frameworks = @($AppFramework)
        Styles = @('PortableCompat')
        StageRoot = $OutDir
    }
    if ($SkipWorkspaceBuild) {
        $parameters['SkipWorkspaceBuild'] = $true
    }
    if ($SkipChecksums) {
        $parameters['SkipChecksums'] = $true
    }
    if ($IncludeSymbols) {
        $parameters['IncludeSymbols'] = $true
    }
    if ($SignInstaller) {
        $effectiveSignThumbprint = Resolve-DefaultSignThumbprint -RepoRoot $script:RepoRoot -ExplicitThumbprint $SignThumbprint -UseTestimoXFallback $UseTestimoXSignThumbprintFallback
        if ([string]::IsNullOrWhiteSpace($SignThumbprint) -and -not [string]::IsNullOrWhiteSpace($effectiveSignThumbprint)) {
            $fromEnvironment = @('CERT_THUMBPRINT', 'SIGN_THUMBPRINT', 'CODE_SIGN_THUMBPRINT', 'INTELLIGENCEX_SIGN_THUMBPRINT', 'TESTIMOX_SIGN_THUMBPRINT') |
                Where-Object { -not [string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($_)) } |
                Select-Object -First 1

            if ($fromEnvironment) {
                Write-Step "Using signing thumbprint from environment variable $fromEnvironment."
            } elseif ($UseTestimoXSignThumbprintFallback) {
                Write-Warn 'Using default TestimoX signing thumbprint fallback from sibling repo.'
            }
        } elseif ([string]::IsNullOrWhiteSpace($effectiveSignThumbprint)) {
            Write-Warn 'No default signing thumbprint found; MSI signing will rely on subject name or local cert auto-selection.'
        }

        $parameters['SignInstaller'] = $true
        if (-not [string]::IsNullOrWhiteSpace($effectiveSignThumbprint)) {
            $parameters['SignThumbprint'] = $effectiveSignThumbprint
        }
    }
    if (-not [string]::IsNullOrWhiteSpace($SignToolPath)) {
        $parameters['SignToolPath'] = $SignToolPath
    }
    if (-not [string]::IsNullOrWhiteSpace($SignSubjectName)) {
        $parameters['SignSubjectName'] = $SignSubjectName
    }
    if (-not [string]::IsNullOrWhiteSpace($SignOnMissingTool)) {
        $parameters['SignOnMissingTool'] = $SignOnMissingTool
    }
    if (-not [string]::IsNullOrWhiteSpace($SignOnFailure)) {
        $parameters['SignOnFailure'] = $SignOnFailure
    }
    if (-not [string]::IsNullOrWhiteSpace($SignTimestampUrl)) {
        $parameters['SignTimestampUrl'] = $SignTimestampUrl
    }
    if (-not [string]::IsNullOrWhiteSpace($SignDescription)) {
        $parameters['SignDescription'] = $SignDescription
    }
    if (-not [string]::IsNullOrWhiteSpace($SignUrl)) {
        $parameters['SignUrl'] = $SignUrl
    }
    if (-not [string]::IsNullOrWhiteSpace($SignCsp)) {
        $parameters['SignCsp'] = $SignCsp
    }
    if (-not [string]::IsNullOrWhiteSpace($SignKeyContainer)) {
        $parameters['SignKeyContainer'] = $SignKeyContainer
    }
    if (-not [string]::IsNullOrWhiteSpace($TestimoXRoot)) {
        $parameters['TestimoXRoot'] = $TestimoXRoot
    }

    Invoke-ScriptFile -ScriptPath $ScriptPath -Parameters $parameters -FailureContext 'Unified Build-Project release failed.' -FailureHint 'Use Build-Project.ps1 -Plan to inspect the unified release graph, or rerun Build-Project.ps1 directly for a narrower repro.'
}

function Get-UnifiedReleaseFallbackReasons {
    $reasons = [System.Collections.Generic.List[string]]::new()

    if ($Frontend -ne 'app') {
        $reasons.Add('Frontend host still uses the legacy path.')
    }
    if ($SkipPluginPackages) {
        $reasons.Add('Skipping plugin packages requires the granular path.')
    }
    if ($SkipPortable) {
        $reasons.Add('Skipping the portable bundle requires the granular path.')
    }
    if ($SkipInstaller) {
        $reasons.Add('Skipping the installer requires the granular path.')
    }
    return @($reasons)
}

$script:RepoRoot = (Get-Item (Split-Path -Parent $MyInvocation.MyCommand.Path)).Parent.FullName
. (Join-Path $script:RepoRoot 'Build\Internal\Build.ScriptSupport.ps1')
. (Join-Path $script:RepoRoot 'Build\Internal\Resolve-ReleaseDefaults.ps1')

if ([string]::IsNullOrWhiteSpace($ReleaseId)) {
    $ReleaseId = Get-Date -Format 'yyyyMMdd-HHmmss'
}
if ([string]::IsNullOrWhiteSpace($OutDir)) {
    $OutDir = Join-Path $script:RepoRoot ("Artifacts\Releases\{0}" -f $ReleaseId)
}
if ($ClearOut -and (Test-Path $OutDir)) {
    Remove-Item -Recurse -Force $OutDir
}
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$workspaceScript = Join-Path $script:RepoRoot 'Build\Build-Workspace.ps1'
$buildProjectScript = Join-Path $script:RepoRoot 'Build\Build-Project.ps1'
$pluginsScript = Join-Path $script:RepoRoot 'Build\Advanced\Publish-Plugins.ps1'
$portableScript = Join-Path $script:RepoRoot 'Build\Advanced\Package-Portable.ps1'
$installerScript = Join-Path $script:RepoRoot 'Build\Advanced\Build-Installer.ps1'

$nugetOut = Join-Path $OutDir 'nuget'
$portableOut = Join-Path $OutDir 'portable'
$installerOut = Join-Path $OutDir 'installer'
$bundleName = "IntelligenceX.Chat-$ReleaseId-$Runtime"
$payloadPath = Join-Path $portableOut $bundleName
$serviceIncluded = [bool]$IncludeService

Write-Header 'Build Release'
Write-Step "Frontend: $Frontend"
Write-Step "Release ID: $ReleaseId"
Write-Step "Runtime: $Runtime"
Write-Step "Framework: $Framework"
Write-Step "App framework: $AppFramework"
Write-Step "Output root: $OutDir"
Write-Step "Include symbols: $([bool]$IncludeSymbols)"

$fallbackReasons = @(Get-UnifiedReleaseFallbackReasons)
$canUseUnifiedBuildProject = $fallbackReasons.Count -eq 0

if ($canUseUnifiedBuildProject) {
    Write-Header 'Unified Release'
    Invoke-BuildProjectRelease -ScriptPath $buildProjectScript

    Write-Ok "Release artifacts ready: $OutDir"
    return
}

Write-Header 'Legacy Release Fallback'
foreach ($reason in $fallbackReasons) {
    Write-Step $reason
}

if (-not $SkipWorkspaceBuild) {
    Write-Header 'Workspace Build Validation'
    $workspaceArgs = @{
        Profile = 'full-private'
        Configuration = $Configuration
        IncludePublicTools = $true
        IncludeChat = $true
    }
    if ($SkipTests) {
        $workspaceArgs['SkipTests'] = $true
    }
    if ($SkipHarness) {
        $workspaceArgs['SkipHarness'] = $true
    }
    if (-not [string]::IsNullOrWhiteSpace($TestimoXRoot)) {
        $workspaceArgs['TestimoXRoot'] = $TestimoXRoot
    }
    Invoke-ScriptFile -ScriptPath $workspaceScript -Parameters $workspaceArgs -FailureContext 'Workspace validation failed before release packaging.' -FailureHint 'Use -SkipWorkspaceBuild if you already validated locally, or rerun Build-Workspace.ps1 directly to diagnose the failure.'
}

if (-not $SkipPluginPackages) {
    Write-Header 'Package Plugins (NuGet)'
    $pluginArgs = @{
        Mode = 'all'
        Configuration = $Configuration
        OutDir = $nugetOut
    }
    if (-not [string]::IsNullOrWhiteSpace($TestimoXRoot)) {
        $pluginArgs['TestimoXRoot'] = $TestimoXRoot
    }
    Invoke-ScriptFile -ScriptPath $pluginsScript -Parameters $pluginArgs -FailureContext 'Plugin packaging failed.' -FailureHint 'Run Build\\Advanced\\Publish-Plugins.ps1 directly if you want to narrow the failure to package generation only.'
}

if (-not $SkipPortable) {
    Write-Header 'Package Portable Bundle'
    $portableArgs = @{
        Frontend = $Frontend
        Runtime = $Runtime
        Configuration = $Configuration
        Framework = $Framework
        AppFramework = $AppFramework
        PluginMode = 'all'
        IncludePrivateToolPacks = $true
        OutDir = $portableOut
        BundleName = $bundleName
        ClearOut = $true
        Zip = $true
    }
    if ($IncludeService) {
        $portableArgs['IncludeService'] = $true
    }
    if ($IncludeSymbols) {
        $portableArgs['IncludeSymbols'] = $true
    }
    if (-not [string]::IsNullOrWhiteSpace($TestimoXRoot)) {
        $portableArgs['TestimoXRoot'] = $TestimoXRoot
    }
    Invoke-ScriptFile -ScriptPath $portableScript -Parameters $portableArgs -FailureContext 'Portable bundle generation failed.' -FailureHint 'Run Build\\Advanced\\Package-Portable.ps1 directly if you want to diagnose only the portable bundle path.'
    if ($Frontend -eq 'app' -or $IncludeService) {
        $serviceIncluded = $true
    }
}

if (-not $SkipInstaller) {
    Write-Header 'Build MSI Installer'
    $installerArgs = @{
        Frontend = $Frontend
        Runtime = $Runtime
        Configuration = $Configuration
        Framework = $Framework
        AppFramework = $AppFramework
        OutDir = $installerOut
    }
    if (Test-Path $payloadPath) {
        $installerArgs['PayloadDir'] = $payloadPath
    } else {
        $installerArgs['PortableOutDir'] = $portableOut
        $installerArgs['BundleName'] = $bundleName
    }
    if ($IncludeService) {
        $installerArgs['IncludeService'] = $true
    }
    if ($IncludeSymbols) {
        $installerArgs['IncludeSymbols'] = $true
    }
    if (-not [string]::IsNullOrWhiteSpace($TestimoXRoot)) {
        $installerArgs['TestimoXRoot'] = $TestimoXRoot
    }
    if ($SignInstaller) {
        $SignThumbprint = Resolve-DefaultSignThumbprint -RepoRoot $script:RepoRoot -ExplicitThumbprint $SignThumbprint -UseTestimoXFallback $UseTestimoXSignThumbprintFallback
        if (-not [string]::IsNullOrWhiteSpace($SignThumbprint)) {
            $fromEnvironment = @('CERT_THUMBPRINT', 'SIGN_THUMBPRINT', 'CODE_SIGN_THUMBPRINT', 'INTELLIGENCEX_SIGN_THUMBPRINT', 'TESTIMOX_SIGN_THUMBPRINT') |
                Where-Object { -not [string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable($_)) } |
                Select-Object -First 1
            if ($fromEnvironment) {
                Write-Step "Using signing thumbprint from environment variable $fromEnvironment."
            } elseif ($UseTestimoXSignThumbprintFallback) {
                Write-Warn "Using default TestimoX signing thumbprint fallback from sibling repo."
            }
        } else {
            Write-Warn 'No default signing thumbprint found; MSI signing will rely on subject name or local cert auto-selection.'
        }
        $installerArgs['Sign'] = $true
        $installerArgs['SignToolPath'] = $SignToolPath
        if ($SignThumbprint) { $installerArgs['SignThumbprint'] = $SignThumbprint }
        if ($SignSubjectName) { $installerArgs['SignSubjectName'] = $SignSubjectName }
        if ($SignTimestampUrl) { $installerArgs['SignTimestampUrl'] = $SignTimestampUrl }
        if ($SignDescription) { $installerArgs['SignDescription'] = $SignDescription }
        if ($SignUrl) { $installerArgs['SignUrl'] = $SignUrl }
        if ($SignCsp) { $installerArgs['SignCsp'] = $SignCsp }
        if ($SignKeyContainer) { $installerArgs['SignKeyContainer'] = $SignKeyContainer }
    }
    Invoke-ScriptFile -ScriptPath $installerScript -Parameters $installerArgs -FailureContext 'MSI build failed.' -FailureHint 'Run Build\\Advanced\\Build-Installer.ps1 directly if you want to diagnose only the installer path.'
    if ($Frontend -eq 'app' -or $IncludeService) {
        $serviceIncluded = $true
    }
}

Write-Ok "Release artifacts ready: $OutDir"



