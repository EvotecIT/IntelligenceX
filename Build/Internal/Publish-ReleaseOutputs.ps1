[CmdletBinding()] param(
    [Parameter(Mandatory)]
    [string] $RepoRoot,

    [Parameter(Mandatory)]
    [string] $ConfigPath,

    [Parameter(Mandatory)]
    [string] $StageRoot,

    [switch] $SyncWinget,
    [switch] $PublishNuget,
    [switch] $PublishGitHub,
    [string] $GitHubCommand = 'gh',
    [string] $DotNetCommand = 'dotnet'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $RepoRoot 'Build\Internal\Build.ScriptSupport.ps1')

function Read-JsonFile {
    param([Parameter(Mandatory)][string] $Path)

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "Required JSON file not found: $Path"
    }

    return Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json -Depth 100
}

function Resolve-ConfigRelativePath {
    param(
        [Parameter(Mandatory)][string] $ConfigDirectory,
        [string] $PathValue
    )

    if ([string]::IsNullOrWhiteSpace($PathValue)) {
        return $null
    }

    if ([System.IO.Path]::IsPathRooted($PathValue)) {
        return [System.IO.Path]::GetFullPath($PathValue)
    }

    return [System.IO.Path]::GetFullPath((Join-Path $ConfigDirectory $PathValue))
}

function Invoke-ExternalCommand {
    param(
        [Parameter(Mandatory)][string] $Command,
        [Parameter(Mandatory)][string[]] $Arguments,
        [Parameter(Mandatory)][string] $FailureContext,
        [hashtable] $EnvironmentVariables
    )

    $commandLine = Format-CommandLine -Command $Command -Arguments $Arguments
    $previousValues = @{}
    try {
        foreach ($entry in @($(if ($null -ne $EnvironmentVariables) { $EnvironmentVariables.GetEnumerator() } else { @() }))) {
            $name = [string] $entry.Key
            $previousValues[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
            [Environment]::SetEnvironmentVariable($name, [string] $entry.Value, 'Process')
        }

        & $Command @Arguments
        $exitCode = $LASTEXITCODE
    } finally {
        foreach ($name in $previousValues.Keys) {
            [Environment]::SetEnvironmentVariable($name, $previousValues[$name], 'Process')
        }
    }

    if ($exitCode -eq 0) {
        return
    }

    throw "$FailureContext`nCommand failed with exit code ${exitCode}.`nCommand: $commandLine"
}

function Get-ReleaseVersion {
    param([Parameter(Mandatory)] $ReleaseManifest)

    $version = [string] $ReleaseManifest.packages.ResolvedVersion
    if ([string]::IsNullOrWhiteSpace($version)) {
        throw 'release-manifest.json did not include packages.ResolvedVersion.'
    }

    return $version
}

function Sync-WingetOutputs {
    param(
        [Parameter(Mandatory)][string] $ConfigDirectory,
        [Parameter(Mandatory)][string] $StageRootPath,
        [Parameter(Mandatory)] $ReleaseConfig
    )

    if ($null -eq $ReleaseConfig.Winget -or -not [bool] $ReleaseConfig.Winget.Enabled) {
        return $null
    }

    $wingetSource = Resolve-ConfigRelativePath -ConfigDirectory $ConfigDirectory -PathValue ([string] $ReleaseConfig.Winget.OutputPath)
    if ([string]::IsNullOrWhiteSpace($wingetSource) -or -not (Test-Path -LiteralPath $wingetSource)) {
        Write-Warn "Winget output path not found; skipping stage sync: $wingetSource"
        return $null
    }

    $wingetStageRoot = Join-Path $StageRootPath 'Winget'
    if (Test-Path -LiteralPath $wingetStageRoot) {
        Remove-Item -LiteralPath $wingetStageRoot -Recurse -Force
    }
    New-Item -ItemType Directory -Force -Path $wingetStageRoot | Out-Null

    Get-ChildItem -LiteralPath $wingetSource -Filter *.yaml -File | ForEach-Object {
        Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $wingetStageRoot $_.Name) -Force
    }

    return $wingetStageRoot
}

