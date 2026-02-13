# One-command local chat service startup (build-root entrypoint).

[CmdletBinding()] param(
    [string] $Pipe = 'intelligencex.chat',
    [string] $Model = 'gpt-5.3-codex',
    [string[]] $AllowRoot,
    [string] $Framework = 'net10.0-windows',
    [int] $MaxToolRounds = 3,
    [switch] $NoParallelTools,
    [switch] $NoBuild,
    [string[]] $PluginPath,
    [switch] $NoDefaultPluginPaths,
    [string[]] $ExtraArgs
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Header($text) { Write-Host "`n=== $text ===" -ForegroundColor Cyan }
function Write-Step($text)   { Write-Host "[+] $text" -ForegroundColor Yellow }

$repoRoot = (Get-Item (Split-Path -Parent $MyInvocation.MyCommand.Path)).Parent.FullName
$serviceProject = Join-Path $repoRoot 'IntelligenceX.Chat\IntelligenceX.Chat.Service\IntelligenceX.Chat.Service.csproj'

if (-not (Test-Path $serviceProject)) {
    throw "Service project not found: $serviceProject"
}

if (-not $AllowRoot -or $AllowRoot.Count -eq 0) {
    $AllowRoot = @((Split-Path -Parent $repoRoot))
}

$runArgs = @('run', '--project', $serviceProject, '--framework', $Framework)
if ($NoBuild) {
    $runArgs += '--no-build'
}
$runArgs += '--'

if (-not [string]::IsNullOrWhiteSpace($Pipe)) {
    $runArgs += @('--pipe', $Pipe)
}
if (-not [string]::IsNullOrWhiteSpace($Model)) {
    $runArgs += @('--model', $Model)
}
foreach ($root in $AllowRoot) {
    if (-not [string]::IsNullOrWhiteSpace($root)) {
        $runArgs += @('--allow-root', $root)
    }
}
if ($MaxToolRounds -gt 0) {
    $runArgs += @('--max-tool-rounds', $MaxToolRounds)
}
if ($NoParallelTools) {
    $runArgs += '--no-parallel-tools'
}
if ($PluginPath -and $PluginPath.Count -gt 0) {
    foreach ($path in $PluginPath) {
        if (-not [string]::IsNullOrWhiteSpace($path)) {
            $runArgs += @('--plugin-path', $path)
        }
    }
}
if ($NoDefaultPluginPaths) {
    $runArgs += '--no-default-plugin-paths'
}
if ($ExtraArgs -and $ExtraArgs.Count -gt 0) {
    $runArgs += $ExtraArgs
}

Write-Header 'Run Chat Service'
Write-Step "Pipe: $Pipe"
Write-Step "Model: $Model"
Write-Step ("Allow roots: {0}" -f ($AllowRoot -join '; '))
Write-Step 'Starting IntelligenceX.Chat.Service...'

Push-Location $repoRoot
try {
    & dotnet @runArgs
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet run failed with exit code $LASTEXITCODE."
    }
} finally {
    Pop-Location
}
