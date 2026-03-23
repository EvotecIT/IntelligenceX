# Pack and optionally publish tool-pack plugins.

[CmdletBinding()] param(
    [ValidateSet('public','private','all')]
    [string] $Mode = 'public',

    [ValidateSet('Debug','Release')]
    [string] $Configuration = 'Release',

    [string] $OutDir,
    [string] $VersionSuffix,
    [string] $PackageVersion,
    [switch] $NoBuild,
    [switch] $IncludeSymbols,
    [string] $TestimoXRoot,

    [switch] $Push,
    [string] $Source,
    [string] $ApiKey,
    [switch] $SkipDuplicate = $true
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
. (Join-Path $script:RepoRoot 'Build\Internal\Resolve-TestimoXRoot.ps1')

if ([string]::IsNullOrWhiteSpace($OutDir)) {
    $OutDir = Join-Path $script:RepoRoot 'Artifacts\NuGet'
}
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$publicProjects = @(
    'IntelligenceX.Tools\IntelligenceX.Tools.Common\IntelligenceX.Tools.Common.csproj',
    'IntelligenceX.Tools\IntelligenceX.Tools.FileSystem\IntelligenceX.Tools.FileSystem.csproj',
    'IntelligenceX.Tools\IntelligenceX.Tools.Email\IntelligenceX.Tools.Email.csproj',
    'IntelligenceX.Tools\IntelligenceX.Tools.EventLog\IntelligenceX.Tools.EventLog.csproj',
    'IntelligenceX.Tools\IntelligenceX.Tools.PowerShell\IntelligenceX.Tools.PowerShell.csproj',
    'IntelligenceX.Tools\IntelligenceX.Tools.ReviewerSetup\IntelligenceX.Tools.ReviewerSetup.csproj'
)

$privateProjects = @(
    'IntelligenceX.Tools\IntelligenceX.Tools.System\IntelligenceX.Tools.System.csproj',
    'IntelligenceX.Tools\IntelligenceX.Tools.ADPlayground\IntelligenceX.Tools.ADPlayground.csproj',
    'IntelligenceX.Tools\IntelligenceX.Tools.TestimoX\IntelligenceX.Tools.TestimoX.csproj'
)

$selected = switch ($Mode) {
    'public' { @($publicProjects) }
    'private' { @($privateProjects) }
    'all' { @($publicProjects + $privateProjects | Select-Object -Unique) }
    default { throw "Unexpected mode: $Mode" }
}

$needsPrivateRoot = $selected | Where-Object { $privateProjects -contains $_ } | Select-Object -First 1
$privateArg = $null
if ($needsPrivateRoot) {
    $resolvedTestimoXRoot = Resolve-TestimoXRoot -Provided $TestimoXRoot -RepoRoot $script:RepoRoot
    $privateArg = "/p:TestimoXRoot=$resolvedTestimoXRoot"
}

Write-Header 'Pack Plugins'
Write-Step "Mode: $Mode"
Write-Step "Output: $OutDir"

foreach ($project in $selected) {
    Write-Step "Pack: $project"
    $packArgs = @(
        'pack',
        $project,
        '-c',
        $Configuration,
        '-o',
        $OutDir,
        '/p:ContinuousIntegrationBuild=true'
    )

    if ($NoBuild) {
        $packArgs += '--no-build'
    }
    if (-not [string]::IsNullOrWhiteSpace($PackageVersion)) {
        $packArgs += "/p:PackageVersion=$PackageVersion"
    } elseif (-not [string]::IsNullOrWhiteSpace($VersionSuffix)) {
        $packArgs += "/p:VersionSuffix=$VersionSuffix"
    }
    if ($IncludeSymbols) {
        $packArgs += '/p:IncludeSymbols=true'
        $packArgs += '/p:SymbolPackageFormat=snupkg'
    }
    if ($privateArg -and ($privateProjects -contains $project)) {
        $packArgs += $privateArg
    }

    Invoke-DotNet -Args $packArgs -WorkingDirectory $script:RepoRoot
}

$producedPackages = Get-ChildItem -Path $OutDir -File -Filter '*.nupkg' |
    Where-Object { $_.Name -notlike '*.snupkg' -and $_.Name -notlike '*.symbols.nupkg' } |
    Sort-Object Name

if ($producedPackages.Count -eq 0) {
    throw "No NuGet packages were produced in $OutDir."
}

Write-Header 'Produced Packages'
foreach ($pkg in $producedPackages) {
    Write-Host "- $($pkg.Name)"
}

if ($Push) {
    if ([string]::IsNullOrWhiteSpace($Source)) {
        throw "When -Push is specified, provide -Source."
    }
    if ([string]::IsNullOrWhiteSpace($ApiKey)) {
        throw "When -Push is specified, provide -ApiKey."
    }

    Write-Header 'Push Packages'
    foreach ($pkg in $producedPackages) {
        Write-Step "Push: $($pkg.Name)"
        $pushArgs = @('nuget', 'push', $pkg.FullName, '--source', $Source, '--api-key', $ApiKey)
        if ($SkipDuplicate) {
            $pushArgs += '--skip-duplicate'
        }
        Invoke-DotNet -Args $pushArgs -WorkingDirectory $script:RepoRoot
    }
}

Write-Ok 'Plugin packaging complete.'
