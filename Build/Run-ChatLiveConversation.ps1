# Runs one live scenario conversation (real auth + real tools) and validates transcript/tool-ledger quality.

[CmdletBinding()] param(
    [string] $ScenarioFile = '',
    [string] $ScenarioDir = '.\IntelligenceX.Chat\scenarios',
    [string] $ScenarioFilter = '*-10-turn.json',
    [string[]] $ScenarioTags = @('strict', 'live'),
    [int] $ExpectedTurns = 0,
    [string] $OutDir = '.\artifacts\chat-live',
    [string[]] $AllowRoot,
    [switch] $NoBuild,
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

function Get-ScenarioTurnCount([string] $path) {
    $raw = Get-Content -Path $path -Raw
    $trimmed = $raw.TrimStart()
    if ($trimmed.StartsWith('{') -or $trimmed.StartsWith('[')) {
        $json = $raw | ConvertFrom-Json -Depth 100
        if ($null -eq $json) {
            return 0
        }
        $turnsProperty = $json.PSObject.Properties['turns']
        if ($null -ne $turnsProperty -and $null -ne $turnsProperty.Value) {
            return @($turnsProperty.Value).Count
        }
        if ($json -is [System.Array]) {
            return @($json).Count
        }
        return 0
    }

    $count = 0
    foreach ($line in ($raw -split "`r?`n")) {
        $candidate = $line.Trim()
        if ($candidate.Length -eq 0) { continue }
        if ($candidate.StartsWith('#') -or $candidate.StartsWith('//')) { continue }
        if ($candidate.StartsWith('- ')) {
            $candidate = $candidate.Substring(2).Trim()
        }
        if ($candidate.Length -gt 0) {
            $count++
        }
    }
    return $count
}

$repoRoot = (Get-Item (Split-Path -Parent $MyInvocation.MyCommand.Path)).Parent.FullName
$runChatScript = Join-Path $repoRoot 'Build\Run-Chat.ps1'
if (-not (Test-Path $runChatScript)) {
    throw "Run script not found: $runChatScript"
}

$resolvedScenarioFile = ''
$scenarioSelectionReason = 'explicit'
if (-not [string]::IsNullOrWhiteSpace($ScenarioFile)) {
    $resolvedScenarioFile = Resolve-RepoRelativePath -repoRoot $repoRoot -pathValue $ScenarioFile
    if (-not (Test-Path $resolvedScenarioFile)) {
        throw "Scenario file not found: $resolvedScenarioFile"
    }
} else {
    $resolvedScenarioDir = Resolve-RepoRelativePath -repoRoot $repoRoot -pathValue $ScenarioDir
    if (-not (Test-Path $resolvedScenarioDir)) {
        throw "Scenario directory not found: $resolvedScenarioDir"
    }

    $requiredTags = @(Normalize-TagValues -values $ScenarioTags)
    $candidateFiles = @(Get-ChildItem -Path $resolvedScenarioDir -File -Filter $ScenarioFilter | Sort-Object Name)
    if ($candidateFiles.Count -eq 0) {
        throw "No scenario files matched '$ScenarioFilter' in '$resolvedScenarioDir'."
    }

    $selectedScenario = $null
    foreach ($candidate in $candidateFiles) {
        $candidateTags = @(Get-ScenarioTags -path $candidate.FullName)
        if (Test-ScenarioTagMatch -scenarioTags $candidateTags -requiredTags $requiredTags) {
            $selectedScenario = $candidate
            break
        }
    }

    if ($null -eq $selectedScenario) {
        if ($requiredTags.Count -gt 0) {
            throw "No scenario files matched '$ScenarioFilter' with tags '$($requiredTags -join ',')' in '$resolvedScenarioDir'."
        }
        throw "No scenario files selected in '$resolvedScenarioDir'."
    }

    $resolvedScenarioFile = $selectedScenario.FullName
    $scenarioSelectionReason = if ($requiredTags.Count -gt 0) {
        "auto-selected by tags ($($requiredTags -join ', '))"
    } else {
        "auto-selected by filter"
    }
}

$scenarioTurns = Get-ScenarioTurnCount -path $resolvedScenarioFile
if ($scenarioTurns -le 0) {
    throw "Scenario '$resolvedScenarioFile' does not contain any turns."
}
if ($ExpectedTurns -gt 0 -and $scenarioTurns -ne $ExpectedTurns) {
    throw "Scenario '$resolvedScenarioFile' has $scenarioTurns turns, expected $ExpectedTurns."
}

$resolvedOutDir = Resolve-RepoRelativePath -repoRoot $repoRoot -pathValue $OutDir
New-Item -ItemType Directory -Path $resolvedOutDir -Force | Out-Null

$beforeJson = @{}
Get-ChildItem -Path $resolvedOutDir -File -Filter '*.json' | ForEach-Object {
    $beforeJson[$_.FullName] = $_.LastWriteTimeUtc.Ticks
}

Write-Header 'Run Live Chat Conversation Harness'
Write-Step ("Scenario: {0}" -f $resolvedScenarioFile)
Write-Step ("Scenario selection: {0}" -f $scenarioSelectionReason)
if ($ExpectedTurns -gt 0) {
    Write-Step ("Expected turns: {0}" -f $ExpectedTurns)
} else {
    Write-Step ("Expected turns: auto (scenario has {0})" -f $scenarioTurns)
}
Write-Step ("Output dir: {0}" -f $resolvedOutDir)
Write-Step ("Continue on error: {0}" -f $ContinueOnError)

$runParams = @{
    ScenarioFile = $resolvedScenarioFile
    ScenarioOutput = $resolvedOutDir
    ScenarioContinueOnError = $ContinueOnError
    ParallelTools = [bool]$ParallelTools
    EchoToolOutputs = [bool]$EchoToolOutputs
    NoBuild = [bool]$NoBuild
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

$runSucceeded = $true
try {
    & $runChatScript @runParams
} catch {
    $runSucceeded = $false
    Write-Fail ("Host run failed: {0}" -f $_.Exception.Message)
}

$candidateJson = @(Get-ChildItem -Path $resolvedOutDir -File -Filter '*.json' |
    Where-Object {
        if (-not $beforeJson.ContainsKey($_.FullName)) {
            return $true
        }

        $previousTicks = [long]$beforeJson[$_.FullName]
        return $_.LastWriteTimeUtc.Ticks -gt $previousTicks
    } |
    Sort-Object LastWriteTimeUtc -Descending)
if ($candidateJson.Count -eq 0) {
    throw "No new scenario JSON artifact found in '$resolvedOutDir' for this run."
}

$reportPath = $candidateJson[0].FullName
$report = Get-Content -Path $reportPath -Raw | ConvertFrom-Json -Depth 100
if ($null -eq $report) {
    throw "Failed to parse scenario JSON artifact: $reportPath"
}

$turns = @($report.turns)
$totalTurns = [int]$report.total_turns
$passedTurns = [int]$report.passed_turns
$assertionFailures = 0
$pairingIssues = 0
$duplicateIssues = 0

foreach ($turn in $turns) {
    $failures = @($turn.assertion_failures)
    $assertionFailures += $failures.Count

    $callIds = @()
    foreach ($call in @($turn.tool_calls)) {
        $callId = "$($call.call_id)".Trim()
        if ($callId.Length -gt 0) {
            $callIds += $callId
        }
    }
    $outputIds = @()
    foreach ($output in @($turn.tool_outputs)) {
        $callId = "$($output.call_id)".Trim()
        if ($callId.Length -gt 0) {
            $outputIds += $callId
        }
    }

    $callSet = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $outputSet = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($id in $callIds) { [void]$callSet.Add($id) }
    foreach ($id in $outputIds) { [void]$outputSet.Add($id) }

    foreach ($id in $callSet) {
        if (-not $outputSet.Contains($id)) {
            $pairingIssues++
        }
    }
    foreach ($id in $outputSet) {
        if (-not $callSet.Contains($id)) {
            $pairingIssues++
        }
    }

    $callCounts = @{}
    foreach ($id in $callIds) {
        if (-not $callCounts.ContainsKey($id)) { $callCounts[$id] = 0 }
        $callCounts[$id]++
    }
    foreach ($entry in $callCounts.GetEnumerator()) {
        if ($entry.Value -gt 1) {
            $duplicateIssues++
        }
    }

    $outputCounts = @{}
    foreach ($id in $outputIds) {
        if (-not $outputCounts.ContainsKey($id)) { $outputCounts[$id] = 0 }
        $outputCounts[$id]++
    }
    foreach ($entry in $outputCounts.GetEnumerator()) {
        if ($entry.Value -gt 1) {
            $duplicateIssues++
        }
    }
}

$schemaVersion = "$($report.schema_version)"
$expectedTurnsMismatch = $ExpectedTurns -gt 0 -and $totalTurns -ne $ExpectedTurns

Write-Header 'Live Harness Summary'
Write-Step ("Schema: {0}" -f $schemaVersion)
Write-Step ("Turns passed: {0}/{1}" -f $passedTurns, $totalTurns)
Write-Step ("Assertion failures: {0}" -f $assertionFailures)
Write-Step ("Pairing issues (derived): {0}" -f $pairingIssues)
Write-Step ("Duplicate call-id issues (derived): {0}" -f $duplicateIssues)
Write-Step ("Artifact JSON: {0}" -f $reportPath)

$markdownPath = [System.IO.Path]::ChangeExtension($reportPath, '.md')
if (Test-Path $markdownPath) {
    Write-Step ("Artifact Markdown: {0}" -f $markdownPath)
}

$harnessFailed = $false
if (-not $runSucceeded) { $harnessFailed = $true }
if ($expectedTurnsMismatch) {
    $harnessFailed = $true
    Write-Fail ("Expected $ExpectedTurns turns but report has $totalTurns.")
}
if ($assertionFailures -gt 0) { $harnessFailed = $true }
if ($pairingIssues -gt 0) { $harnessFailed = $true }
if ($duplicateIssues -gt 0) { $harnessFailed = $true }
if ($passedTurns -lt $totalTurns) { $harnessFailed = $true }

if ($harnessFailed) {
    Write-Fail "Live harness failed quality checks."
    exit 1
}

Write-Step "Live harness passed quality checks."
exit 0
