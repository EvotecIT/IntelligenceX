param(
    [string] $AllowRoot,
    [switch] $ParallelTools,
    [switch] $EchoToolOutputs,
    [string[]] $HostArgs
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$buildScript = Join-Path $repoRoot 'Build\Run-Chat.ps1'
if (-not (Test-Path $buildScript)) {
    throw "Build script not found: $buildScript"
}

$args = @{}
if (-not [string]::IsNullOrWhiteSpace($AllowRoot)) {
    $args['AllowRoot'] = @($AllowRoot)
}
$args['ParallelTools'] = $ParallelTools.IsPresent
$args['EchoToolOutputs'] = $EchoToolOutputs.IsPresent
if ($HostArgs -and $HostArgs.Count -gt 0) {
    $args['ExtraArgs'] = $HostArgs
}

& $buildScript @args
exit $LASTEXITCODE
