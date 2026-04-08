# One-command local chat startup (host-only by default).

[CmdletBinding()] param(
    [string[]] $AllowRoot,
    [string] $Framework = 'net10.0-windows',
    [switch] $NoBuild,
    [switch] $ParallelTools = $true,
    [switch] $EchoToolOutputs = $true,
    [switch] $EnablePowerShellPack,
    [switch] $EnableTestimoXPack,
    [switch] $EnableDnsClientXPack,
    [switch] $DisableDnsClientXPack,
    [switch] $EnableDomainDetectivePack,
    [switch] $DisableDomainDetectivePack,
    [string[]] $PluginPath,
    [switch] $NoDefaultPluginPaths,
    [switch] $ShowToolIds,
    [switch] $ForceLogin,
    [switch] $IncludePrivateToolPacks,
    [string] $TestimoXRoot,
    [string] $Model,
    [string] $InstructionsFile,
    [string] $OpenAITransport,
    [string] $OpenAIBaseUrl,
    [string] $OpenAIApiKey,
    [switch] $OpenAIStream,
    [switch] $OpenAINoStream,
    [switch] $OpenAIAllowInsecureHttp,
    [switch] $OpenAIAllowInsecureHttpNonLoopback,
    [int] $TurnTimeoutSeconds = 0,
    [int] $ToolTimeoutSeconds = 0,
    [string] $ScenarioFile,
    [string] $ScenarioOutput,
    [switch] $ScenarioContinueOnError,
    [string[]] $ExtraArgs
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Header($text) { Write-Host "`n=== $text ===" -ForegroundColor Cyan }
function Write-Step($text)   { Write-Host "[+] $text" -ForegroundColor Yellow }
function Test-HasExplicitBuiltInToolProbeOverride([string[]] $arguments) {
    foreach ($argument in @($arguments)) {
        if ([string]::IsNullOrWhiteSpace($argument)) {
            continue
        }

        $candidate = $argument.Trim()
        if ($candidate.Equals('--built-in-tool-probe-path', [System.StringComparison]::OrdinalIgnoreCase)) {
            return $true
        }
        if ($candidate.Equals('--enable-workspace-built-in-tool-output-probing', [System.StringComparison]::OrdinalIgnoreCase)) {
            return $true
        }
        if ($candidate.Equals('--disable-workspace-built-in-tool-output-probing', [System.StringComparison]::OrdinalIgnoreCase)) {
            return $true
        }
        if ($candidate.StartsWith('--built-in-tool-probe-path=', [System.StringComparison]::OrdinalIgnoreCase)) {
            return $true
        }
    }

    return $false
}

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
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
    $runArgs += @('--enable-pack-id', 'powershell')
}
if ($EnableTestimoXPack) {
    $runArgs += @('--enable-pack-id', 'testimox')
}
if ($EnableDnsClientXPack) {
    $runArgs += @('--enable-pack-id', 'dnsclientx')
}
if ($DisableDnsClientXPack) {
    $runArgs += @('--disable-pack-id', 'dnsclientx')
}
if ($EnableDomainDetectivePack) {
    $runArgs += @('--enable-pack-id', 'domaindetective')
}
if ($DisableDomainDetectivePack) {
    $runArgs += @('--disable-pack-id', 'domaindetective')
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
if (-not [string]::IsNullOrWhiteSpace($OpenAITransport)) {
    $runArgs += @('--openai-transport', $OpenAITransport)
}
if (-not [string]::IsNullOrWhiteSpace($OpenAIBaseUrl)) {
    $runArgs += @('--openai-base-url', $OpenAIBaseUrl)
}
if (-not [string]::IsNullOrWhiteSpace($OpenAIApiKey)) {
    $runArgs += @('--openai-api-key', $OpenAIApiKey)
}
if ($OpenAIStream) {
    $runArgs += '--openai-stream'
}
if ($OpenAINoStream) {
    $runArgs += '--openai-no-stream'
}
if ($OpenAIAllowInsecureHttp) {
    $runArgs += '--openai-allow-insecure-http'
}
if ($OpenAIAllowInsecureHttpNonLoopback) {
    $runArgs += '--openai-allow-insecure-http-non-loopback'
}
if ($TurnTimeoutSeconds -gt 0) {
    $runArgs += @('--turn-timeout-seconds', $TurnTimeoutSeconds.ToString())
}
if ($ToolTimeoutSeconds -gt 0) {
    $runArgs += @('--tool-timeout-seconds', $ToolTimeoutSeconds.ToString())
}
if (-not [string]::IsNullOrWhiteSpace($ScenarioFile)) {
    $runArgs += @('--scenario-file', $ScenarioFile)
}
if (-not [string]::IsNullOrWhiteSpace($ScenarioOutput)) {
    $runArgs += @('--scenario-output', $ScenarioOutput)
}
if ($ScenarioContinueOnError) {
    $runArgs += '--scenario-continue-on-error'
}
if (-not (Test-HasExplicitBuiltInToolProbeOverride -arguments $ExtraArgs)) {
    $runArgs += '--enable-workspace-built-in-tool-output-probing'
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
