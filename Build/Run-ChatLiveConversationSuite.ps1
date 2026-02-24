# Runs a repeatable batch of live chat conversations (real auth + real tools) and validates artifacts.

[CmdletBinding()] param(
    [string] $ScenarioDir = '.\IntelligenceX.Chat\scenarios',
    [string] $Filter = '*-10-turn.json',
    [string[]] $Tags = @('strict', 'live'),
    [int] $ExpectedTurns = 10,
    [string] $OutDir = '.\artifacts\chat-live',
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

function Write-Header([string] $text) { Write-Host "`n=== $text ===" -ForegroundColor Cyan }
function Write-Step([string] $text) { Write-Host "[+] $text" -ForegroundColor Yellow }
function Write-Fail([string] $text) { Write-Host "[x] $text" -ForegroundColor Red }
function Resolve-RepoRelativePath([string] $repoRoot, [string] $pathValue) {
    if ([string]::IsNullOrWhiteSpace($pathValue)) {
        throw "Path value is required."
    }

    if ([System.IO.Path]::IsPathRooted($pathValue)) {
        return [System.IO.Path]::GetFullPath($pathValue)
    }

    return [System.IO.Path]::GetFullPath((Join-Path $repoRoot $pathValue))
}

function Normalize-TagValues([string[]] $values) {
    $result = New-Object System.Collections.Generic.List[string]
    foreach ($value in @($values)) {
        $candidate = "$value".Trim().ToLowerInvariant()
        if ($candidate.Length -eq 0) {
            continue
        }
        if (-not $result.Contains($candidate)) {
            $result.Add($candidate) | Out-Null
        }
    }
    return @($result.ToArray())
}

function Get-ScenarioTags([string] $path) {
    try {
        $raw = Get-Content -Path $path -Raw
        $trimmed = $raw.TrimStart()
        if (-not ($trimmed.StartsWith('{') -or $trimmed.StartsWith('['))) {
            return @()
        }

        $json = $raw | ConvertFrom-Json -Depth 100
        if ($null -eq $json) {
            return @()
        }

        $tagsProperty = $json.PSObject.Properties['tags']
        if ($null -eq $tagsProperty -or $null -eq $tagsProperty.Value) {
            return @()
        }

        $rawTags = @($tagsProperty.Value | ForEach-Object { "$_" })
        return Normalize-TagValues -values $rawTags
    } catch {
        return @()
    }
}

function Test-ScenarioTagMatch([string[]] $scenarioTags, [string[]] $requiredTags) {
    $required = @($requiredTags)
    $scenario = @($scenarioTags)

    if ($required.Count -eq 0) {
        return $true
    }
    if ($scenario.Count -eq 0) {
        return $false
    }

    foreach ($tag in $required) {
        if (-not ($scenario -contains $tag)) {
            return $false
        }
    }
    return $true
}

$repoRoot = (Get-Item (Split-Path -Parent $MyInvocation.MyCommand.Path)).Parent.FullName
$liveHarnessScript = Join-Path $repoRoot 'Build\Run-ChatLiveConversation.ps1'
if (-not (Test-Path $liveHarnessScript)) {
    throw "Live harness script not found: $liveHarnessScript"
}

$resolvedScenarioDir = Resolve-RepoRelativePath -repoRoot $repoRoot -pathValue $ScenarioDir
if (-not (Test-Path $resolvedScenarioDir)) {
    throw "Scenario directory not found: $resolvedScenarioDir"
}

$resolvedOutDir = Resolve-RepoRelativePath -repoRoot $repoRoot -pathValue $OutDir
New-Item -ItemType Directory -Path $resolvedOutDir -Force | Out-Null

$requiredTags = @(Normalize-TagValues -values $Tags)
$candidateFiles = @(Get-ChildItem -Path $resolvedScenarioDir -File -Filter $Filter | Sort-Object Name)
if ($candidateFiles.Count -eq 0) {
    throw "No scenario files matched '$Filter' in '$resolvedScenarioDir'."
}

$scenarioRuns = New-Object System.Collections.Generic.List[object]
foreach ($file in $candidateFiles) {
    $scenarioTags = @(Get-ScenarioTags -path $file.FullName)
    if (-not (Test-ScenarioTagMatch -scenarioTags $scenarioTags -requiredTags $requiredTags)) {
        continue
    }

    $scenarioRuns.Add([pscustomobject]@{
        File = $file
        Tags = $scenarioTags
    }) | Out-Null
}

if ($scenarioRuns.Count -eq 0) {
    if ($requiredTags.Count -gt 0) {
        throw "No scenario files matched '$Filter' and tags '$($requiredTags -join ',')' in '$resolvedScenarioDir'."
    }
    throw "No scenario files selected in '$resolvedScenarioDir'."
}

Write-Header 'Run Live Chat Conversation Suite'
Write-Step ("Scenario dir: {0}" -f $resolvedScenarioDir)
Write-Step ("Filter: {0}" -f $Filter)
if ($requiredTags.Count -gt 0) {
    Write-Step ("Required tags: {0}" -f ($requiredTags -join ', '))
}
Write-Step ("Scenarios: {0}" -f $scenarioRuns.Count)
Write-Step ("Expected turns: {0}" -f $ExpectedTurns)
Write-Step ("Output dir: {0}" -f $resolvedOutDir)

$passed = 0
$failed = 0
$failedScenarios = New-Object System.Collections.Generic.List[string]

for ($i = 0; $i -lt $scenarioRuns.Count; $i++) {
    $scenarioRun = $scenarioRuns[$i]
    $scenario = $scenarioRun.File
    $scenarioNoBuild = $NoBuild -or ($i -gt 0)

    Write-Header ("Live Scenario {0}/{1}: {2}" -f ($i + 1), $scenarioRuns.Count, $scenario.Name)
    Write-Step ("NoBuild: {0}" -f $scenarioNoBuild)
    $scenarioRunTags = @($scenarioRun.Tags)
    if ($scenarioRunTags.Count -gt 0) {
        Write-Step ("Tags: {0}" -f ($scenarioRunTags -join ', '))
    }

    $runParams = @{
        ScenarioFile = $scenario.FullName
        ExpectedTurns = $ExpectedTurns
        OutDir = $resolvedOutDir
        ContinueOnError = $ContinueOnError
        ParallelTools = [bool]$ParallelTools
        EchoToolOutputs = [bool]$EchoToolOutputs
        NoBuild = $scenarioNoBuild
    }

    if ($AllowRoot -and $AllowRoot.Count -gt 0) {
        $runParams['AllowRoot'] = $AllowRoot
    }
    if ($EnablePowerShellPack) {
        $runParams['EnablePowerShellPack'] = $true
    }
    if ($EnableTestimoXPack) {
        $runParams['EnableTestimoXPack'] = $true
    }
    if ($EnableDnsClientXPack) {
        $runParams['EnableDnsClientXPack'] = $true
    }
    if ($DisableDnsClientXPack) {
        $runParams['DisableDnsClientXPack'] = $true
    }
    if ($EnableDomainDetectivePack) {
        $runParams['EnableDomainDetectivePack'] = $true
    }
    if ($DisableDomainDetectivePack) {
        $runParams['DisableDomainDetectivePack'] = $true
    }
    if (-not [string]::IsNullOrWhiteSpace($Model)) {
        $runParams['Model'] = $Model
    }
    if ($ExtraArgs -and $ExtraArgs.Count -gt 0) {
        $runParams['ExtraArgs'] = $ExtraArgs
    }

    $scenarioSucceeded = $true
    try {
        & $liveHarnessScript @runParams
    } catch {
        $scenarioSucceeded = $false
        Write-Fail ("Live scenario failed: {0}" -f $scenario.Name)
        Write-Fail $_.Exception.Message
    }

    if ($scenarioSucceeded) {
        $passed++
        Write-Step ("Live scenario passed: {0}" -f $scenario.Name)
    } else {
        $failed++
        $failedScenarios.Add($scenario.Name) | Out-Null
        if ($StopOnFailure) {
            Write-Fail "Stopping early because -StopOnFailure is set."
            break
        }
    }
}

Write-Header 'Live Suite Summary'
Write-Step ("Passed: {0}" -f $passed)
Write-Step ("Failed: {0}" -f $failed)
if ($failedScenarios.Count -gt 0) {
    Write-Fail ("Failed scenarios: {0}" -f ($failedScenarios -join ', '))
}
Write-Step ("Artifacts: {0}" -f $resolvedOutDir)

if ($failed -gt 0) {
    exit 1
}

exit 0
