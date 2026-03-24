# Runs strict chat quality checks in one command.
# - Strict scenario suite (required)
# - Optional live harness smoke run (opt-in)
# - Optional live harness suite run (opt-in, no hardcoded single scenario)

[CmdletBinding()] param(
    [string] $ScenarioDir = '.\IntelligenceX.Chat\scenarios',
    [string] $ScenarioFilter = '*-10-turn.json',
    [string[]] $ScenarioTags,
    [string] $ScenarioOutDir = '.\artifacts\chat-scenarios',
    [switch] $RunTransportRecoveryProfile,
    [string] $TransportRecoveryScenarioFilter = 'ad-*-10-turn.json',
    [string[]] $TransportRecoveryTags = @('ad', 'strict', 'transport-recovery'),
    [string] $TransportRecoveryOutDir = '.\artifacts\chat-scenarios-transport',
    [switch] $RunRecoveryUnitTests,
    [switch] $RunLiveHarness,
    [string] $LiveScenarioFile = '',
    [string] $LiveScenarioDir = '.\IntelligenceX.Chat\scenarios',
    [string] $LiveScenarioFilter = '*-10-turn.json',
    [string[]] $LiveScenarioTags = @('strict', 'live'),
    [int] $LiveExpectedTurns = 0,
    [string] $LiveOutDir = '.\artifacts\chat-live',
    [switch] $RunLiveHarnessSuite,
    [string] $LiveSuiteScenarioDir = '.\IntelligenceX.Chat\scenarios',
    [string] $LiveSuiteFilter = '*-10-turn.json',
    [string[]] $LiveSuiteTags = @('strict', 'live'),
    [int] $LiveSuiteExpectedTurns = 10,
    [string] $LiveSuiteOutDir = '.\artifacts\chat-live-suite',
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

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$scenarioSuiteScript = Join-Path $repoRoot 'Build\Chat\Run-ChatScenarioSuite.ps1'
$liveHarnessScript = Join-Path $repoRoot 'Build\Chat\Run-ChatLiveConversation.ps1'
$liveHarnessSuiteScript = Join-Path $repoRoot 'Build\Chat\Run-ChatLiveConversationSuite.ps1'

if (-not (Test-Path $scenarioSuiteScript)) {
    throw "Scenario suite script not found: $scenarioSuiteScript"
}
if (-not (Test-Path $liveHarnessScript)) {
    throw "Live harness script not found: $liveHarnessScript"
}
if (-not (Test-Path $liveHarnessSuiteScript)) {
    throw "Live harness suite script not found: $liveHarnessSuiteScript"
}
if ($RunLiveHarness -and $RunLiveHarnessSuite) {
    throw "Use only one live mode per run: -RunLiveHarness or -RunLiveHarnessSuite."
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
    Write-Step "Running recovery unit tests (transport/tool-pairing + duplicate-bubble guards)..."

    $chatTestsProject = Join-Path $repoRoot 'IntelligenceX.Chat\IntelligenceX.Chat.Tests\IntelligenceX.Chat.Tests.csproj'
    if (-not (Test-Path $chatTestsProject)) {
        throw "Chat tests project not found: $chatTestsProject"
    }
    $chatAppTestsProject = Join-Path $repoRoot 'IntelligenceX.Chat\IntelligenceX.Chat.App.Tests\IntelligenceX.Chat.App.Tests.csproj'
    if (-not (Test-Path $chatAppTestsProject)) {
        throw "Chat app tests project not found: $chatAppTestsProject"
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

    $appTestArgs = @(
        'test',
        $chatAppTestsProject,
        '-c', 'Release',
        '--filter', 'FullyQualifiedName~MainWindowNoTextWarningHandlingTests'
    )
    if ($NoBuild) {
        $appTestArgs += '--no-build'
    }

    & dotnet @appTestArgs
    if ($LASTEXITCODE -ne 0) {
        throw "Recovery unit tests failed (app duplicate-bubble guards)."
    }

    Write-Step "Recovery unit tests passed."
}

if ((-not $RunLiveHarness) -and (-not $RunLiveHarnessSuite)) {
    Write-Step "Strict scenario suite passed."
    Write-Step "Live harness run skipped. Use -RunLiveHarness (single scenario) or -RunLiveHarnessSuite (tag-driven suite)."
    exit 0
}

if ($RunLiveHarnessSuite) {
    Write-Step "Strict scenario suite passed."
    Write-Step "Running live harness suite..."

    $liveSuiteParams = @{
        ScenarioDir = $LiveSuiteScenarioDir
        Filter = $LiveSuiteFilter
        Tags = $LiveSuiteTags
        ExpectedTurns = $LiveSuiteExpectedTurns
        OutDir = $LiveSuiteOutDir
        ContinueOnError = $false
        ParallelTools = [bool]$ParallelTools
        EchoToolOutputs = [bool]$EchoToolOutputs
        NoBuild = $true
    }
    if ($AllowRoot -and $AllowRoot.Count -gt 0) {
        $liveSuiteParams['AllowRoot'] = $AllowRoot
    }
    if ($EnablePowerShellPack) {
        $liveSuiteParams['EnablePowerShellPack'] = $true
    }
    if ($EnableTestimoXPack) {
        $liveSuiteParams['EnableTestimoXPack'] = $true
    }
    if ($EnableDnsClientXPack) {
        $liveSuiteParams['EnableDnsClientXPack'] = $true
    }
    if ($DisableDnsClientXPack) {
        $liveSuiteParams['DisableDnsClientXPack'] = $true
    }
    if ($EnableDomainDetectivePack) {
        $liveSuiteParams['EnableDomainDetectivePack'] = $true
    }
    if ($DisableDomainDetectivePack) {
        $liveSuiteParams['DisableDomainDetectivePack'] = $true
    }
    if (-not [string]::IsNullOrWhiteSpace($Model)) {
        $liveSuiteParams['Model'] = $Model
    }
    if ($ExtraArgs -and $ExtraArgs.Count -gt 0) {
        $liveSuiteParams['ExtraArgs'] = $ExtraArgs
    }

    & $liveHarnessSuiteScript @liveSuiteParams
    Write-Step "Live harness suite passed."
    exit 0
}

Write-Step "Strict scenario suite passed."
Write-Step "Running live harness smoke scenario..."

$liveParams = @{
    ExpectedTurns = $LiveExpectedTurns
    OutDir = $LiveOutDir
    ContinueOnError = $false
    ParallelTools = [bool]$ParallelTools
    EchoToolOutputs = [bool]$EchoToolOutputs
    NoBuild = $true
}
if (-not [string]::IsNullOrWhiteSpace($LiveScenarioFile)) {
    $liveParams['ScenarioFile'] = $LiveScenarioFile
} else {
    $liveParams['ScenarioDir'] = $LiveScenarioDir
    $liveParams['ScenarioFilter'] = $LiveScenarioFilter
    $liveParams['ScenarioTags'] = $LiveScenarioTags
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
