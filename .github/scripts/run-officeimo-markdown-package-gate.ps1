Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')

$exportArtifactsProject = Join-Path $repoRoot 'IntelligenceX.Chat\IntelligenceX.Chat.ExportArtifacts\IntelligenceX.Chat.ExportArtifacts.csproj'
$appTestsProject = Join-Path $repoRoot 'IntelligenceX.Chat\IntelligenceX.Chat.App.Tests\IntelligenceX.Chat.App.Tests.csproj'

$packageModeProperty = '-p:UseLocalOfficeImoCheckout=false'
$skipSidecarBuildProperty = '-p:SkipChatServiceSidecarBuild=true'
Write-Output 'Running OfficeIMO markdown package-mode gate...'

function Invoke-DotNet {
    param(
        [Parameter(Mandatory)]
        [string[]] $Arguments
    )

    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

function Assert-PackageMode($projectPath) {
    $effectiveMode = (& dotnet msbuild $projectPath -nologo -getProperty:UseLocalOfficeImoCheckout $packageModeProperty $skipSidecarBuildProperty | Select-Object -Last 1).Trim()
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet msbuild property lookup failed for $projectPath with exit code $LASTEXITCODE."
    }

    if ($effectiveMode -ne 'false') {
        throw "Expected UseLocalOfficeImoCheckout=false for $projectPath but got '$effectiveMode'."
    }
}

Assert-PackageMode $exportArtifactsProject
Assert-PackageMode $appTestsProject

Invoke-DotNet @('restore', $exportArtifactsProject, $packageModeProperty, $skipSidecarBuildProperty, '--force-evaluate')
Invoke-DotNet @('restore', $appTestsProject, $packageModeProperty, $skipSidecarBuildProperty, '--force-evaluate')
Invoke-DotNet @('build', $exportArtifactsProject, $packageModeProperty, $skipSidecarBuildProperty, '--configuration', 'Release', '--no-restore')
Invoke-DotNet @('build', $appTestsProject, $packageModeProperty, $skipSidecarBuildProperty, '--configuration', 'Release', '--no-restore')
