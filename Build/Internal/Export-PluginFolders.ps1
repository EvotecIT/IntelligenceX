# Export tool packs as folder-based plugins (main assembly + dependencies + manifest).
# Release exports strip .pdb files by default unless -IncludeSymbols is specified.

[CmdletBinding()] param(
    [ValidateSet('public','private','all')]
    [string] $Mode = 'public',

    [ValidateSet('Debug','Release')]
    [string] $Configuration = 'Release',

    [string] $Framework = 'net10.0-windows',
    [string] $OutDir,
    [string] $TestimoXRoot,
    [switch] $IncludeSymbols
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

function Get-ProjectFrameworks {
    param([xml] $Xml)

    $frameworkNode = $Xml.SelectSingleNode('//Project/PropertyGroup/TargetFramework')
    if ($frameworkNode -and -not [string]::IsNullOrWhiteSpace($frameworkNode.InnerText)) {
        return @($frameworkNode.InnerText.Trim())
    }

    $frameworksNode = $Xml.SelectSingleNode('//Project/PropertyGroup/TargetFrameworks')
    if ($null -eq $frameworksNode -or [string]::IsNullOrWhiteSpace($frameworksNode.InnerText)) {
        return @()
    }

    return $frameworksNode.InnerText.Split(';', [System.StringSplitOptions]::RemoveEmptyEntries) |
        ForEach-Object { $_.Trim() } |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
}

$script:RepoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
. (Join-Path $script:RepoRoot 'Build\Internal\Resolve-TestimoXRoot.ps1')

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

function Remove-PluginSymbols {
    param(
        [Parameter(Mandatory)]
        [string] $PluginDirectory
    )

    if ($IncludeSymbols) {
        return
    }

    $symbolFiles = Get-ChildItem -Path $PluginDirectory -File -Filter '*.pdb' -ErrorAction SilentlyContinue
    foreach ($symbolFile in $symbolFiles) {
        Remove-Item -Force $symbolFile.FullName
    }
}

function Get-PluginEntryType {
    param(
        [Parameter(Mandatory)]
        [string] $ProjectPath
    )

    $projectDir = Split-Path -Parent $ProjectPath
    $candidates = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::Ordinal)
    $sourceFiles = Get-ChildItem -Path $projectDir -Recurse -Filter '*.cs' -File -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -notmatch '(\\|/)(bin|obj)(\\|/)' }

    foreach ($sourceFile in $sourceFiles) {
        $content = Get-Content -Path $sourceFile.FullName -Raw
        if ([string]::IsNullOrWhiteSpace($content)) {
            continue
        }

        $namespaceMatch = [regex]::Match($content, '(?m)^\s*namespace\s+([A-Za-z_][A-Za-z0-9_.]*)\s*[;{]')
        $namespaceName = if ($namespaceMatch.Success) { $namespaceMatch.Groups[1].Value.Trim() } else { '' }

        $typeMatches = [regex]::Matches($content, '(?m)^\s*(?:public|internal|protected|private|sealed|abstract|partial|static|\s)*class\s+([A-Za-z_][A-Za-z0-9_]*)\s*:\s*([^\r\n{]+)')
        foreach ($typeMatch in $typeMatches) {
            $className = $typeMatch.Groups[1].Value.Trim()
            $inheritanceList = $typeMatch.Groups[2].Value
            if ([string]::IsNullOrWhiteSpace($className) -or [string]::IsNullOrWhiteSpace($inheritanceList)) {
                continue
            }

            if ($inheritanceList -notmatch '(^|[\s,])IToolPack($|[\s,])') {
                continue
            }

            $fullTypeName = if ([string]::IsNullOrWhiteSpace($namespaceName)) {
                $className
            } else {
                "$namespaceName.$className"
            }
            $null = $candidates.Add($fullTypeName)
        }
    }

    if ($candidates.Count -ne 1) {
        $candidateList = @($candidates) -join ', '
        throw "Expected exactly one IToolPack implementation in '$ProjectPath', found $($candidates.Count): $candidateList"
    }

    return @($candidates)[0]
}

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

Write-Header 'Export Plugin Folders'
Write-Step "Mode: $Mode"
Write-Step "Framework: $Framework"
Write-Step "Output: $OutDir"
Write-Step "Include symbols: $([bool]$IncludeSymbols)"

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

    $assemblyName = if ($null -eq $assemblyNameNode -or [string]::IsNullOrWhiteSpace($assemblyNameNode.InnerText)) {
        [System.IO.Path]::GetFileNameWithoutExtension($projectPath)
    } else {
        $assemblyNameNode.InnerText.Trim()
    }
    $packageId = if ($null -eq $packageIdNode -or [string]::IsNullOrWhiteSpace($packageIdNode.InnerText)) { $assemblyName } else { $packageIdNode.InnerText.Trim() }
    $entryType = Get-PluginEntryType -ProjectPath $projectPath
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
    Remove-PluginSymbols -PluginDirectory $pluginDir

    $manifestPath = Join-Path $pluginDir 'ix-plugin.json'
    $sourceKind = if ($privateProjects -contains $project) { 'closed_source' } else { 'open_source' }
    $manifest = [ordered]@{
        schemaVersion = 1
        pluginId = $packageId.ToLowerInvariant()
        displayName = $packageId
        packageId = $packageId
        sourceKind = $sourceKind
        entryAssembly = "$assemblyName.dll"
        entryType = $entryType
        mode = $Mode
        framework = $resolvedFramework
    }
    $manifestJson = $manifest | ConvertTo-Json -Depth 5
    Set-Content -Path $manifestPath -Value $manifestJson -Encoding UTF8
}

Write-Ok 'Plugin folder export complete.'