function Get-GitHubReleaseAssets {
    param([Parameter(Mandatory)][string] $StageRootPath)

    $assetPaths = [System.Collections.Generic.List[string]]::new()
    foreach ($subPath in @('GitHub', 'NuGet', 'Winget')) {
        $candidate = Join-Path $StageRootPath $subPath
        if (Test-Path -LiteralPath $candidate) {
            Get-ChildItem -LiteralPath $candidate -File | Sort-Object Name | ForEach-Object {
                $assetPaths.Add($_.FullName)
            }
        }
    }

    foreach ($fileName in @('release-manifest.json', 'SHA256SUMS.txt')) {
        $candidate = Join-Path $StageRootPath $fileName
        if (Test-Path -LiteralPath $candidate) {
            $assetPaths.Add($candidate)
        }
    }

    return @($assetPaths)
}

function Publish-GitHubRelease {
    param(
        [Parameter(Mandatory)] $ReleaseConfig,
        [Parameter(Mandatory)] $ReleaseManifest,
        [Parameter(Mandatory)][string] $StageRootPath,
        [Parameter(Mandatory)][string] $ConfigDirectory,
        [Parameter(Mandatory)][string] $GitHubCli
    )

    $owner = [string] $ReleaseConfig.Packages.GitHubUsername
    $repo = [string] $ReleaseConfig.Packages.GitHubRepositoryName
    if ([string]::IsNullOrWhiteSpace($owner) -or [string]::IsNullOrWhiteSpace($repo)) {
        throw 'release.json Packages.GitHubUsername / GitHubRepositoryName are required for GitHub publishing.'
    }

    $tokenFilePath = Resolve-ConfigRelativePath -ConfigDirectory $ConfigDirectory -PathValue ([string] $ReleaseConfig.Packages.GitHubAccessTokenFilePath)
    if ([string]::IsNullOrWhiteSpace($tokenFilePath) -or -not (Test-Path -LiteralPath $tokenFilePath)) {
        throw "GitHub token file not found: $tokenFilePath"
    }

    $version = Get-ReleaseVersion -ReleaseManifest $ReleaseManifest
    $tag = "v$version"
    $releaseName = "$repo $version"
    $repoSlug = "$owner/$repo"
    $generateNotes = [bool] $ReleaseConfig.Packages.GitHubGenerateReleaseNotes
    $isPreRelease = [bool] $ReleaseConfig.Packages.GitHubIsPreRelease
    $assets = Get-GitHubReleaseAssets -StageRootPath $StageRootPath
    if ($assets.Count -eq 0) {
        throw "No GitHub release assets were found under $StageRootPath"
    }

    $ghToken = (Get-Content -LiteralPath $tokenFilePath -Raw).Trim()
    if ([string]::IsNullOrWhiteSpace($ghToken)) {
        throw "GitHub token file was empty: $tokenFilePath"
    }

    $envVars = @{ GH_TOKEN = $ghToken }

    $previousGitHubToken = [Environment]::GetEnvironmentVariable('GH_TOKEN', 'Process')
    try {
        [Environment]::SetEnvironmentVariable('GH_TOKEN', $ghToken, 'Process')
        & $GitHubCli release view $tag --repo $repoSlug *> $null
        $releaseExists = ($LASTEXITCODE -eq 0)
    } finally {
        [Environment]::SetEnvironmentVariable('GH_TOKEN', $previousGitHubToken, 'Process')
    }

    if (-not $releaseExists) {
        $createArgs = [System.Collections.Generic.List[string]]::new()
        $createArgs.AddRange([string[]]@('release', 'create', $tag, '--repo', $repoSlug, '--title', $releaseName))
        if ($generateNotes) {
            $createArgs.Add('--generate-notes')
        }
        if ($isPreRelease) {
            $createArgs.Add('--prerelease')
        }

        Write-Step "Create GitHub release: $repoSlug $tag"
        Invoke-ExternalCommand -Command $GitHubCli -Arguments @($createArgs) -FailureContext 'GitHub release creation failed.' -EnvironmentVariables $envVars
    } else {
        Write-Step "Reuse GitHub release: $repoSlug $tag"
    }

    $uploadArgs = [System.Collections.Generic.List[string]]::new()
    $uploadArgs.AddRange([string[]]@('release', 'upload', $tag, '--repo', $repoSlug, '--clobber'))
    $uploadArgs.AddRange([string[]] $assets)

    Write-Step "Upload $($assets.Count) asset(s) to GitHub release $tag"
    Invoke-ExternalCommand -Command $GitHubCli -Arguments @($uploadArgs) -FailureContext 'GitHub release upload failed.' -EnvironmentVariables $envVars
}

