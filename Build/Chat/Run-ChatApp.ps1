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

    $appOutputCandidates = Get-ChildItem $binRoot -Filter 'IntelligenceX.Chat.App.dll' -File -Recurse -ErrorAction SilentlyContinue |
        ForEach-Object {
            $pathSegments = $_.DirectoryName -split '[\\/]'
            [pscustomobject]@{
                DirectoryName = $_.DirectoryName
                IsPublishPath = $pathSegments -contains 'publish'
                LastWriteTimeUtc = $_.LastWriteTimeUtc
                Depth = $pathSegments.Count
            }
        } |
        Sort-Object @{ Expression = 'IsPublishPath'; Descending = $false },
            @{ Expression = 'LastWriteTimeUtc'; Descending = $true },
            @{ Expression = 'Depth'; Descending = $false },
            DirectoryName

    foreach ($candidate in $appOutputCandidates) {
        $servicePath = Join-Path $candidate.DirectoryName 'service'
        if (Test-Path (Join-Path $servicePath 'IntelligenceX.Chat.Service.dll')) {
            return $servicePath
        }

        return $servicePath
    }

    return $null
}

function Publish-ChatServiceSidecar {
    param(
        [Parameter(Mandatory)]
        [string] $RepoRoot,
        [Parameter(Mandatory)]
        [string] $OutputPath,
        [Parameter(Mandatory)]
        [string] $Configuration,
        [switch] $NoBuild
    )

    $serviceProject = Join-Path $RepoRoot 'IntelligenceX.Chat\IntelligenceX.Chat.Service\IntelligenceX.Chat.Service.csproj'
    if (-not (Test-Path $serviceProject)) {
        throw "Service project not found: $serviceProject"
    }

    if (Test-Path $OutputPath) {
        Remove-Item -LiteralPath $OutputPath -Recurse -Force
    }
    New-Item -ItemType Directory -Path $OutputPath -Force | Out-Null

    $serviceProjectDirectory = Split-Path -Parent $serviceProject
    $ridBuildOutputPath = Join-Path $serviceProjectDirectory ("bin\" + $Configuration + "\net10.0-windows\win-x64")
    $hasRidBuildOutputs =
        (Test-Path -LiteralPath $ridBuildOutputPath) -and
        ($null -ne (Get-ChildItem -LiteralPath $ridBuildOutputPath -Recurse -File -ErrorAction SilentlyContinue | Select-Object -First 1))

    $publishArgs = @(
        'publish',
        $serviceProject,
        '-c',
        $Configuration,
        '-f',
        'net10.0-windows',
        '-r',
        'win-x64',
        '-o',
        $OutputPath,
        '--no-self-contained',
        '/p:WarningsNotAsErrors=NU1510'
    )

    if ($NoBuild) {
        & dotnet restore $serviceProject -r win-x64
        if ($LASTEXITCODE -ne 0) {
            throw "dotnet restore failed with exit code $LASTEXITCODE."
        }
        if ($hasRidBuildOutputs) {
            $publishArgs += '--no-build'
        } else {
            Write-Step "RID-specific service build outputs not found at '$ridBuildOutputPath'; publishing with build despite -NoBuild."
        }
    }

    Write-Step "Publish clean service sidecar: $OutputPath"
    & dotnet @publishArgs
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE."
    }
}

