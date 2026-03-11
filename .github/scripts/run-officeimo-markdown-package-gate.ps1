Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')

$exportArtifactsProject = Join-Path $repoRoot 'IntelligenceX.Chat\IntelligenceX.Chat.ExportArtifacts\IntelligenceX.Chat.ExportArtifacts.csproj'
$appTestsProject = Join-Path $repoRoot 'IntelligenceX.Chat\IntelligenceX.Chat.App.Tests\IntelligenceX.Chat.App.Tests.csproj'

$packageModeProperty = '-p:UseLocalOfficeImoCheckout=false'
$filter = 'FullyQualifiedName~TranscriptMarkdownContractTests|FullyQualifiedName~TranscriptMarkdownContractIntegrationTests|FullyQualifiedName~OfficeImoMarkdownRuntimeContractTests|FullyQualifiedName~OfficeImoMarkdownInputNormalizationRuntimeContractTests|FullyQualifiedName~LocalExportArtifactWriterTests'

Write-Host 'Running OfficeIMO markdown package-mode gate...'

function Assert-PackageMode($projectPath) {
    $effectiveMode = dotnet msbuild $projectPath -nologo -getProperty:UseLocalOfficeImoCheckout $packageModeProperty
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

    if (($effectiveMode | Out-String).Trim() -ne 'false') {
        Write-Error "Expected UseLocalOfficeImoCheckout=false for $projectPath but got '$effectiveMode'."
        exit 1
    }
}

Assert-PackageMode $exportArtifactsProject
Assert-PackageMode $appTestsProject

dotnet restore $exportArtifactsProject $packageModeProperty --force-evaluate
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet restore $appTestsProject $packageModeProperty --force-evaluate
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet build $exportArtifactsProject $packageModeProperty --configuration Release --no-restore
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

dotnet test $appTestsProject $packageModeProperty --configuration Release --filter $filter --no-restore
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
