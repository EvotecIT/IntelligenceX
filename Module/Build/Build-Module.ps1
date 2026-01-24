param(
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'

$moduleRoot = Split-Path -Parent $PSScriptRoot
$repoRoot = Resolve-Path (Join-Path $moduleRoot '..')
$projectPath = Join-Path $repoRoot 'IntelligenceX.PowerShell/IntelligenceX.PowerShell.csproj'
$libRoot = Join-Path $moduleRoot 'Lib'

$frameworks = @('net8.0')
if ($IsWindows) {
    $frameworks += 'net472'
}

foreach ($framework in $frameworks) {
    Write-Host "Building $projectPath for $framework..."
    dotnet build $projectPath -c $Configuration -f $framework | Out-String | Write-Host

    $targetDir = Join-Path $libRoot $framework
    if (-not (Test-Path $targetDir)) {
        New-Item -ItemType Directory -Path $targetDir | Out-Null
    }

    $buildOutput = Join-Path $repoRoot "IntelligenceX.PowerShell/bin/$Configuration/$framework"
    Copy-Item -Path (Join-Path $buildOutput '*.dll') -Destination $targetDir -Force
    Copy-Item -Path (Join-Path $buildOutput '*.pdb') -Destination $targetDir -Force -ErrorAction SilentlyContinue
    Copy-Item -Path (Join-Path $buildOutput '*.xml') -Destination $targetDir -Force -ErrorAction SilentlyContinue
}

Write-Host 'Module build complete.'
