# Build workspace using explicit profiles for OSS and private tool development.

[CmdletBinding()] param(
    [ValidateSet('oss','full-private')]
    [string] $Profile = 'oss',

    [ValidateSet('Debug','Release')]
    [string] $Configuration = 'Release',

    [switch] $SkipTests,
    [switch] $SkipHarness,
    [switch] $IncludePublicTools = $true,
    [switch] $IncludeChat,
    [string] $TestimoXRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Header($text) { Write-Host "`n=== $text ===" -ForegroundColor Cyan }
function Write-Step($text)   { Write-Host "[+] $text" -ForegroundColor Yellow }
function Write-Ok($text)     { Write-Host "[OK] $text" -ForegroundColor Green }
function Write-Warn($text)   { Write-Host "[!] $text" -ForegroundColor DarkYellow }

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

function Ensure-TrailingSlash {
    param([Parameter(Mandatory)][string] $Path)
    $full = [System.IO.Path]::GetFullPath($Path)
    if ($full.EndsWith([System.IO.Path]::DirectorySeparatorChar)) {
        return $full
    }
    return ($full + [System.IO.Path]::DirectorySeparatorChar)
}

function Test-TestimoXMarkers {
    param([Parameter(Mandatory)][string] $Root)

    $full = [System.IO.Path]::GetFullPath($Root)
    $markers = @(
        (Join-Path $full 'ADPlayground\ADPlayground.csproj'),
        (Join-Path $full 'ComputerX\Features\FeatureInventoryQuery.cs'),
        (Join-Path $full 'ComputerX\PowerShellRuntime\PowerShellCommandQuery.cs')
    )

    foreach ($marker in $markers) {
        if (-not (Test-Path $marker)) {
            return $false
        }
    }
    return $true
}

function Resolve-TestimoXRoot {
    param(
        [string] $Provided,
        [Parameter(Mandatory)]
        [string] $RepoRoot
    )

    if (-not [string]::IsNullOrWhiteSpace($Provided)) {
        if (-not (Test-TestimoXMarkers -Root $Provided)) {
            throw "Provided -TestimoXRoot does not contain required markers: $Provided"
        }
        return (Ensure-TrailingSlash -Path $Provided)
    }

    $candidates = @(
        (Join-Path $RepoRoot '..\TestimoX'),
        (Join-Path $RepoRoot '..\TestimoX-master'),
        (Join-Path $RepoRoot '..\..\TestimoX'),
        (Join-Path $RepoRoot '..\..\TestimoX-master')
    )

    foreach ($candidate in $candidates) {
        if (Test-TestimoXMarkers -Root $candidate) {
            return (Ensure-TrailingSlash -Path $candidate)
        }
    }

    throw "Unable to locate TestimoX private engines. Pass -TestimoXRoot explicitly for -Profile full-private."
}

$script:RepoRoot = (Get-Item (Split-Path -Parent $MyInvocation.MyCommand.Path)).Parent.FullName

$publicToolProjects = @(
    'IntelligenceX.Tools\IntelligenceX.Tools.Common\IntelligenceX.Tools.Common.csproj',
    'IntelligenceX.Tools\IntelligenceX.Tools.FileSystem\IntelligenceX.Tools.FileSystem.csproj',
    'IntelligenceX.Tools\IntelligenceX.Tools.Email\IntelligenceX.Tools.Email.csproj',
    'IntelligenceX.Tools\IntelligenceX.Tools.EventLog\IntelligenceX.Tools.EventLog.csproj',
    'IntelligenceX.Tools\IntelligenceX.Tools.PowerShell\IntelligenceX.Tools.PowerShell.csproj',
    'IntelligenceX.Tools\IntelligenceX.Tools.ReviewerSetup\IntelligenceX.Tools.ReviewerSetup.csproj'
)

$privateToolProjects = @(
    'IntelligenceX.Tools\IntelligenceX.Tools.System\IntelligenceX.Tools.System.csproj',
    'IntelligenceX.Tools\IntelligenceX.Tools.ADPlayground\IntelligenceX.Tools.ADPlayground.csproj',
    'IntelligenceX.Tools\IntelligenceX.Tools.TestimoX\IntelligenceX.Tools.TestimoX.csproj'
)
$resolvedTestimoXRoot = $null

Write-Header 'Build Workspace'
Write-Step "Profile: $Profile"
Write-Step "Configuration: $Configuration"

Write-Header 'CI Baseline'
Invoke-DotNet -Args @('build', 'IntelligenceX.CI.slnf', '-c', $Configuration) -WorkingDirectory $script:RepoRoot
if (-not $SkipTests) {
    Invoke-DotNet -Args @('test', 'IntelligenceX.CI.slnf', '-c', $Configuration, '--no-build') -WorkingDirectory $script:RepoRoot
}

if (-not $SkipHarness) {
    Write-Header 'Executable Harness'
    foreach ($tfm in @('net8.0', 'net10.0')) {
        $harnessDll = Join-Path $script:RepoRoot ("IntelligenceX.Tests\bin\{0}\{1}\IntelligenceX.Tests.dll" -f $Configuration, $tfm)
        if (-not (Test-Path $harnessDll)) {
            Write-Warn "Harness not found for ${tfm}: $harnessDll"
            continue
        }
        Write-Step "Running harness: $tfm"
        Invoke-DotNet -Args @($harnessDll) -WorkingDirectory $script:RepoRoot
    }
}

if ($IncludePublicTools) {
    Write-Header 'Public Tool Packs'
    foreach ($project in $publicToolProjects) {
        Write-Step "Build: $project"
        Invoke-DotNet -Args @('build', $project, '-c', $Configuration) -WorkingDirectory $script:RepoRoot
    }
}

if ($Profile -eq 'full-private') {
    Write-Header 'Private Tool Packs'
    $resolvedTestimoXRoot = Resolve-TestimoXRoot -Provided $TestimoXRoot -RepoRoot $script:RepoRoot
    Write-Step "TestimoX root: $resolvedTestimoXRoot"
    $privateArg = "/p:TestimoXRoot=$resolvedTestimoXRoot"

    foreach ($project in $privateToolProjects) {
        Write-Step "Build: $project"
        Invoke-DotNet -Args @('build', $project, '-c', $Configuration, $privateArg) -WorkingDirectory $script:RepoRoot
    }

    if (-not $SkipTests) {
        Write-Step 'Run IntelligenceX.Tools.Tests'
        Invoke-DotNet -Args @(
            'test',
            'IntelligenceX.Tools\IntelligenceX.Tools.Tests\IntelligenceX.Tools.Tests.csproj',
            '-c',
            $Configuration,
            '--no-build',
            $privateArg
        ) -WorkingDirectory $script:RepoRoot
    }
}

if ($IncludeChat) {
    Write-Header 'Chat Build'
    $chatArgs = @('build', 'IntelligenceX.Chat\IntelligenceX.Chat.sln', '-c', $Configuration)
    if ($Profile -eq 'full-private') {
        if ([string]::IsNullOrWhiteSpace($resolvedTestimoXRoot)) {
            $resolvedTestimoXRoot = Resolve-TestimoXRoot -Provided $TestimoXRoot -RepoRoot $script:RepoRoot
        }
        $chatArgs += '/p:IncludePrivateToolPacks=true'
        $chatArgs += "/p:TestimoXRoot=$resolvedTestimoXRoot"
    }
    Invoke-DotNet -Args $chatArgs -WorkingDirectory $script:RepoRoot
}

Write-Ok 'Workspace build completed.'
