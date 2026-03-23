# Compares two chat scenario JSON artifacts and reports turn-level deltas.

[CmdletBinding()] param(
    [Parameter(Mandatory = $true)]
    [string] $BaseReport,
    [Parameter(Mandatory = $true)]
    [string] $CurrentReport,
    [switch] $FailOnRegression
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Header([string] $text) { Write-Host "`n=== $text ===" -ForegroundColor Cyan }
function Write-Step([string] $text) { Write-Host "[+] $text" -ForegroundColor Yellow }
function Write-Fail([string] $text) { Write-Host "[x] $text" -ForegroundColor Red }

function Resolve-FullPath([string] $pathValue) {
    if ([string]::IsNullOrWhiteSpace($pathValue)) {
        throw "Path is required."
    }

    return [System.IO.Path]::GetFullPath($pathValue)
}

function Get-ArrayCount($value) {
    if ($null -eq $value) {
        return 0
    }
    return @($value).Count
}

function Get-TurnMap($turns) {
    $map = @{}
    foreach ($turn in @($turns)) {
        $index = [int]$turn.index
        $map[$index] = $turn
    }
    return $map
}

$basePath = Resolve-FullPath -pathValue $BaseReport
$currentPath = Resolve-FullPath -pathValue $CurrentReport
if (-not (Test-Path $basePath)) {
    throw "Base report not found: $basePath"
}
if (-not (Test-Path $currentPath)) {
    throw "Current report not found: $currentPath"
}

$base = Get-Content -Path $basePath -Raw | ConvertFrom-Json -Depth 100
$current = Get-Content -Path $currentPath -Raw | ConvertFrom-Json -Depth 100
if ($null -eq $base -or $null -eq $current) {
    throw "Failed to parse one or both JSON reports."
}

$basePassed = [int]$base.passed_turns
$baseTotal = [int]$base.total_turns
$currentPassed = [int]$current.passed_turns
$currentTotal = [int]$current.total_turns

Write-Header "Scenario Report Diff"
Write-Step ("Base: {0} ({1}/{2} turns passed)" -f $basePath, $basePassed, $baseTotal)
Write-Step ("Current: {0} ({1}/{2} turns passed)" -f $currentPath, $currentPassed, $currentTotal)

$baseMap = Get-TurnMap -turns $base.turns
$currentMap = Get-TurnMap -turns $current.turns
$allIndexes = @($baseMap.Keys + $currentMap.Keys | Sort-Object -Unique)

$regressions = New-Object System.Collections.Generic.List[string]
$improvements = New-Object System.Collections.Generic.List[string]
$neutral = New-Object System.Collections.Generic.List[string]

foreach ($index in $allIndexes) {
    $hasBase = $baseMap.ContainsKey($index)
    $hasCurrent = $currentMap.ContainsKey($index)
    if (-not $hasCurrent) {
        $regressions.Add(("Turn {0}: missing in current report." -f $index)) | Out-Null
        continue
    }
    if (-not $hasBase) {
        $improvements.Add(("Turn {0}: new turn present in current report." -f $index)) | Out-Null
        continue
    }

    $baseTurn = $baseMap[$index]
    $currentTurn = $currentMap[$index]
    $label = "$($currentTurn.label)"
    if ([string]::IsNullOrWhiteSpace($label)) {
        $label = "$($baseTurn.label)"
    }
    if ([string]::IsNullOrWhiteSpace($label)) {
        $label = "Turn $index"
    }

    $baseSuccess = [bool]$baseTurn.success
    $currentSuccess = [bool]$currentTurn.success
    $baseFailures = Get-ArrayCount -value $baseTurn.assertion_failures
    $currentFailures = Get-ArrayCount -value $currentTurn.assertion_failures
    $baseCalls = Get-ArrayCount -value $baseTurn.tool_calls
    $currentCalls = Get-ArrayCount -value $currentTurn.tool_calls
    $baseOutputs = Get-ArrayCount -value $baseTurn.tool_outputs
    $currentOutputs = Get-ArrayCount -value $currentTurn.tool_outputs

    if ($baseSuccess -and -not $currentSuccess) {
        $regressions.Add(("{0} ({1}): success regressed true -> false." -f $index, $label)) | Out-Null
        continue
    }
    if (-not $baseSuccess -and $currentSuccess) {
        $improvements.Add(("{0} ({1}): success improved false -> true." -f $index, $label)) | Out-Null
        continue
    }
    if ($currentFailures -gt $baseFailures) {
        $regressions.Add(("{0} ({1}): assertion failures increased {2} -> {3}." -f $index, $label, $baseFailures, $currentFailures)) | Out-Null
        continue
    }
    if ($currentFailures -lt $baseFailures) {
        $improvements.Add(("{0} ({1}): assertion failures decreased {2} -> {3}." -f $index, $label, $baseFailures, $currentFailures)) | Out-Null
        continue
    }

    $neutral.Add(("{0} ({1}): stable (calls {2}->{3}, outputs {4}->{5}, failures {6}->{7})." -f $index, $label, $baseCalls, $currentCalls, $baseOutputs, $currentOutputs, $baseFailures, $currentFailures)) | Out-Null
}

Write-Header "Diff Summary"
Write-Step ("Improvements: {0}" -f $improvements.Count)
Write-Step ("Regressions: {0}" -f $regressions.Count)
Write-Step ("Stable turns: {0}" -f $neutral.Count)

if ($improvements.Count -gt 0) {
    Write-Header "Improvements"
    foreach ($line in $improvements) {
        Write-Step $line
    }
}
if ($regressions.Count -gt 0) {
    Write-Header "Regressions"
    foreach ($line in $regressions) {
        Write-Fail $line
    }
}

if ($FailOnRegression -and $regressions.Count -gt 0) {
    exit 1
}

exit 0
