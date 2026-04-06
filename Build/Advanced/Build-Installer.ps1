# Build IntelligenceX Chat MSI installer.

[CmdletBinding()] param(
    [ValidateSet('win-x64','win-arm64')]
    [string] $Runtime = 'win-x64',

    [ValidateSet('Debug','Release')]
    [string] $Configuration = 'Release',

    [ValidateSet('host','app')]
    [string] $Frontend = 'app',

    [string] $Framework = 'net10.0-windows',
    [string] $AppFramework = 'net10.0-windows10.0.26100.0',
    [string] $PayloadDir,
    [string] $PortableOutDir,
    [string] $BundleName,
    [switch] $IncludeService,
    [switch] $IncludeSymbols,
    [string] $TestimoXRoot,

    [string] $ProductName = 'IntelligenceX Chat',
    [string] $Manufacturer = 'Evotec',
    [string] $ProductVersion,
    [string] $UpgradeCode = '{a2b787a5-f539-4763-add6-2baa2c2518c7}',

    [string] $OutDir,
    [switch] $Sign,
    [string] $SignToolPath = 'signtool.exe',
    [string] $SignThumbprint,
    [string] $SignSubjectName,
    [string] $SignTimestampUrl = 'http://timestamp.digicert.com',
    [string] $SignDescription = 'IntelligenceX Chat',
    [string] $SignUrl,
    [string] $SignCsp,
    [string] $SignKeyContainer,
    [bool] $UseTestimoXSignThumbprintFallback = $true
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$script:RepoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
. (Join-Path $script:RepoRoot 'Build\Internal\Build.ScriptSupport.ps1')

$legacyScript = Join-Path $script:RepoRoot 'Build\Internal\Build-Installer.Legacy.ps1'
$buildProjectScript = Join-Path $script:RepoRoot 'Build\Build-Project.ps1'

function Use-LegacyInstallerFlow {
    if ($Frontend.ToLowerInvariant() -ne 'app') {
        return $true
    }
    if (-not [string]::IsNullOrWhiteSpace($PayloadDir)) {
        return $true
    }
    if (-not [string]::IsNullOrWhiteSpace($PortableOutDir)) {
        return $true
    }
    if (-not [string]::IsNullOrWhiteSpace($BundleName)) {
        return $true
    }
    return $false
}

if (Use-LegacyInstallerFlow) {
    Write-Warn 'Falling back to the legacy installer path because custom payload, bundle-layout, or host-mode options were requested.'
    $legacyParameters = @{}
    foreach ($name in $PSBoundParameters.Keys) {
        $legacyParameters[$name] = $PSBoundParameters[$name]
    }

    Invoke-ScriptFile -ScriptPath $legacyScript -Parameters $legacyParameters -FailureContext 'Legacy installer build failed.' -FailureHint 'Use the default app path without custom payload, bundle-layout, or host-mode options to stay on the thin PowerForge flow.'
    return
}

if (-not [string]::IsNullOrWhiteSpace($Framework) -and $Framework -ne 'net10.0-windows') {
    Write-Warn "Ignoring -Framework '$Framework'. The unified app installer uses the frameworks declared in Build\\powerforge.dotnetpublish.json."
}
if ($IncludeService) {
    Write-Step 'Unified app installer already includes the required service payload; -IncludeService is now a no-op.'
}

$stageRoot = if ([string]::IsNullOrWhiteSpace($OutDir)) {
    Join-Path $script:RepoRoot ("Artifacts\Installer\IntelligenceX.Chat\{0}\PowerForgeStage" -f $Runtime)
} else {
    Join-Path ([System.IO.Path]::GetFullPath($OutDir)) '_powerforge-stage'
}

if (Test-Path -LiteralPath $stageRoot) {
    Remove-Item -LiteralPath $stageRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $stageRoot -Force | Out-Null

$parameters = @{
    Configuration = $Configuration
    Targets = @('IntelligenceX.Chat.App')
    Runtimes = @($Runtime)
    Frameworks = @($AppFramework)
    Styles = @('PortableCompat')
    ToolOutputs = @('Installer')
    ToolsOnly = $true
    SkipWorkspaceBuild = $true
    StageRoot = $stageRoot
}

if ($IncludeSymbols) {
    $parameters['IncludeSymbols'] = $true
}

$installerProperties = [System.Collections.Generic.List[string]]::new()
if ($ProductName -ne 'IntelligenceX Chat') {
    $installerProperties.Add("ProductName=$ProductName")
}
if ($Manufacturer -ne 'Evotec') {
    $installerProperties.Add("Manufacturer=$Manufacturer")
}
if ($PSBoundParameters.ContainsKey('ProductVersion') -and -not [string]::IsNullOrWhiteSpace($ProductVersion)) {
    $installerProperties.Add("ProductVersion=$ProductVersion")
}
if ($UpgradeCode -ne '{a2b787a5-f539-4763-add6-2baa2c2518c7}') {
    $installerProperties.Add("UpgradeCode=$UpgradeCode")
}
if ($installerProperties.Count -gt 0) {
    $parameters['InstallerProperties'] = @($installerProperties)
}

if ($Sign) {
    $parameters['SignInstaller'] = $true
    $parameters['UseTestimoXSignThumbprintFallback'] = $UseTestimoXSignThumbprintFallback
}
if ($PSBoundParameters.ContainsKey('SignToolPath') -and -not [string]::IsNullOrWhiteSpace($SignToolPath)) {
    $parameters['SignToolPath'] = $SignToolPath
}
if ($PSBoundParameters.ContainsKey('SignThumbprint') -and -not [string]::IsNullOrWhiteSpace($SignThumbprint)) {
    $parameters['SignThumbprint'] = $SignThumbprint
}
if ($PSBoundParameters.ContainsKey('SignSubjectName') -and -not [string]::IsNullOrWhiteSpace($SignSubjectName)) {
    $parameters['SignSubjectName'] = $SignSubjectName
}
if ($PSBoundParameters.ContainsKey('SignTimestampUrl') -and -not [string]::IsNullOrWhiteSpace($SignTimestampUrl)) {
    $parameters['SignTimestampUrl'] = $SignTimestampUrl
}
if ($PSBoundParameters.ContainsKey('SignDescription') -and -not [string]::IsNullOrWhiteSpace($SignDescription)) {
    $parameters['SignDescription'] = $SignDescription
}
if ($PSBoundParameters.ContainsKey('SignUrl') -and -not [string]::IsNullOrWhiteSpace($SignUrl)) {
    $parameters['SignUrl'] = $SignUrl
}
if ($PSBoundParameters.ContainsKey('SignCsp') -and -not [string]::IsNullOrWhiteSpace($SignCsp)) {
    $parameters['SignCsp'] = $SignCsp
}
if ($PSBoundParameters.ContainsKey('SignKeyContainer') -and -not [string]::IsNullOrWhiteSpace($SignKeyContainer)) {
    $parameters['SignKeyContainer'] = $SignKeyContainer
}
if (-not [string]::IsNullOrWhiteSpace($TestimoXRoot)) {
    $parameters['TestimoXRoot'] = $TestimoXRoot
}

Write-Header 'Build Installer'
Write-Step 'Flow: thin PowerForge installer path'
Write-Step "Runtime: $Runtime"
Write-Step "App framework: $AppFramework"
Write-Step "Stage root: $stageRoot"

Invoke-ScriptFile -ScriptPath $buildProjectScript -Parameters $parameters -FailureContext 'Unified installer build failed.' -FailureHint 'Use the legacy path only for custom payload, bundle-layout, or host-mode flows.'

$resolvedOutDir = if ([string]::IsNullOrWhiteSpace($OutDir)) {
    Join-Path $script:RepoRoot ("Artifacts\Installer\IntelligenceX.Chat\{0}\Output" -f $Runtime)
} else {
    [System.IO.Path]::GetFullPath($OutDir)
}
New-Item -ItemType Directory -Path $resolvedOutDir -Force | Out-Null

$stageMsi = Get-ChildItem -Path $stageRoot -Recurse -Filter '*.msi' -File -ErrorAction SilentlyContinue |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if ($null -eq $stageMsi) {
    throw "Unified installer flow completed but no MSI was found under stage root: $stageRoot"
}

$finalMsiPath = Join-Path $resolvedOutDir $stageMsi.Name
Copy-Item -LiteralPath $stageMsi.FullName -Destination $finalMsiPath -Force

Write-Ok ("MSI ready: {0}" -f $finalMsiPath)
