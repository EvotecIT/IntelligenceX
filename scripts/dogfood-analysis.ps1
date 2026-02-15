param(
    [string]$Framework = "net8.0",
    [string]$ConfigPath = ".intelligencex/reviewer.json",
    [string]$OutDir = "artifacts"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$RepoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
Set-Location $RepoRoot

$cliProject = "IntelligenceX.Cli/IntelligenceX.Cli.csproj"

if (-not (Test-Path $ConfigPath)) {
    throw "Config not found: $ConfigPath"
}

Write-Output "Dogfood: validate-catalog"
dotnet run --project $cliProject --framework $Framework -- analyze validate-catalog --workspace .
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Output "Dogfood: analyze run"
dotnet run --project $cliProject --framework $Framework -- analyze run --config $ConfigPath --out $OutDir --framework $Framework --workspace .
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$changedFiles = Join-Path $OutDir "changed-files.txt"
Write-Output "Dogfood: ci changed-files ($changedFiles)"
dotnet run --project $cliProject --framework $Framework -- ci changed-files --workspace . --out $changedFiles
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Output "Dogfood: analyze gate"
dotnet run --project $cliProject --framework $Framework -- analyze gate --config $ConfigPath --workspace . --changed-files $changedFiles
exit $LASTEXITCODE
