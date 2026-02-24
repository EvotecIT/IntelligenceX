# Summarizes IX Chat scenario coverage and strictness metadata.

[CmdletBinding()] param(
    [string] $ScenarioDir = '.\IntelligenceX.Chat\scenarios',
    [string] $Filter = 'ad-*-10-turn.json',
    [string] $OutFile
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Resolve-RepoRelativePath([string] $repoRoot, [string] $pathValue) {
    if ([string]::IsNullOrWhiteSpace($pathValue)) {
        throw "Path value is required."
    }

    if ([System.IO.Path]::IsPathRooted($pathValue)) {
        return [System.IO.Path]::GetFullPath($pathValue)
    }

    return [System.IO.Path]::GetFullPath((Join-Path $repoRoot $pathValue))
}

function Get-NormalizedStringList([object] $rawValue) {
    $result = New-Object System.Collections.Generic.List[string]
    foreach ($value in @($rawValue)) {
        $candidate = "$value".Trim()
        if ($candidate.Length -eq 0) {
            continue
        }
        $result.Add($candidate) | Out-Null
    }
    return @($result.ToArray())
}

function Get-JsonPropertyValue([object] $instance, [string] $propertyName, [object] $defaultValue = $null) {
    if ($null -eq $instance) {
        return $defaultValue
    }

    $property = $instance.PSObject.Properties[$propertyName]
    if ($null -eq $property) {
        return $defaultValue
    }

    return $property.Value
}

function Has-ToolContract([object] $turn) {
    $minToolCalls = 0
    $minToolRounds = 0
    $minToolCallsValue = Get-JsonPropertyValue -instance $turn -propertyName 'min_tool_calls'
    $minToolRoundsValue = Get-JsonPropertyValue -instance $turn -propertyName 'min_tool_rounds'
    if ($null -ne $minToolCallsValue) {
        [void][int]::TryParse("$minToolCallsValue", [ref]$minToolCalls)
    }
    if ($null -ne $minToolRoundsValue) {
        [void][int]::TryParse("$minToolRoundsValue", [ref]$minToolRounds)
    }

    if ($minToolCalls -gt 0 -or $minToolRounds -gt 0) {
        return $true
    }

    $requireTools = @(Get-NormalizedStringList -rawValue (Get-JsonPropertyValue -instance $turn -propertyName 'require_tools'))
    $requireAnyTools = @(Get-NormalizedStringList -rawValue (Get-JsonPropertyValue -instance $turn -propertyName 'require_any_tools'))
    $assertToolOutputContains = @(Get-NormalizedStringList -rawValue (Get-JsonPropertyValue -instance $turn -propertyName 'assert_tool_output_contains'))
    $assertToolOutputNotContains = @(Get-NormalizedStringList -rawValue (Get-JsonPropertyValue -instance $turn -propertyName 'assert_tool_output_not_contains'))
    $forbidToolErrorCodes = @(Get-NormalizedStringList -rawValue (Get-JsonPropertyValue -instance $turn -propertyName 'forbid_tool_error_codes'))
    $assertNoToolErrors = [bool](Get-JsonPropertyValue -instance $turn -propertyName 'assert_no_tool_errors' -defaultValue $false)

    return $requireTools.Count -gt 0 `
        -or $requireAnyTools.Count -gt 0 `
        -or $assertToolOutputContains.Count -gt 0 `
        -or $assertToolOutputNotContains.Count -gt 0 `
        -or $forbidToolErrorCodes.Count -gt 0 `
        -or $assertNoToolErrors
}

function Contains-ToolPattern([object] $turn, [string] $token) {
    $requireTools = @(Get-NormalizedStringList -rawValue (Get-JsonPropertyValue -instance $turn -propertyName 'require_tools'))
    $requireAnyTools = @(Get-NormalizedStringList -rawValue (Get-JsonPropertyValue -instance $turn -propertyName 'require_any_tools'))
    foreach ($pattern in @($requireTools + $requireAnyTools)) {
        if ($pattern.IndexOf($token, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
            return $true
        }
    }
    return $false
}

function Is-StrictDefaultSet([object] $defaults) {
    if ($null -eq $defaults) {
        return $false
    }

    return [bool](Get-JsonPropertyValue -instance $defaults -propertyName 'assert_clean_completion' -defaultValue $false) `
        -and [bool](Get-JsonPropertyValue -instance $defaults -propertyName 'assert_tool_call_output_pairing' -defaultValue $false) `
        -and [bool](Get-JsonPropertyValue -instance $defaults -propertyName 'assert_no_duplicate_tool_call_ids' -defaultValue $false) `
        -and [bool](Get-JsonPropertyValue -instance $defaults -propertyName 'assert_no_duplicate_tool_output_call_ids' -defaultValue $false) `
        -and ([int](Get-JsonPropertyValue -instance $defaults -propertyName 'max_no_tool_execution_retries' -defaultValue -1) -eq 0) `
        -and ([int](Get-JsonPropertyValue -instance $defaults -propertyName 'max_duplicate_tool_call_signatures' -defaultValue -1) -eq 1)
}

$repoRoot = (Get-Item (Split-Path -Parent $MyInvocation.MyCommand.Path)).Parent.FullName
$resolvedScenarioDir = Resolve-RepoRelativePath -repoRoot $repoRoot -pathValue $ScenarioDir
if (-not (Test-Path $resolvedScenarioDir)) {
    throw "Scenario directory not found: $resolvedScenarioDir"
}

$files = @(Get-ChildItem -Path $resolvedScenarioDir -File -Filter $Filter | Sort-Object Name)
if ($files.Count -eq 0) {
    throw "No scenario files matched '$Filter' in '$resolvedScenarioDir'."
}

$rows = New-Object System.Collections.Generic.List[object]
foreach ($file in $files) {
    $json = Get-Content -Path $file.FullName -Raw | ConvertFrom-Json -Depth 100
    $turns = @(Get-JsonPropertyValue -instance $json -propertyName 'turns')
    $turnCount = $turns.Count
    $toolContractTurns = 0
    $adTurns = 0
    $eventLogTurns = 0
    $crossDcTurns = 0

    foreach ($turn in $turns) {
        if (Has-ToolContract -turn $turn) {
            $toolContractTurns++
        }
        if (Contains-ToolPattern -turn $turn -token "ad_") {
            $adTurns++
        }
        if (Contains-ToolPattern -turn $turn -token "eventlog_") {
            $eventLogTurns++
        }
        $user = "$(Get-JsonPropertyValue -instance $turn -propertyName 'user')"
        if ($user -match '(?i)all other|all remaining|every remaining|cross-DC|cross DC|those DCs') {
            $crossDcTurns++
        }
    }

    $tags = @(Get-NormalizedStringList -rawValue (Get-JsonPropertyValue -instance $json -propertyName 'tags'))
    $strictDefaults = Is-StrictDefaultSet -defaults (Get-JsonPropertyValue -instance $json -propertyName 'defaults')
    $rows.Add([pscustomobject]@{
        ScenarioName = "$(Get-JsonPropertyValue -instance $json -propertyName 'name')"
        File = $file.Name
        Turns = $turnCount
        ToolContractTurns = $toolContractTurns
        AdTurns = $adTurns
        EventLogTurns = $eventLogTurns
        CrossDcTurns = $crossDcTurns
        StrictDefaults = $strictDefaults
        Tags = ($tags -join ',')
    }) | Out-Null
}

Write-Host "`n=== Chat Scenario Coverage ===" -ForegroundColor Cyan
$rows | Format-Table ScenarioName, Turns, ToolContractTurns, AdTurns, EventLogTurns, CrossDcTurns, StrictDefaults, Tags -AutoSize

if (-not [string]::IsNullOrWhiteSpace($OutFile)) {
    $resolvedOutFile = Resolve-RepoRelativePath -repoRoot $repoRoot -pathValue $OutFile
    $outDir = Split-Path -Parent $resolvedOutFile
    if (-not [string]::IsNullOrWhiteSpace($outDir)) {
        New-Item -ItemType Directory -Path $outDir -Force | Out-Null
    }

    $lines = New-Object System.Collections.Generic.List[string]
    $lines.Add("# Chat Scenario Coverage") | Out-Null
    $lines.Add("") | Out-Null
    $lines.Add("| Scenario | Turns | ToolContractTurns | AdTurns | EventLogTurns | CrossDcTurns | StrictDefaults | Tags |") | Out-Null
    $lines.Add("|---|---:|---:|---:|---:|---:|:---:|---|") | Out-Null
    foreach ($row in $rows) {
        $lines.Add(
            ("| {0} | {1} | {2} | {3} | {4} | {5} | {6} | {7} |" -f
                $row.ScenarioName,
                $row.Turns,
                $row.ToolContractTurns,
                $row.AdTurns,
                $row.EventLogTurns,
                $row.CrossDcTurns,
                $(if ($row.StrictDefaults) { "yes" } else { "no" }),
                $row.Tags)) | Out-Null
    }

    Set-Content -Path $resolvedOutFile -Value $lines -Encoding UTF8
    Write-Host ("[+] Coverage markdown written: {0}" -f $resolvedOutFile) -ForegroundColor Yellow
}

exit 0
