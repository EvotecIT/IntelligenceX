# Runs long-run compaction/soak chat scenarios using the real host runtime.

[CmdletBinding()] param(
    [string] $ScenarioDir = '.\IntelligenceX.Chat\scenarios',
    [string] $Filter = 'ad-compaction-soak-*.json',
    [string[]] $Tags = @('ad', 'strict', 'compaction', 'soak'),
    [string] $OutDir = '.\artifacts\chat-compaction-soak',
    [string[]] $AllowRoot,
    [switch] $NoBuild,
    [switch] $StopOnFailure,
    [bool] $ContinueOnError = $false,
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

$repoRoot = (Get-Item (Split-Path -Parent $MyInvocation.MyCommand.Path)).Parent.FullName
$scenarioSuiteScript = Join-Path $repoRoot 'Build\Run-ChatScenarioSuite.ps1'
if (-not (Test-Path $scenarioSuiteScript)) {
    throw "Scenario suite script not found: $scenarioSuiteScript"
}

$params = @{
    ScenarioDir = $ScenarioDir
    Filter = $Filter
    Tags = $Tags
    OutDir = $OutDir
    ContinueOnError = $ContinueOnError
    ParallelTools = [bool]$ParallelTools
    EchoToolOutputs = [bool]$EchoToolOutputs
}

if ($AllowRoot -and $AllowRoot.Count -gt 0) {
    $params['AllowRoot'] = $AllowRoot
}
if ($NoBuild) {
    $params['NoBuild'] = $true
}
if ($StopOnFailure) {
    $params['StopOnFailure'] = $true
}
if ($EnablePowerShellPack) {
    $params['EnablePowerShellPack'] = $true
}
if ($EnableTestimoXPack) {
    $params['EnableTestimoXPack'] = $true
}
if ($EnableDnsClientXPack) {
    $params['EnableDnsClientXPack'] = $true
}
if ($DisableDnsClientXPack) {
    $params['DisableDnsClientXPack'] = $true
}
if ($EnableDomainDetectivePack) {
    $params['EnableDomainDetectivePack'] = $true
}
if ($DisableDomainDetectivePack) {
    $params['DisableDomainDetectivePack'] = $true
}
if (-not [string]::IsNullOrWhiteSpace($Model)) {
    $params['Model'] = $Model
}
if ($ExtraArgs -and $ExtraArgs.Count -gt 0) {
    $params['ExtraArgs'] = $ExtraArgs
}

& $scenarioSuiteScript @params
