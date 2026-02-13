param(
    [ValidateSet('Debug', 'Release')]
    [string] $Configuration = 'Release',
    [switch] $NoBuild
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$buildScript = Join-Path $repoRoot 'Build\Run-ChatApp.ps1'
if (-not (Test-Path $buildScript)) {
    throw "Build script not found: $buildScript"
}

& $buildScript -Configuration $Configuration -NoBuild:$NoBuild
exit $LASTEXITCODE
