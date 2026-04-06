# Publish the Chat host and validate that bundled/private tool-pack assets are present.

[CmdletBinding()] param(
    [ValidateSet('win-x64','win-arm64')]
    [string] $Runtime = 'win-x64',

    [ValidateSet('Debug','Release')]
    [string] $Configuration = 'Release',

    [string] $Framework = 'net10.0-windows',
    [string] $OutDir,
    [switch] $SelfContained = $true,
    [switch] $SingleFile,
    [switch] $NoBuild,
    [switch] $IncludePrivateToolPacks,
    [string] $TestimoXRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Header($text) { Write-Host "`n=== $text ===" -ForegroundColor Cyan }
function Write-Step($text)   { Write-Host "[+] $text" -ForegroundColor Yellow }
function Write-Ok($text)     { Write-Host "[OK] $text" -ForegroundColor Green }

function Invoke-DotNet {
    param(
        [Parameter(Mandatory)]
        [string[]] $Args,
        [string] $WorkingDirectory
    )

    if ([string]::IsNullOrWhiteSpace($WorkingDirectory)) {
        $WorkingDirectory = $script:RepoRoot
    }

    Push-Location $WorkingDirectory
    try {
        & dotnet @Args
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet command failed with exit code ${LASTEXITCODE}: dotnet $($Args -join ' ')"
        }
    } finally {
        Pop-Location
    }
}

$script:RepoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$hostProject = Join-Path $script:RepoRoot 'IntelligenceX.Chat\IntelligenceX.Chat.Host\IntelligenceX.Chat.Host.csproj'
. (Join-Path $script:RepoRoot 'Build\Internal\Assert-ChatHostArtifacts.ps1')
. (Join-Path $script:RepoRoot 'Build\Internal\Publish-ChatBundledToolProjects.ps1')

if ($SingleFile) {
    Write-Host "[!] Single-file publish is not supported for Chat.Host packaging; keeping loose tool-pack/runtime assemblies instead." -ForegroundColor DarkYellow
    $SingleFile = $false
}

if (-not (Test-Path $hostProject)) {
    throw "Host project not found: $hostProject"
}

if ([string]::IsNullOrWhiteSpace($OutDir)) {
    $suffix = if ($IncludePrivateToolPacks) { 'private' } else { 'public' }
    $OutDir = Join-Path $script:RepoRoot ("Artifacts\ChatHostPublish\{0}-{1}" -f $Runtime, $suffix)
}

if (Test-Path $OutDir) {
    Remove-Item -Recurse -Force $OutDir
}
New-Item -ItemType Directory -Path $OutDir -Force | Out-Null

$publishArgs = @(
    'publish',
    $hostProject,
    '-c',
    $Configuration,
    '-f',
    $Framework,
    '-r',
    $Runtime,
    '-o',
    $OutDir
)
if ($SelfContained) {
    $publishArgs += '--self-contained'
} else {
    $publishArgs += '--no-self-contained'
}
if ($SingleFile) {
    $publishArgs += '/p:PublishSingleFile=true'
}
if ($NoBuild) {
    $publishArgs += '--no-build'
}
if (-not [string]::IsNullOrWhiteSpace($TestimoXRoot)) {
    $resolved = [System.IO.Path]::GetFullPath($TestimoXRoot)
    if (-not $resolved.EndsWith([System.IO.Path]::DirectorySeparatorChar)) {
        $resolved += [System.IO.Path]::DirectorySeparatorChar
    }
    $publishArgs += "/p:TestimoXRoot=$resolved"
}

Write-Header 'Publish Chat Host'
Write-Step "Project: $hostProject"
Write-Step "Runtime: $Runtime"
Write-Step "Configuration: $Configuration"
Write-Step "Framework: $Framework"
Write-Step "Self-contained: $([bool]$SelfContained)"
Write-Step "Single-file: $([bool]$SingleFile)"
Write-Step "Include private tool packs: $([bool]$IncludePrivateToolPacks)"
Write-Step "Output: $OutDir"

Invoke-DotNet -Args $publishArgs -WorkingDirectory $script:RepoRoot
Publish-ChatBundledToolProjects `
    -RepoRoot $script:RepoRoot `
    -OutputPath $OutDir `
    -Configuration $Configuration `
    -Runtime $Runtime `
    -NoBuild:$NoBuild `
    -IncludePrivateToolPacks:$IncludePrivateToolPacks `
    -TestimoXRoot $TestimoXRoot

Write-Header 'Validate Published Artifacts'
Assert-ChatHostArtifacts -RootPath $OutDir -IncludePrivateToolPacks:$IncludePrivateToolPacks

Write-Ok "Chat host published and validated: $OutDir"