function Resolve-ChatPrivateToolPackState {
    param(
        [Parameter(Mandatory)]
        [string] $RepoRoot,
        [switch] $IncludePrivateToolPacks,
        [string] $TestimoXRoot
    )

    $explicitPrivateToolPacks = $IncludePrivateToolPacks.IsPresent -or -not [string]::IsNullOrWhiteSpace($TestimoXRoot)
    if ($explicitPrivateToolPacks) {
        $resolvedRoot = Resolve-TestimoXRoot -Provided $TestimoXRoot -RepoRoot $RepoRoot
        return [pscustomobject]@{
            Enabled = $true
            TestimoXRoot = $resolvedRoot
            Mode = 'explicit'
            Message = "Private tool packs: enabled ($resolvedRoot)"
        }
    }

    foreach ($envName in @('TESTIMOX_ROOT', 'TestimoXRoot')) {
        $fromEnvironment = [System.Environment]::GetEnvironmentVariable($envName)
        if ([string]::IsNullOrWhiteSpace($fromEnvironment)) {
            continue
        }

        if (Test-TestimoXMarkers -Root $fromEnvironment) {
            $resolvedRoot = Ensure-TestimoXTrailingSlash -Path $fromEnvironment
            return [pscustomobject]@{
                Enabled = $true
                TestimoXRoot = $resolvedRoot
                Mode = 'auto'
                Message = "Private tool packs: auto-enabled from $envName ($resolvedRoot)"
            }
        }

        Write-Warning "Environment variable $envName is set but does not contain required TestimoX markers: $fromEnvironment"
    }

    foreach ($candidate in (Get-TestimoXRootCandidates -RepoRoot $RepoRoot)) {
        if (-not (Test-TestimoXMarkers -Root $candidate)) {
            continue
        }

        $resolvedRoot = Ensure-TestimoXTrailingSlash -Path $candidate
        return [pscustomobject]@{
            Enabled = $true
            TestimoXRoot = $resolvedRoot
            Mode = 'auto'
            Message = "Private tool packs: auto-enabled ($resolvedRoot)"
        }
    }

    return [pscustomobject]@{
        Enabled = $false
        TestimoXRoot = $null
        Mode = 'unavailable'
        Message = 'Private tool packs: not available; continuing public-only'
    }
}
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$appProject = Join-Path $repoRoot 'IntelligenceX.Chat\IntelligenceX.Chat.App\IntelligenceX.Chat.App.csproj'
$resolvedOfficeImoRoot = $null
. (Join-Path $repoRoot 'Build\Internal\Publish-ChatBundledToolProjects.ps1')
. (Join-Path $repoRoot 'Build\Internal\Resolve-TestimoXRoot.ps1')
$resolvedOfficeImoRoot = Resolve-OptionalSiblingRepoRoot `
    -RepoRoot $repoRoot `
    -RepoNames @('OfficeIMO') `
    -MarkerRelativePaths @('OfficeIMO.MarkdownRenderer\OfficeIMO.MarkdownRenderer.csproj')

if (-not (Test-Path $appProject)) {
    throw "Project not found: $appProject"
}

# Prevent file lock issues during build/run.
Stop-IfRunning -Names @('IntelligenceX.Chat.App', 'IntelligenceX.Chat.Service')

$privateToolPackState = Resolve-ChatPrivateToolPackState `
    -RepoRoot $repoRoot `
    -IncludePrivateToolPacks:$IncludePrivateToolPacks `
    -TestimoXRoot $TestimoXRoot
$effectiveIncludePrivateToolPacks = [bool]$privateToolPackState.Enabled
$effectiveTestimoXRoot = $privateToolPackState.TestimoXRoot

$dotnetRunArgs = @(
    'run',
    '--project', $appProject,
    '-c', $Configuration
)

if ($NoBuild) {
    $dotnetRunArgs += '--no-build'
}

if ($resolvedOfficeImoRoot) {
    $dotnetRunArgs += "/p:UseLocalOfficeImoCheckout=true"
    $dotnetRunArgs += "/p:OfficeImoRepoRoot=$resolvedOfficeImoRoot"
}

if ($effectiveIncludePrivateToolPacks) {
    $dotnetRunArgs += '/p:IncludePrivateToolPacks=true'
    if (-not [string]::IsNullOrWhiteSpace($effectiveTestimoXRoot)) {
        $dotnetRunArgs += "/p:TestimoXRoot=$effectiveTestimoXRoot"
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
Write-Step $privateToolPackState.Message

Push-Location $repoRoot
try {
    if (-not $NoBuild) {
        $dotnetBuildArgs = @(
            'build',
            $appProject,
            '-c',
            $Configuration,
            '/p:SkipChatServiceSidecarBuild=true'
        )
        if ($resolvedOfficeImoRoot) {
            $dotnetBuildArgs += "/p:UseLocalOfficeImoCheckout=true"
            $dotnetBuildArgs += "/p:OfficeImoRepoRoot=$resolvedOfficeImoRoot"
        }
        if ($effectiveIncludePrivateToolPacks) {
            $dotnetBuildArgs += '/p:IncludePrivateToolPacks=true'
            if (-not [string]::IsNullOrWhiteSpace($effectiveTestimoXRoot)) {
                $dotnetBuildArgs += "/p:TestimoXRoot=$effectiveTestimoXRoot"
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

    Publish-ChatServiceSidecar `
        -RepoRoot $repoRoot `
        -OutputPath $serviceOutputPath `
        -Configuration $Configuration `
        -NoBuild:$NoBuild

    # Local runs should mirror packaged Chat startup by staging tool packs into a clean sidecar.
    Write-Step "Bundle tool projects into service sidecar: $serviceOutputPath"
    Publish-ChatBundledToolProjects `
        -RepoRoot $repoRoot `
        -OutputPath $serviceOutputPath `
        -Configuration $Configuration `
        -Runtime 'win-x64' `
        -NoBuild:$NoBuild `
        -IncludePrivateToolPacks:$effectiveIncludePrivateToolPacks `
        -TestimoXRoot $effectiveTestimoXRoot

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
