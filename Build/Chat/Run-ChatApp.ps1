# One-command local WinUI chat app startup (build-root entrypoint).

[CmdletBinding()] param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',
    [switch] $NoBuild,
    [switch] $IncludePrivateToolPacks,
    [string] $TestimoXRoot
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Header($text) { Write-Host "`n=== $text ===" -ForegroundColor Cyan }
function Write-Step($text)   { Write-Host "[+] $text" -ForegroundColor Yellow }

function Stop-IfRunning {
    param([string[]] $Names)

    foreach ($name in $Names) {
        $procs = Get-Process -Name $name -ErrorAction SilentlyContinue
        if (-not $procs) {
            continue
        }

        foreach ($p in $procs) {
            try {
                Stop-Process -Id $p.Id -Force -ErrorAction Stop
            } catch {
                Write-Warning "Could not stop process '$name' (pid $($p.Id)): $($_.Exception.Message)"
            }
        }
    }
}

function Resolve-ChatAppServiceOutputPath {
    param(
        [Parameter(Mandatory)]
        [string] $AppProjectPath,
        [Parameter(Mandatory)]
        [string] $Configuration
    )

    $appProjectDirectory = Split-Path -Parent $AppProjectPath
    $binRoot = Join-Path $appProjectDirectory ("bin\" + $Configuration)
    if (-not (Test-Path $binRoot)) {
        return $null
    }

    $candidates = Get-ChildItem $binRoot -Directory -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -eq 'service' -and (Test-Path (Join-Path $_.FullName 'IntelligenceX.Chat.Service.dll')) } |
        ForEach-Object {
            $pathSegments = $_.FullName -split '[\\/]'
            [pscustomobject]@{
                FullName = $_.FullName
                IsPublishPath = $pathSegments -contains 'publish'
                Depth = $pathSegments.Count
            }
        } |
        Sort-Object @{ Expression = 'IsPublishPath'; Descending = $false }, @{ Expression = 'Depth'; Descending = $false }, FullName

    foreach ($candidate in $candidates) {
        return $candidate.FullName
    }

    return $null
}

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$appProject = Join-Path $repoRoot 'IntelligenceX.Chat\IntelligenceX.Chat.App\IntelligenceX.Chat.App.csproj'
$officeImoRoot = Join-Path (Split-Path $repoRoot -Parent) 'OfficeIMO'
$resolvedOfficeImoRoot = $null
. (Join-Path $repoRoot 'Build\Internal\Publish-ChatBundledToolProjects.ps1')

if (-not (Test-Path $appProject)) {
    throw "Project not found: $appProject"
}

# Prevent file lock issues during build/run.
Stop-IfRunning -Names @('IntelligenceX.Chat.App', 'IntelligenceX.Chat.Service')

$dotnetRunArgs = @(
    'run',
    '--project', $appProject,
    '-c', $Configuration
)

if ($NoBuild) {
    $dotnetRunArgs += '--no-build'
}

if (Test-Path (Join-Path $officeImoRoot 'OfficeIMO.MarkdownRenderer\OfficeIMO.MarkdownRenderer.csproj')) {
    $resolvedOfficeImoRoot = [System.IO.Path]::GetFullPath($officeImoRoot)
    if (-not $resolvedOfficeImoRoot.EndsWith([System.IO.Path]::DirectorySeparatorChar)) {
        $resolvedOfficeImoRoot += [System.IO.Path]::DirectorySeparatorChar
    }
    $dotnetRunArgs += "/p:UseLocalOfficeImoCheckout=true"
    $dotnetRunArgs += "/p:OfficeImoRepoRoot=$resolvedOfficeImoRoot"
}

if ($IncludePrivateToolPacks) {
    $dotnetRunArgs += '/p:IncludePrivateToolPacks=true'
    if (-not [string]::IsNullOrWhiteSpace($TestimoXRoot)) {
        $resolved = [System.IO.Path]::GetFullPath($TestimoXRoot)
        if (-not $resolved.EndsWith([System.IO.Path]::DirectorySeparatorChar)) {
            $resolved += [System.IO.Path]::DirectorySeparatorChar
        }
        $dotnetRunArgs += "/p:TestimoXRoot=$resolved"
    }
}

Write-Header 'Run Chat App'
Write-Step "Configuration: $Configuration"
Write-Step "Project: $appProject"
if ($resolvedOfficeImoRoot) {
    Write-Step "OfficeIMO: local checkout ($resolvedOfficeImoRoot)"
} else {
    Write-Step 'OfficeIMO: package fallback'
}

Push-Location $repoRoot
try {
    if (-not $NoBuild) {
        $dotnetBuildArgs = @(
            'build',
            $appProject,
            '-c',
            $Configuration
        )
        if ($resolvedOfficeImoRoot) {
            $dotnetBuildArgs += "/p:UseLocalOfficeImoCheckout=true"
            $dotnetBuildArgs += "/p:OfficeImoRepoRoot=$resolvedOfficeImoRoot"
        }
        if ($IncludePrivateToolPacks) {
            $dotnetBuildArgs += '/p:IncludePrivateToolPacks=true'
            if (-not [string]::IsNullOrWhiteSpace($TestimoXRoot)) {
                $resolved = [System.IO.Path]::GetFullPath($TestimoXRoot)
                if (-not $resolved.EndsWith([System.IO.Path]::DirectorySeparatorChar)) {
                    $resolved += [System.IO.Path]::DirectorySeparatorChar
                }
                $dotnetBuildArgs += "/p:TestimoXRoot=$resolved"
            }
        }

        Write-Step 'Build app and sidecar before local run'
        & dotnet @dotnetBuildArgs
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet build failed with exit code $LASTEXITCODE."
        }
    }

    $serviceOutputPath = Resolve-ChatAppServiceOutputPath -AppProjectPath $appProject -Configuration $Configuration
    if ([string]::IsNullOrWhiteSpace($serviceOutputPath)) {
        throw "Unable to resolve Chat service output under the app build output."
    }

    Write-Step "Bundle tool projects into service sidecar: $serviceOutputPath"
    Publish-ChatBundledToolProjects `
        -RepoRoot $repoRoot `
        -OutputPath $serviceOutputPath `
        -Configuration $Configuration `
        -Runtime 'win-x64' `
        -NoBuild:$NoBuild `
        -IncludePrivateToolPacks:$IncludePrivateToolPacks `
        -TestimoXRoot $TestimoXRoot

    if (-not $dotnetRunArgs.Contains('--no-build')) {
        $dotnetRunArgs += '--no-build'
    }

    & dotnet @dotnetRunArgs
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet run failed with exit code $LASTEXITCODE."
    }
} finally {
    Pop-Location
}
