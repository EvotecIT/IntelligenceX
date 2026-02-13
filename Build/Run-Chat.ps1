# One-command local chat startup (host-only by default).

[CmdletBinding()] param(
    [string[]] $AllowRoot,
    [string] $Framework = 'net10.0-windows',
    [switch] $NoBuild,
    [switch] $ParallelTools = $true,
    [switch] $EchoToolOutputs = $true,
    [switch] $EnablePowerShellPack,
    [switch] $EnableTestimoXPack,
    [string[]] $PluginPath,
    [switch] $NoDefaultPluginPaths,
    [switch] $ShowToolIds,
    [switch] $ForceLogin,
    [switch] $IncludePrivateToolPacks,
    [string] $TestimoXRoot,
    [string] $Model,
    [string] $InstructionsFile,
    [string[]] $ExtraArgs
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Header($text) { Write-Host "`n=== $text ===" -ForegroundColor Cyan }
function Write-Step($text)   { Write-Host "[+] $text" -ForegroundColor Yellow }

$repoRoot = (Get-Item (Split-Path -Parent $MyInvocation.MyCommand.Path)).Parent.FullName
$hostProject = Join-Path $repoRoot 'IntelligenceX.Chat\IntelligenceX.Chat.Host\IntelligenceX.Chat.Host.csproj'

if (-not (Test-Path $hostProject)) {
    throw "Host project not found: $hostProject"
}

if (-not $AllowRoot -or $AllowRoot.Count -eq 0) {
    $AllowRoot = @((Split-Path -Parent $repoRoot))
}

$runArgs = @('run', '--project', $hostProject, '--framework', $Framework)
if ($NoBuild) {
    $runArgs += '--no-build'
}
if ($IncludePrivateToolPacks) {
    $runArgs += '/p:IncludePrivateToolPacks=true'
    if (-not [string]::IsNullOrWhiteSpace($TestimoXRoot)) {
        $resolved = [System.IO.Path]::GetFullPath($TestimoXRoot)
        if (-not $resolved.EndsWith([System.IO.Path]::DirectorySeparatorChar)) {
            $resolved += [System.IO.Path]::DirectorySeparatorChar
        }
        $runArgs += "/p:TestimoXRoot=$resolved"
    }
}
$runArgs += '--'

foreach ($root in $AllowRoot) {
    if (-not [string]::IsNullOrWhiteSpace($root)) {
        $runArgs += @('--allow-root', $root)
    }
}

if ($ParallelTools) {
    $runArgs += '--parallel-tools'
}
if ($EchoToolOutputs) {
    $runArgs += '--echo-tool-outputs'
}
if ($EnablePowerShellPack) {
    $runArgs += '--enable-powershell-pack'
}
if ($EnableTestimoXPack) {
    $runArgs += '--enable-testimox-pack'
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
if ($ShowToolIds) {
    $runArgs += '--show-tool-ids'
}
if ($ForceLogin) {
    $runArgs += '--login'
}
if (-not [string]::IsNullOrWhiteSpace($Model)) {
    $runArgs += @('--model', $Model)
}
if (-not [string]::IsNullOrWhiteSpace($InstructionsFile)) {
    $runArgs += @('--instructions-file', $InstructionsFile)
}
if ($ExtraArgs -and $ExtraArgs.Count -gt 0) {
    $runArgs += $ExtraArgs
}

Write-Header 'Run Chat Host'
Write-Step ("Allow roots: {0}" -f ($AllowRoot -join '; '))
Write-Step 'Starting IntelligenceX.Chat.Host...'

Push-Location $repoRoot
try {
    & dotnet @runArgs
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet run failed with exit code $LASTEXITCODE."
    }
} finally {
    Pop-Location
}