function Publish-NuGetPackages {
    param(
        [Parameter(Mandatory)] $ReleaseConfig,
        [Parameter(Mandatory)][string] $StageRootPath,
        [Parameter(Mandatory)][string] $ConfigDirectory,
        [Parameter(Mandatory)][string] $DotNetCli
    )

    $nugetRoot = Join-Path $StageRootPath 'NuGet'
    if (-not (Test-Path -LiteralPath $nugetRoot)) {
        throw "NuGet stage root was not found: $nugetRoot"
    }

    $packages = @(Get-ChildItem -LiteralPath $nugetRoot -Filter *.nupkg -File | Where-Object { $_.Name -notlike '*.symbols.nupkg' -and $_.Name -notlike '*.snupkg' } | Sort-Object Name)
    if ($packages.Count -eq 0) {
        throw "No .nupkg files were found under $nugetRoot"
    }

    $publishSource = [string] $ReleaseConfig.Packages.PublishSource
    $apiKeyFilePath = Resolve-ConfigRelativePath -ConfigDirectory $ConfigDirectory -PathValue ([string] $ReleaseConfig.Packages.PublishApiKeyFilePath)
    if ([string]::IsNullOrWhiteSpace($publishSource)) {
        throw 'release.json Packages.PublishSource is required for NuGet publishing.'
    }
    if ([string]::IsNullOrWhiteSpace($apiKeyFilePath) -or -not (Test-Path -LiteralPath $apiKeyFilePath)) {
        throw "NuGet API key file not found: $apiKeyFilePath"
    }

    $apiKey = (Get-Content -LiteralPath $apiKeyFilePath -Raw).Trim()
    if ([string]::IsNullOrWhiteSpace($apiKey)) {
        throw "NuGet API key file was empty: $apiKeyFilePath"
    }

    foreach ($package in $packages) {
        $pushArgs = [System.Collections.Generic.List[string]]::new()
        $pushArgs.AddRange([string[]]@('nuget', 'push', $package.FullName, '--source', $publishSource, '--api-key', $apiKey))
        if ([bool] $ReleaseConfig.Packages.SkipDuplicate) {
            $pushArgs.Add('--skip-duplicate')
        }

        Write-Step "Publish NuGet package: $($package.Name)"
        Invoke-ExternalCommand -Command $DotNetCli -Arguments @($pushArgs) -FailureContext "NuGet publish failed for $($package.Name)."
    }
}

$resolvedRepoRoot = [System.IO.Path]::GetFullPath($RepoRoot)
$resolvedConfigPath = [System.IO.Path]::GetFullPath($ConfigPath)
$resolvedStageRoot = [System.IO.Path]::GetFullPath($StageRoot)

if (-not (Test-Path -LiteralPath $resolvedStageRoot)) {
    throw "Stage root not found: $resolvedStageRoot"
}

$releaseConfig = Read-JsonFile -Path $resolvedConfigPath
$releaseManifestPath = Join-Path $resolvedStageRoot 'release-manifest.json'
$releaseManifest = Read-JsonFile -Path $releaseManifestPath
$configDirectory = Split-Path -Parent $resolvedConfigPath

if ($SyncWinget) {
    $wingetStageRoot = Sync-WingetOutputs -ConfigDirectory $configDirectory -StageRootPath $resolvedStageRoot -ReleaseConfig $releaseConfig
    if (-not [string]::IsNullOrWhiteSpace($wingetStageRoot)) {
        Write-Ok "Winget manifests staged: $wingetStageRoot"
    }
}

if ($PublishGitHub) {
    Publish-GitHubRelease -ReleaseConfig $releaseConfig -ReleaseManifest $releaseManifest -StageRootPath $resolvedStageRoot -ConfigDirectory $configDirectory -GitHubCli $GitHubCommand
}

if ($PublishNuget) {
    Publish-NuGetPackages -ReleaseConfig $releaseConfig -StageRootPath $resolvedStageRoot -ConfigDirectory $configDirectory -DotNetCli $DotNetCommand
}
