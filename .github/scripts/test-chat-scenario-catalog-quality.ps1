# Validates chat scenario catalog quality contracts without requiring live model execution.

[CmdletBinding()] param(
    [string] $ScenarioDir = '.\IntelligenceX.Chat\scenarios',
    [string] $Filter = '*-10-turn.json'
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

function Get-TagSet([object] $scenario) {
    return [System.Collections.Generic.HashSet[string]]::new(
        [string[]](Get-NormalizedStringList -rawValue (Get-JsonPropertyValue -instance $scenario -propertyName 'tags')),
        [System.StringComparer]::OrdinalIgnoreCase)
}

function Has-PatternToken([string[]] $patterns, [string] $token) {
    foreach ($pattern in @($patterns)) {
        if ($pattern.IndexOf($token, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
            return $true
        }
    }
    return $false
}

function Is-StrictDefaults([object] $defaults) {
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

function Turn-HasToolContract([object] $turn) {
    $minToolCalls = [int](Get-JsonPropertyValue -instance $turn -propertyName 'min_tool_calls' -defaultValue 0)
    $minToolRounds = [int](Get-JsonPropertyValue -instance $turn -propertyName 'min_tool_rounds' -defaultValue 0)
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

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoCursor = Get-Item $scriptDir
while ($null -ne $repoCursor -and -not (Test-Path (Join-Path $repoCursor.FullName 'IntelligenceX.Chat'))) {
    $repoCursor = $repoCursor.Parent
}
if ($null -eq $repoCursor) {
    throw "Could not resolve repository root from script path '$scriptDir'."
}

$repoRoot = $repoCursor.FullName
$resolvedScenarioDir = Resolve-RepoRelativePath -repoRoot $repoRoot -pathValue $ScenarioDir
if (-not (Test-Path $resolvedScenarioDir)) {
    throw "Scenario directory not found: $resolvedScenarioDir"
}

$files = @(Get-ChildItem -Path $resolvedScenarioDir -File -Filter $Filter | Sort-Object Name)
if ($files.Count -eq 0) {
    throw "No scenario files matched '$Filter' in '$resolvedScenarioDir'."
}

$usedDefaultFilter = -not $PSBoundParameters.ContainsKey('Filter')
$selectedMixedDomainAmbiguityScenarioCount = @(
    $files | Where-Object {
        $_.Name.StartsWith("mixed-domain-ambiguity-", [StringComparison]::OrdinalIgnoreCase)
    }
).Count

$failures = New-Object System.Collections.Generic.List[string]
$summaries = New-Object System.Collections.Generic.List[string]
$mixedDomainAmbiguityScenarioCount = 0

foreach ($file in $files) {
    try {
        $scenario = Get-Content -Path $file.FullName -Raw | ConvertFrom-Json -Depth 100
    } catch {
        $failures.Add("$($file.Name): invalid JSON ($($_.Exception.Message))") | Out-Null
        continue
    }

    $scenarioName = "$(Get-JsonPropertyValue -instance $scenario -propertyName 'name')".Trim()
    if ($scenarioName.Length -eq 0) {
        $failures.Add("$($file.Name): missing non-empty scenario name.") | Out-Null
    }

    $tags = Get-TagSet -scenario $scenario
    if (-not $tags.Contains("strict")) {
        $failures.Add("$($file.Name): missing required 'strict' tag.") | Out-Null
    }
    if (-not $tags.Contains("live")) {
        $failures.Add("$($file.Name): missing required 'live' tag.") | Out-Null
    }

    if (-not (Is-StrictDefaults -defaults (Get-JsonPropertyValue -instance $scenario -propertyName 'defaults'))) {
        $failures.Add("$($file.Name): defaults do not enforce strict scenario guardrails.") | Out-Null
    }

    $turns = @(Get-JsonPropertyValue -instance $scenario -propertyName 'turns')
    if ($turns.Count -ne 10) {
        $failures.Add("$($file.Name): expected exactly 10 turns, found $($turns.Count).") | Out-Null
        continue
    }

    $toolContractTurns = 0
    $requiredPatterns = New-Object System.Collections.Generic.List[string]
    $forbiddenPatterns = New-Object System.Collections.Generic.List[string]

    for ($i = 0; $i -lt $turns.Count; $i++) {
        $turn = $turns[$i]
        $userText = "$(Get-JsonPropertyValue -instance $turn -propertyName 'user')".Trim()
        if ($userText.Length -eq 0) {
            $failures.Add("$($file.Name): turn $($i + 1) is missing user text.") | Out-Null
        }
        if (Turn-HasToolContract -turn $turn) {
            $toolContractTurns++
        }

        foreach ($pattern in @(Get-NormalizedStringList -rawValue (Get-JsonPropertyValue -instance $turn -propertyName 'require_tools'))) {
            $requiredPatterns.Add($pattern) | Out-Null
        }
        foreach ($pattern in @(Get-NormalizedStringList -rawValue (Get-JsonPropertyValue -instance $turn -propertyName 'require_any_tools'))) {
            $requiredPatterns.Add($pattern) | Out-Null
        }
        foreach ($pattern in @(Get-NormalizedStringList -rawValue (Get-JsonPropertyValue -instance $turn -propertyName 'forbid_tools'))) {
            $forbiddenPatterns.Add($pattern) | Out-Null
        }
    }

    if ($toolContractTurns -eq 0) {
        $failures.Add("$($file.Name): expected at least one turn with a tool execution contract.") | Out-Null
    }

    $requiredPatternList = @($requiredPatterns.ToArray())
    $forbiddenPatternList = @($forbiddenPatterns.ToArray())

    if ($file.Name.StartsWith("ad-", [StringComparison]::OrdinalIgnoreCase)) {
        if (-not $tags.Contains("ad")) {
            $failures.Add("$($file.Name): AD scenario must include 'ad' tag.") | Out-Null
        }
        if (-not (Has-PatternToken -patterns $requiredPatternList -token "ad_")) {
            $failures.Add("$($file.Name): AD scenario must include at least one ad_* required tool pattern.") | Out-Null
        }

        if ($file.Name.Equals("ad-reboot-local-10-turn.json", [StringComparison]::OrdinalIgnoreCase)) {
            $crossCheckTurn = $null
            foreach ($turn in $turns) {
                $turnName = "$(Get-JsonPropertyValue -instance $turn -propertyName 'name')".Trim()
                if ($turnName.Equals("Cross-check peer DCs", [StringComparison]::OrdinalIgnoreCase)) {
                    $crossCheckTurn = $turn
                    break
                }
            }

            if ($null -eq $crossCheckTurn) {
                $failures.Add("$($file.Name): expected 'Cross-check peer DCs' turn for cross-DC reboot validation.") | Out-Null
            } else {
                $crossCheckMinToolCalls = [int](Get-JsonPropertyValue -instance $crossCheckTurn -propertyName 'min_tool_calls' -defaultValue 0)
                if ($crossCheckMinToolCalls -lt 2) {
                    $failures.Add("$($file.Name): 'Cross-check peer DCs' must set min_tool_calls >= 2.") | Out-Null
                }

                $minimumDistinct = Get-JsonPropertyValue -instance $crossCheckTurn -propertyName 'min_distinct_tool_input_values'
                $minimumMachineName = [int](Get-JsonPropertyValue -instance $minimumDistinct -propertyName 'machine_name' -defaultValue 0)
                if ($minimumMachineName -lt 2) {
                    $failures.Add("$($file.Name): 'Cross-check peer DCs' must enforce min_distinct_tool_input_values.machine_name >= 2.") | Out-Null
                }
            }
        }

        if ($file.Name.Equals("ad-scope-shift-cross-dc-fanout-10-turn.json", [StringComparison]::OrdinalIgnoreCase)) {
            $confirmedFanoutTurn = $null
            $continueNonAd0Turn = $null
            foreach ($turn in $turns) {
                $turnName = "$(Get-JsonPropertyValue -instance $turn -propertyName 'name')".Trim()
                if ($turnName.Equals("Confirmed fanout execution", [StringComparison]::OrdinalIgnoreCase)) {
                    $confirmedFanoutTurn = $turn
                    continue
                }

                if ($turnName.Equals("Continue live remote checks after clarification", [StringComparison]::OrdinalIgnoreCase)) {
                    $continueNonAd0Turn = $turn
                }
            }

            if ($null -eq $confirmedFanoutTurn) {
                $failures.Add("$($file.Name): expected 'Confirmed fanout execution' turn.") | Out-Null
            } else {
                $confirmedFanoutMinimumDistinct = Get-JsonPropertyValue -instance $confirmedFanoutTurn -propertyName 'min_distinct_tool_input_values'
                $confirmedFanoutMachineName = [int](Get-JsonPropertyValue -instance $confirmedFanoutMinimumDistinct -propertyName 'machine_name' -defaultValue 0)
                if ($confirmedFanoutMachineName -lt 2) {
                    $failures.Add("$($file.Name): 'Confirmed fanout execution' must enforce min_distinct_tool_input_values.machine_name >= 2.") | Out-Null
                }

                $confirmedFanoutDisallowedToolOutputLiterals = @(Get-NormalizedStringList -rawValue (Get-JsonPropertyValue -instance $confirmedFanoutTurn -propertyName 'assert_tool_output_not_contains'))
                if (-not ($confirmedFanoutDisallowedToolOutputLiterals | Where-Object { $_.IndexOf('"machine_name":"AD0.ad.evotec.xyz"', [StringComparison]::OrdinalIgnoreCase) -ge 0 })) {
                    $failures.Add("$($file.Name): 'Confirmed fanout execution' must block AD0 tool output reuse (`assert_tool_output_not_contains` for machine_name AD0).") | Out-Null
                }
            }

            if ($null -eq $continueNonAd0Turn) {
                $failures.Add("$($file.Name): expected 'Continue live remote checks after clarification' turn.") | Out-Null
            } else {
                $continueNonAd0MinimumDistinct = Get-JsonPropertyValue -instance $continueNonAd0Turn -propertyName 'min_distinct_tool_input_values'
                $continueNonAd0MachineName = [int](Get-JsonPropertyValue -instance $continueNonAd0MinimumDistinct -propertyName 'machine_name' -defaultValue 0)
                if ($continueNonAd0MachineName -lt 2) {
                    $failures.Add("$($file.Name): 'Continue live remote checks after clarification' must enforce min_distinct_tool_input_values.machine_name >= 2.") | Out-Null
                }

                $continueNonAd0DisallowedToolOutputLiterals = @(Get-NormalizedStringList -rawValue (Get-JsonPropertyValue -instance $continueNonAd0Turn -propertyName 'assert_tool_output_not_contains'))
                if (-not ($continueNonAd0DisallowedToolOutputLiterals | Where-Object { $_.IndexOf('"machine_name":"AD0.ad.evotec.xyz"', [StringComparison]::OrdinalIgnoreCase) -ge 0 })) {
                    $failures.Add("$($file.Name): 'Continue live remote checks after clarification' must block AD0 tool output reuse (`assert_tool_output_not_contains` for machine_name AD0).") | Out-Null
                }
            }
        }
    }

    if ($file.Name.StartsWith("dns-", [StringComparison]::OrdinalIgnoreCase)) {
        if (-not $tags.Contains("dns")) {
            $failures.Add("$($file.Name): DNS scenario must include 'dns' tag.") | Out-Null
        }
        if (-not (Has-PatternToken -patterns $requiredPatternList -token "dnsclientx_")) {
            $failures.Add("$($file.Name): DNS scenario must include at least one dnsclientx_* required tool pattern.") | Out-Null
        }
        if (-not (Has-PatternToken -patterns $requiredPatternList -token "domaindetective_")) {
            $failures.Add("$($file.Name): DNS scenario must include at least one domaindetective_* required tool pattern.") | Out-Null
        }
        if (-not (Has-PatternToken -patterns $forbiddenPatternList -token "ad_")) {
            $failures.Add("$($file.Name): DNS scenario must forbid ad_* tools in at least one turn.") | Out-Null
        }
        if (-not (Has-PatternToken -patterns $forbiddenPatternList -token "eventlog_")) {
            $failures.Add("$($file.Name): DNS scenario must forbid eventlog_* tools in at least one turn.") | Out-Null
        }
    }

    if ($file.Name.StartsWith("mixed-domain-ambiguity-", [StringComparison]::OrdinalIgnoreCase)) {
        $mixedDomainAmbiguityScenarioCount++

        if (-not $tags.Contains("domain-ambiguity")) {
            $failures.Add("$($file.Name): mixed-domain ambiguity scenario must include 'domain-ambiguity' tag.") | Out-Null
        }
        if (-not $tags.Contains("ad")) {
            $failures.Add("$($file.Name): mixed-domain ambiguity scenario must include 'ad' tag.") | Out-Null
        }
        if (-not $tags.Contains("dns")) {
            $failures.Add("$($file.Name): mixed-domain ambiguity scenario must include 'dns' tag.") | Out-Null
        }

        $clarifyTurn = $turns[0]
        $clarifyForbidden = @(Get-NormalizedStringList -rawValue (Get-JsonPropertyValue -instance $clarifyTurn -propertyName 'forbid_tools'))
        if (-not ($clarifyForbidden -contains '*')) {
            $failures.Add("$($file.Name): clarify turn must block tool execution (`forbid_tools: ['*']`).") | Out-Null
        }

        $clarifyContains = @(Get-NormalizedStringList -rawValue (Get-JsonPropertyValue -instance $clarifyTurn -propertyName 'assert_contains'))
        $clarifiesAd = $false
        $clarifiesDns = $false
        foreach ($value in $clarifyContains) {
            if ($value.IndexOf("ad", [StringComparison]::OrdinalIgnoreCase) -ge 0 -or $value.IndexOf("directory", [StringComparison]::OrdinalIgnoreCase) -ge 0) {
                $clarifiesAd = $true
            }
            if ($value.IndexOf("dns", [StringComparison]::OrdinalIgnoreCase) -ge 0) {
                $clarifiesDns = $true
            }
        }
        if (-not $clarifiesAd) {
            $failures.Add("$($file.Name): clarify turn must reference AD/Active Directory in assert_contains.") | Out-Null
        }
        if (-not $clarifiesDns) {
            $failures.Add("$($file.Name): clarify turn must reference DNS in assert_contains.") | Out-Null
        }

        $adRoutedTurnFound = $false
        $dnsRoutedTurnFound = $false
        foreach ($turn in @($turns | Select-Object -Skip 1)) {
            $requiredTurnPatterns = New-Object System.Collections.Generic.List[string]
            foreach ($pattern in @(Get-NormalizedStringList -rawValue (Get-JsonPropertyValue -instance $turn -propertyName 'require_tools'))) {
                $requiredTurnPatterns.Add($pattern) | Out-Null
            }
            foreach ($pattern in @(Get-NormalizedStringList -rawValue (Get-JsonPropertyValue -instance $turn -propertyName 'require_any_tools'))) {
                $requiredTurnPatterns.Add($pattern) | Out-Null
            }
            if ($requiredTurnPatterns.Count -eq 0) {
                continue
            }

            $requiredTurnPatternList = @($requiredTurnPatterns.ToArray())
            $forbiddenTurnPatternList = @(Get-NormalizedStringList -rawValue (Get-JsonPropertyValue -instance $turn -propertyName 'forbid_tools'))

            $requiresAd = (Has-PatternToken -patterns $requiredTurnPatternList -token "ad_") -or (Has-PatternToken -patterns $requiredTurnPatternList -token "eventlog_")
            $requiresDns = (Has-PatternToken -patterns $requiredTurnPatternList -token "dnsclientx_") -or (Has-PatternToken -patterns $requiredTurnPatternList -token "domaindetective_")
            $forbidsAd = (Has-PatternToken -patterns $forbiddenTurnPatternList -token "ad_") -or (Has-PatternToken -patterns $forbiddenTurnPatternList -token "eventlog_")
            $forbidsDns = (Has-PatternToken -patterns $forbiddenTurnPatternList -token "dnsclientx_") -or (Has-PatternToken -patterns $forbiddenTurnPatternList -token "domaindetective_")

            if ($requiresAd -and $forbidsDns) {
                $adRoutedTurnFound = $true
            }
            if ($requiresDns -and $forbidsAd) {
                $dnsRoutedTurnFound = $true
            }
        }

        if (-not $adRoutedTurnFound) {
            $failures.Add("$($file.Name): expected at least one AD-routed turn that forbids DNS tools.") | Out-Null
        }
        if (-not $dnsRoutedTurnFound) {
            $failures.Add("$($file.Name): expected at least one DNS-routed turn that forbids AD/EventLog tools.") | Out-Null
        }
    }

    $summaries.Add(("{0}: turns={1}, tool_contract_turns={2}" -f $file.Name, $turns.Count, $toolContractTurns)) | Out-Null
}

if (($usedDefaultFilter -or $selectedMixedDomainAmbiguityScenarioCount -gt 0) -and $mixedDomainAmbiguityScenarioCount -lt 2) {
    $failures.Add("Expected at least two mixed-domain ambiguity scenarios (`mixed-domain-ambiguity-*-10-turn.json`) for routing resilience coverage.") | Out-Null
}

if ($failures.Count -gt 0) {
    Write-Host "`n=== Chat Scenario Catalog Quality Gate: FAILED ===" -ForegroundColor Red
    foreach ($failure in $failures) {
        Write-Host ("[x] {0}" -f $failure) -ForegroundColor Red
    }
    exit 1
}

Write-Host "`n=== Chat Scenario Catalog Quality Gate: PASSED ===" -ForegroundColor Green
foreach ($line in $summaries) {
    Write-Host ("[+] {0}" -f $line) -ForegroundColor Yellow
}

exit 0
