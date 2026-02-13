# Export tool packs as folder-based plugins (main assembly + dependencies + manifest).

[CmdletBinding()] param(
    [ValidateSet('public','private','all')]
    [string] $Mode = 'public',

    [ValidateSet('Debug','Release')]
    [string] $Configuration = 'Release',

    [string] $Framework = 'net10.0-windows',
    [string] $OutDir,
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

    throw "Unable to locate TestimoX private engines. Pass -TestimoXRoot explicitly for private plugin export."
}

function Get-ProjectFrameworks {
    param([xml] $Xml)

    $frameworkNode = $Xml.SelectSingleNode('//Project/PropertyGroup/TargetFramework')
    if ($frameworkNode -and -not [string]::IsNullOrWhiteSpace($frameworkNode.InnerText)) {
        return @($frameworkNode.InnerText.Trim())
    }

    $frameworksNode = $Xml.SelectSingleNode('//Project/PropertyGroup/TargetFrameworks')
    if ($frameworksNode -eq $null -or [string]::IsNullOrWhiteSpace($frameworksNode.InnerText)) {
        return @()
    }

    return $frameworksNode.InnerText.Split(';', [System.StringSplitOptions]::RemoveEmptyEntries) |
        ForEach-Object { $_.Trim() } |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
}

function Resolve-FrameworkForProject {
    param(
        [string[]] $ProjectFrameworks,
        [string] $Preferred
    )

    if ($ProjectFrameworks.Count -eq 0) {
        return $Preferred
    }
    if ($ProjectFrameworks -contains $Preferred) {
        return $Preferred
    }

    $preferredCore = $Preferred -replace '-windows$', ''
    if ($ProjectFrameworks -contains $preferredCore) {
        return $preferredCore
    }

    if ($Preferred.EndsWith('-windows', [System.StringComparison]::OrdinalIgnoreCase)) {
        $windowsPreferred = $ProjectFrameworks | Where-Object { $_ -like '*-windows' } | Select-Object -First 1
        if ($windowsPreferred) {
            return $windowsPreferred
        }
    }

    return $ProjectFrameworks[0]
}

$script:RepoRoot = (Get-Item (Split-Path -Parent $MyInvocation.MyCommand.Path)).Parent.FullName

if ([string]::IsNullOrWhiteSpace($OutDir)) {
    $OutDir = Join-Path $script:RepoRoot 'Artifacts\Plugins'
}
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$publicProjects = @(
    'IntelligenceX.Tools\IntelligenceX.Tools.FileSystem\IntelligenceX.Tools.FileSystem.csproj',
    'IntelligenceX.Tools\IntelligenceX.Tools.PowerShell\IntelligenceX.Tools.PowerShell.csproj',
    'IntelligenceX.Tools\IntelligenceX.Tools.EventLog\IntelligenceX.Tools.EventLog.csproj',
    'IntelligenceX.Tools\IntelligenceX.Tools.Email\IntelligenceX.Tools.Email.csproj',
    'IntelligenceX.Tools\IntelligenceX.Tools.ReviewerSetup\IntelligenceX.Tools.ReviewerSetup.csproj'
)

$privateProjects = @(
    'IntelligenceX.Tools\IntelligenceX.Tools.System\IntelligenceX.Tools.System.csproj',
    'IntelligenceX.Tools\IntelligenceX.Tools.ActiveDirectory\IntelligenceX.Tools.ActiveDirectory.csproj',
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

Write-Header 'Export Plugin Folders'
Write-Step "Mode: $Mode"
Write-Step "Framework: $Framework"
Write-Step "Output: $OutDir"

foreach ($project in $selected) {
    $projectPath = Join-Path $script:RepoRoot $project
    if (-not (Test-Path $projectPath)) {
        throw "Project not found: $projectPath"
    }

    [xml] $xml = Get-Content $projectPath
    $assemblyNameNode = $xml.SelectSingleNode('//Project/PropertyGroup/AssemblyName')
    $packageIdNode = $xml.SelectSingleNode('//Project/PropertyGroup/PackageId')
    $projectFrameworks = Get-ProjectFrameworks -Xml $xml
    $resolvedFramework = Resolve-FrameworkForProject -ProjectFrameworks $projectFrameworks -Preferred $Framework

    $assemblyName = if ($assemblyNameNode -eq $null -or [string]::IsNullOrWhiteSpace($assemblyNameNode.InnerText)) {
        [System.IO.Path]::GetFileNameWithoutExtension($projectPath)
    } else {
        $assemblyNameNode.InnerText.Trim()
    }
    $packageId = if ($packageIdNode -eq $null -or [string]::IsNullOrWhiteSpace($packageIdNode.InnerText)) { $assemblyName } else { $packageIdNode.InnerText.Trim() }
    $pluginDir = Join-Path $OutDir $packageId

    Write-Step "Publish plugin folder: $packageId ($resolvedFramework)"
    if (Test-Path $pluginDir) {
        Remove-Item -Recurse -Force $pluginDir
    }
    New-Item -ItemType Directory -Force -Path $pluginDir | Out-Null

    $publishArgs = @(
        'publish',
        $project,
        '-c',
        $Configuration,
        '-f',
        $resolvedFramework,
        '-o',
        $pluginDir,
        '/p:UseAppHost=false'
    )
    if ($privateArg -and ($privateProjects -contains $project)) {
        $publishArgs += $privateArg
    }
    Invoke-DotNet -Args $publishArgs -WorkingDirectory $script:RepoRoot

    $manifestPath = Join-Path $pluginDir 'ix-plugin.json'
    $manifest = [ordered]@{
        schemaVersion = 1
        pluginId = $packageId.ToLowerInvariant()
        displayName = $packageId
        packageId = $packageId
        entryAssembly = "$assemblyName.dll"
        mode = $Mode
        framework = $resolvedFramework
    }
    $manifestJson = $manifest | ConvertTo-Json -Depth 5
    Set-Content -Path $manifestPath -Value $manifestJson -Encoding UTF8
}

Write-Ok 'Plugin folder export complete.'
