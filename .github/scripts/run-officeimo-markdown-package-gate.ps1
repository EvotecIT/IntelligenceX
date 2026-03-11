Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')

$exportArtifactsProject = Join-Path $repoRoot 'IntelligenceX.Chat\IntelligenceX.Chat.ExportArtifacts\IntelligenceX.Chat.ExportArtifacts.csproj'
$appTestsProject = Join-Path $repoRoot 'IntelligenceX.Chat\IntelligenceX.Chat.App.Tests\IntelligenceX.Chat.App.Tests.csproj'

$packageModeProperty = '-p:UseLocalOfficeImoCheckout=false'
$skipSidecarBuildProperty = '-p:SkipChatServiceSidecarBuild=true'
$filter = @(
    'FullyQualifiedName~DescribeMarkdownRendererContract_ReportsMinimumPublishedVersion'
    'FullyQualifiedName~DescribeMarkdownContract_ReportsNormalizationPresetMinimumVersion'
    'FullyQualifiedName~CreateTranscriptRendererOptions_EnablesExpectedVisualDefaults'
    'FullyQualifiedName~NormalizeForTranscriptCleanup_NormalizesOrderedListParenMarkers'
    'FullyQualifiedName~NormalizeForTranscriptCleanup_DoesNotMutateFencedCode'
    'FullyQualifiedName~CreateTranscriptMarkdownToWordOptions_ConfiguresNarrativeAndImageDefaults'
) -join '|'

Write-Output 'Running OfficeIMO markdown package-mode gate...'

function Assert-PackageMode($projectPath) {
    $effectiveMode = dotnet msbuild $projectPath -nologo -getProperty:UseLocalOfficeImoCheckout $packageModeProperty $skipSidecarBuildProperty
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    if (($effectiveMode | Out-String).Trim() -ne 'false') {
        Write-Error "Expected UseLocalOfficeImoCheckout=false for $projectPath but got '$effectiveMode'."
        exit 1
    }
}

Assert-PackageMode $exportArtifactsProject
Assert-PackageMode $appTestsProject

dotnet restore $exportArtifactsProject $packageModeProperty $skipSidecarBuildProperty --force-evaluate
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet restore $appTestsProject $packageModeProperty $skipSidecarBuildProperty --force-evaluate
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet build $exportArtifactsProject $packageModeProperty $skipSidecarBuildProperty --configuration Release --no-restore
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet build $appTestsProject $packageModeProperty $skipSidecarBuildProperty --configuration Release --no-restore
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet test $appTestsProject $packageModeProperty $skipSidecarBuildProperty --configuration Release --filter $filter --no-build --no-restore
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
