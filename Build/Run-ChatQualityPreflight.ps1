# Runs strict chat quality checks in one command.
# - Strict scenario suite (required)
# - Optional live harness smoke run (opt-in)

[CmdletBinding()] param(
    [string] $ScenarioDir = '.\IntelligenceX.Chat\scenarios',
    [string] $ScenarioFilter = 'ad-*-10-turn.json',
    [string[]] $ScenarioTags,
    [string] $ScenarioOutDir = '.\artifacts\chat-scenarios',
    [switch] $RunTransportRecoveryProfile,
    [string] $TransportRecoveryScenarioFilter = 'ad-*-10-turn.json',
    [string[]] $TransportRecoveryTags = @('ad', 'strict', 'transport-recovery'),
    [string] $TransportRecoveryOutDir = '.\artifacts\chat-scenarios-transport',
    [switch] $RunRecoveryUnitTests,
    [switch] $RunLiveHarness,
    [string] $LiveScenarioFile = '.\IntelligenceX.Chat\scenarios\ad-cross-dc-followthrough-10-turn.json',
    [int] $LiveExpectedTurns = 10,
    [string] $LiveOutDir = '.\artifacts\chat-live',
    [string[]] $AllowRoot,
    [switch] $NoBuild,
    [switch] $ParallelTools = $true,
    [switch] $EchoToolOutputs = $true,
    [switch] $EnablePowerShellPack,
    [switch] $EnableTestimoXPack,
    [switch] $EnableDnsClientXPack,
    [switch] $DisableDnsClientXPack,
    [switch] $EnableDomainDetectivePack,
    [switch] $DisableDomainDetectivePack,
    [string] $Model,
    [string[]] $ExtraArgs
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Header([string] $text) { Write-Host "`n=== $text ===" -ForegroundColor Cyan }
function Write-Step([string] $text) { Write-Host "[+] $text" -ForegroundColor Yellow }

$repoRoot = (Get-Item (Split-Path -Parent $MyInvocation.MyCommand.Path)).Parent.FullName
$scenarioSuiteScript = Join-Path $repoRoot 'Build\Run-ChatScenarioSuite.ps1'
$liveHarnessScript = Join-Path $repoRoot 'Build\Run-ChatLiveConversation.ps1'

if (-not (Test-Path $scenarioSuiteScript)) {
    throw "Scenario suite script not found: $scenarioSuiteScript"
}
if (-not (Test-Path $liveHarnessScript)) {
    throw "Live harness script not found: $liveHarnessScript"
}

Write-Header 'IX Chat Quality Preflight'
Write-Step "Running strict scenario suite..."

$suiteParams = @{
    ScenarioDir = $ScenarioDir
    Filter = $ScenarioFilter
    OutDir = $ScenarioOutDir
    ContinueOnError = $false
    ParallelTools = [bool]$ParallelTools
    EchoToolOutputs = [bool]$EchoToolOutputs
    NoBuild = [bool]$NoBuild
}
if ($ScenarioTags -and $ScenarioTags.Count -gt 0) {
    $suiteParams['Tags'] = $ScenarioTags
}
if ($AllowRoot -and $AllowRoot.Count -gt 0) {
    $suiteParams['AllowRoot'] = $AllowRoot
}
if ($EnablePowerShellPack) {
    $suiteParams['EnablePowerShellPack'] = $true
}
if ($EnableTestimoXPack) {
    $suiteParams['EnableTestimoXPack'] = $true
}
if ($EnableDnsClientXPack) {
    $suiteParams['EnableDnsClientXPack'] = $true
}
if ($DisableDnsClientXPack) {
    $suiteParams['DisableDnsClientXPack'] = $true
}
if ($EnableDomainDetectivePack) {
    $suiteParams['EnableDomainDetectivePack'] = $true
}
if ($DisableDomainDetectivePack) {
    $suiteParams['DisableDomainDetectivePack'] = $true
}
if (-not [string]::IsNullOrWhiteSpace($Model)) {
    $suiteParams['Model'] = $Model
}
if ($ExtraArgs -and $ExtraArgs.Count -gt 0) {
    $suiteParams['ExtraArgs'] = $ExtraArgs
}

& $scenarioSuiteScript @suiteParams

if ($RunTransportRecoveryProfile) {
    $transportTags = @($TransportRecoveryTags | ForEach-Object { "$_".Trim() } | Where-Object { $_.Length -gt 0 })
    if ($transportTags.Count -eq 0) {
        throw "Transport-recovery profile requires at least one tag. Pass -TransportRecoveryTags with non-empty values."
    }

    Write-Step "Strict scenario suite passed."
    Write-Step "Running transport-recovery strict profile..."
    Write-Step ("Transport tags: {0}" -f ($transportTags -join ', '))

    $transportParams = @{
        ScenarioDir = $ScenarioDir
        Filter = $TransportRecoveryScenarioFilter
        Tags = $transportTags
        OutDir = $TransportRecoveryOutDir
        ContinueOnError = $false
        ParallelTools = [bool]$ParallelTools
        EchoToolOutputs = [bool]$EchoToolOutputs
        NoBuild = $true
    }
    if ($AllowRoot -and $AllowRoot.Count -gt 0) {
        $transportParams['AllowRoot'] = $AllowRoot
    }
    if ($EnablePowerShellPack) {
        $transportParams['EnablePowerShellPack'] = $true
    }
    if ($EnableTestimoXPack) {
        $transportParams['EnableTestimoXPack'] = $true
    }
    if ($EnableDnsClientXPack) {
        $transportParams['EnableDnsClientXPack'] = $true
    }
    if ($DisableDnsClientXPack) {
        $transportParams['DisableDnsClientXPack'] = $true
    }
    if ($EnableDomainDetectivePack) {
        $transportParams['EnableDomainDetectivePack'] = $true
    }
    if ($DisableDomainDetectivePack) {
        $transportParams['DisableDomainDetectivePack'] = $true
    }
    if (-not [string]::IsNullOrWhiteSpace($Model)) {
        $transportParams['Model'] = $Model
    }
    if ($ExtraArgs -and $ExtraArgs.Count -gt 0) {
        $transportParams['ExtraArgs'] = $ExtraArgs
    }

    & $scenarioSuiteScript @transportParams
    Write-Step "Transport-recovery profile passed."
}

if ($RunRecoveryUnitTests) {
    Write-Step "Strict scenario suite passed."
    Write-Step "Running recovery unit tests (transport/tool-pairing guards)..."

    $chatTestsProject = Join-Path $repoRoot 'IntelligenceX.Chat\IntelligenceX.Chat.Tests\IntelligenceX.Chat.Tests.csproj'
    if (-not (Test-Path $chatTestsProject)) {
        throw "Chat tests project not found: $chatTestsProject"
    }

    $testArgs = @(
        'test',
        $chatTestsProject,
        '-c', 'Release',
        '--filter', 'FullyQualifiedName~ChatSchemaRecoveryFallbackTests|FullyQualifiedName~HostScenarioAssertionTests|FullyQualifiedName~BuildToolRoundReplayInput_'
    )
    if ($NoBuild) {
        $testArgs += '--no-build'
    }

    & dotnet @testArgs
    if ($LASTEXITCODE -ne 0) {
        throw "Recovery unit tests failed."
    }

    Write-Step "Recovery unit tests passed."
}

if (-not $RunLiveHarness) {
    Write-Step "Strict scenario suite passed."
    Write-Step "Live harness smoke run skipped. Use -RunLiveHarness to include it."
    exit 0
}

Write-Step "Strict scenario suite passed."
Write-Step "Running live harness smoke scenario..."

$liveParams = @{
    ScenarioFile = $LiveScenarioFile
    ExpectedTurns = $LiveExpectedTurns
    OutDir = $LiveOutDir
    ContinueOnError = $false
    ParallelTools = [bool]$ParallelTools
    EchoToolOutputs = [bool]$EchoToolOutputs
    NoBuild = $true
}
if ($AllowRoot -and $AllowRoot.Count -gt 0) {
    $liveParams['AllowRoot'] = $AllowRoot
}
if ($EnablePowerShellPack) {
    $liveParams['EnablePowerShellPack'] = $true
}
if ($EnableTestimoXPack) {
    $liveParams['EnableTestimoXPack'] = $true
}
if ($EnableDnsClientXPack) {
    $liveParams['EnableDnsClientXPack'] = $true
}
if ($DisableDnsClientXPack) {
    $liveParams['DisableDnsClientXPack'] = $true
}
if ($EnableDomainDetectivePack) {
    $liveParams['EnableDomainDetectivePack'] = $true
}
if ($DisableDomainDetectivePack) {
    $liveParams['DisableDomainDetectivePack'] = $true
}
if (-not [string]::IsNullOrWhiteSpace($Model)) {
    $liveParams['Model'] = $Model
}
if ($ExtraArgs -and $ExtraArgs.Count -gt 0) {
    $liveParams['ExtraArgs'] = $ExtraArgs
}

& $liveHarnessScript @liveParams

Write-Step "Live harness smoke scenario passed."
exit 0
