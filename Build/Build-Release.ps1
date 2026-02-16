# Build full IntelligenceX release artifacts (plugins + portable + installer).

[CmdletBinding()] param(
    [ValidateSet('win-x64','win-arm64')]
    [string] $Runtime = 'win-x64',

    [ValidateSet('Debug','Release')]
    [string] $Configuration = 'Release',

    [string] $Framework = 'net10.0-windows',
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
    [string] $SignTimestampUrl = 'http://timestamp.digicert.com',
    [string] $SignDescription = 'IntelligenceX Chat',
    [string] $SignUrl,
    [string] $SignCsp,
    [string] $SignKeyContainer
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Header($text) { Write-Host "`n=== $text ===" -ForegroundColor Cyan }
function Write-Step($text) { Write-Host "[+] $text" -ForegroundColor Yellow }
function Write-Ok($text) { Write-Host "[OK] $text" -ForegroundColor Green }

function Invoke-ScriptFile {
    param(
        [Parameter(Mandatory)]
        [string] $ScriptPath,
        [Parameter(Mandatory)]
        [hashtable] $Parameters
    )

    & $ScriptPath @Parameters
    if ($LASTEXITCODE -ne 0) {
        throw "Script failed with exit code ${LASTEXITCODE}: $ScriptPath"
    }
}

function Remove-ReleaseSymbols {
    param(
        [Parameter(Mandatory)]
        [string] $RootPath
    )

    if ($IncludeSymbols) {
        return
    }

    Get-ChildItem -Path $RootPath -Recurse -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Extension -eq '.pdb' -or $_.Extension -eq '.wixpdb' } |
        Remove-Item -Force -ErrorAction SilentlyContinue
}

function Write-Sha256Manifest {
    param(
        [Parameter(Mandatory)]
        [string] $RootPath,
        [Parameter(Mandatory)]
        [string] $OutputPath
    )

    $files = Get-ChildItem -Path $RootPath -File -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -ne $OutputPath } |
        Sort-Object FullName

    $lines = [System.Collections.Generic.List[string]]::new()
    foreach ($file in $files) {
        $hash = Get-FileHash -Algorithm SHA256 -Path $file.FullName
        $relative = [System.IO.Path]::GetRelativePath($RootPath, $file.FullName)
        $lines.Add(("{0} *{1}" -f $hash.Hash.ToLowerInvariant(), $relative.Replace('\\', '/')))
    }
    Set-Content -Path $OutputPath -Value $lines -Encoding UTF8
}

$script:RepoRoot = (Get-Item (Split-Path -Parent $MyInvocation.MyCommand.Path)).Parent.FullName

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
$pluginsScript = Join-Path $script:RepoRoot 'Build\Publish-Plugins.ps1'
$portableScript = Join-Path $script:RepoRoot 'Build\Package-Portable.ps1'
$installerScript = Join-Path $script:RepoRoot 'Build\Build-Installer.ps1'

$nugetOut = Join-Path $OutDir 'nuget'
$portableOut = Join-Path $OutDir 'portable'
$installerOut = Join-Path $OutDir 'installer'
$bundleName = "IntelligenceX.Chat-$ReleaseId-$Runtime"
$payloadPath = Join-Path $portableOut $bundleName

Write-Header 'Build Release'
Write-Step "Release ID: $ReleaseId"
Write-Step "Runtime: $Runtime"
Write-Step "Framework: $Framework"
Write-Step "Output root: $OutDir"
Write-Step "Include symbols: $([bool]$IncludeSymbols)"

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
    Invoke-ScriptFile -ScriptPath $workspaceScript -Parameters $workspaceArgs
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
    Invoke-ScriptFile -ScriptPath $pluginsScript -Parameters $pluginArgs
}

if (-not $SkipPortable) {
    Write-Header 'Package Portable Bundle'
    $portableArgs = @{
        Runtime = $Runtime
        Configuration = $Configuration
        Framework = $Framework
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
    Invoke-ScriptFile -ScriptPath $portableScript -Parameters $portableArgs
}

if (-not $SkipInstaller) {
    Write-Header 'Build MSI Installer'
    $installerArgs = @{
        Runtime = $Runtime
        Configuration = $Configuration
        Framework = $Framework
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
    Invoke-ScriptFile -ScriptPath $installerScript -Parameters $installerArgs
}

Remove-ReleaseSymbols -RootPath $OutDir

$manifest = [ordered]@{
    schemaVersion = 1
    releaseId = $ReleaseId
    createdUtc = (Get-Date).ToUniversalTime().ToString('o')
    runtime = $Runtime
    framework = $Framework
    configuration = $Configuration
    includeService = [bool] $IncludeService
    includeSymbols = [bool] $IncludeSymbols
    outputRoot = $OutDir
    artifacts = [ordered]@{
        nuget = if (Test-Path $nugetOut) {
            Get-ChildItem -Path $nugetOut -Filter '*.nupkg' -File -ErrorAction SilentlyContinue |
                Sort-Object Name |
                ForEach-Object { $_.FullName }
        } else { @() }
        portable = if (Test-Path $portableOut) {
            Get-ChildItem -Path $portableOut -File -Recurse -ErrorAction SilentlyContinue |
                Sort-Object FullName |
                ForEach-Object { $_.FullName }
        } else { @() }
        installer = if (Test-Path $installerOut) {
            Get-ChildItem -Path $installerOut -File -Recurse -ErrorAction SilentlyContinue |
                Sort-Object FullName |
                ForEach-Object { $_.FullName }
        } else { @() }
    }
}
$manifestPath = Join-Path $OutDir 'release-manifest.json'
$manifest | ConvertTo-Json -Depth 6 | Set-Content -Path $manifestPath -Encoding UTF8

if (-not $SkipChecksums) {
    Write-Header 'Generate Checksums'
    $checksumPath = Join-Path $OutDir 'SHA256SUMS.txt'
    Write-Sha256Manifest -RootPath $OutDir -OutputPath $checksumPath
    Write-Ok "Checksums: $checksumPath"
}

Write-Ok "Release artifacts ready: $OutDir"



