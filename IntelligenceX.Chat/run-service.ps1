param(
    [string] $Pipe = 'intelligencex.chat',
    [string] $Model = 'gpt-5.3-codex',
    [string] $AllowRoot,
    [int] $MaxToolRounds = 3,
    [switch] $NoParallelTools,
    [string[]] $ServiceArgs
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$buildScript = Join-Path $repoRoot 'Build\Run-ChatService.ps1'
if (-not (Test-Path $buildScript)) {
    throw "Build script not found: $buildScript"
}

$args = @{
    Pipe = $Pipe
    Model = $Model
    MaxToolRounds = $MaxToolRounds
    NoParallelTools = $NoParallelTools.IsPresent
}
if (-not [string]::IsNullOrWhiteSpace($AllowRoot)) {
    $args['AllowRoot'] = @($AllowRoot)
}
if ($ServiceArgs -and $ServiceArgs.Count -gt 0) {
    $args['ExtraArgs'] = $ServiceArgs
}

& $buildScript @args
exit $LASTEXITCODE
