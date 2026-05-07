param(
    [string] $WorkflowPath = ".github/workflows/review-intelligencex.yml"
)

$ErrorActionPreference = "Stop"

$cliDll = Join-Path (Get-Location) "IntelligenceX.Cli/bin/Release/net8.0/IntelligenceX.Cli.dll"
if (Test-Path -LiteralPath $cliDll -PathType Leaf) {
    dotnet $cliDll ci verify-managed-workflow --workflow $WorkflowPath
} else {
    dotnet run --project IntelligenceX.Cli/IntelligenceX.Cli.csproj -c Release -f net8.0 --no-restore -- `
        ci verify-managed-workflow --workflow $WorkflowPath
}
exit $LASTEXITCODE
