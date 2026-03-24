# Runs compaction soak scenarios and compares latest run artifacts against golden baselines.

[CmdletBinding()] param(
    [string] $ScenarioDir = '.\IntelligenceX.Chat\scenarios',
    [string] $Filter = 'ad-compaction-soak-*.json',
    [string[]] $Tags = @('ad', 'strict', 'compaction', 'soak'),
    [string] $OutDir = '.\artifacts\chat-compaction-soak',
    [string] $GoldenDir = '.\artifacts\chat-compaction-soak\golden',
    [switch] $UpdateGolden,
    [switch] $AllowMissingGolden,
    [switch] $NoCompare,
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

function Get-ScenarioStem([string] $scenarioName) {
    $name = if ([string]::IsNullOrWhiteSpace($scenarioName)) { 'scenario' } else { $scenarioName.Trim() }
    $builder = New-Object System.Text.StringBuilder
    foreach ($c in $name.ToCharArray()) {
        if ([char]::IsLetterOrDigit($c)) {
            [void]$builder.Append([char]::ToLowerInvariant($c))
        } else {
            [void]$builder.Append('-')
        }
    }

    $compact = $builder.ToString().Trim('-')
    if ($compact.Length -eq 0) {
        $compact = 'scenario'
    }
    while ($compact.Contains('--')) {
        $compact = $compact.Replace('--', '-')
    }
    return $compact
}

$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$soakScript = Join-Path $repoRoot 'Build\Chat\Run-ChatCompactionSoakSuite.ps1'
$compareScript = Join-Path $repoRoot 'Build\Chat\Compare-ChatScenarioReports.ps1'

if (-not (Test-Path $soakScript)) {
    throw "Compaction soak suite script not found: $soakScript"
}
if (-not (Test-Path $compareScript)) {
    throw "Scenario report compare script not found: $compareScript"
}

$resolvedOutDir = Resolve-RepoRelativePath -repoRoot $repoRoot -pathValue $OutDir
$resolvedGoldenDir = Resolve-RepoRelativePath -repoRoot $repoRoot -pathValue $GoldenDir
New-Item -ItemType Directory -Path $resolvedOutDir -Force | Out-Null
New-Item -ItemType Directory -Path $resolvedGoldenDir -Force | Out-Null

$beforeJson = @{}
Get-ChildItem -Path $resolvedOutDir -File -Filter '*.json' | ForEach-Object {
    $beforeJson[$_.FullName] = $_.LastWriteTimeUtc.Ticks
}

Write-Header 'Run Chat Compaction Soak Baseline'
Write-Step ("Scenario dir: {0}" -f (Resolve-RepoRelativePath -repoRoot $repoRoot -pathValue $ScenarioDir))
Write-Step ("Filter: {0}" -f $Filter)
Write-Step ("Tags: {0}" -f (@($Tags) -join ', '))
Write-Step ("Output dir: {0}" -f $resolvedOutDir)
Write-Step ("Golden dir: {0}" -f $resolvedGoldenDir)
Write-Step ("Compare enabled: {0}" -f (-not $NoCompare))
Write-Step ("Update golden: {0}" -f [bool]$UpdateGolden)
Write-Step ("Allow missing golden: {0}" -f [bool]$AllowMissingGolden)

$soakParams = @{
    ScenarioDir = $ScenarioDir
    Filter = $Filter
    Tags = $Tags
    OutDir = $resolvedOutDir
    ContinueOnError = $ContinueOnError
    ParallelTools = [bool]$ParallelTools
    EchoToolOutputs = [bool]$EchoToolOutputs
}
if ($AllowRoot -and $AllowRoot.Count -gt 0) {
    $soakParams['AllowRoot'] = $AllowRoot
}
if ($NoBuild) {
    $soakParams['NoBuild'] = $true
}
if ($StopOnFailure) {
    $soakParams['StopOnFailure'] = $true
}
if ($EnablePowerShellPack) {
    $soakParams['EnablePowerShellPack'] = $true
}
if ($EnableTestimoXPack) {
    $soakParams['EnableTestimoXPack'] = $true
}
if ($EnableDnsClientXPack) {
    $soakParams['EnableDnsClientXPack'] = $true
}
if ($DisableDnsClientXPack) {
    $soakParams['DisableDnsClientXPack'] = $true
}
if ($EnableDomainDetectivePack) {
    $soakParams['EnableDomainDetectivePack'] = $true
}
if ($DisableDomainDetectivePack) {
    $soakParams['DisableDomainDetectivePack'] = $true
}
if (-not [string]::IsNullOrWhiteSpace($Model)) {
    $soakParams['Model'] = $Model
}
if ($ExtraArgs -and $ExtraArgs.Count -gt 0) {
    $soakParams['ExtraArgs'] = $ExtraArgs
}

& $soakScript @soakParams

$newReports = @(Get-ChildItem -Path $resolvedOutDir -File -Filter '*.json' |
    Where-Object {
        if (-not $beforeJson.ContainsKey($_.FullName)) {
            return $true
        }

        $previousTicks = [long]$beforeJson[$_.FullName]
        return $_.LastWriteTimeUtc.Ticks -gt $previousTicks
    } |
    Sort-Object LastWriteTimeUtc -Descending)
if ($newReports.Count -eq 0) {
    throw "No new scenario JSON artifacts found in '$resolvedOutDir' for this run."
}

$latestByScenario = @{}
foreach ($file in $newReports) {
    $report = Get-Content -Path $file.FullName -Raw | ConvertFrom-Json -Depth 100
    if ($null -eq $report) {
        continue
    }

    $scenarioName = "$($report.name)".Trim()
    if ($scenarioName.Length -eq 0) {
        $scenarioName = [System.IO.Path]::GetFileNameWithoutExtension($file.Name)
    }
    $scenarioKey = $scenarioName.ToLowerInvariant()
    if ($latestByScenario.ContainsKey($scenarioKey)) {
        continue
    }

    $latestByScenario[$scenarioKey] = [pscustomobject]@{
        Name = $scenarioName
        Path = $file.FullName
        StartedUtc = "$($report.started_utc)"
        PassedTurns = [int]$report.passed_turns
        TotalTurns = [int]$report.total_turns
    }
}

$selectedReports = @($latestByScenario.Values | Sort-Object Name)
if ($selectedReports.Count -eq 0) {
    throw "No parseable scenario JSON artifacts were produced in '$resolvedOutDir'."
}

Write-Header 'Selected Scenario Reports'
foreach ($entry in $selectedReports) {
    Write-Step ("{0}: {1}/{2} turns passed ({3})" -f $entry.Name, $entry.PassedTurns, $entry.TotalTurns, $entry.Path)
}

$comparisonRegressions = New-Object System.Collections.Generic.List[string]
$missingGoldenScenarios = New-Object System.Collections.Generic.List[string]
$updatedGoldenScenarios = New-Object System.Collections.Generic.List[string]
$skippedComparisons = New-Object System.Collections.Generic.List[string]

foreach ($entry in $selectedReports) {
    $scenarioStem = Get-ScenarioStem -scenarioName $entry.Name
    $goldenPath = Join-Path $resolvedGoldenDir ($scenarioStem + '.json')
    $hasGolden = Test-Path $goldenPath

    if ($NoCompare) {
        $skippedComparisons.Add($entry.Name) | Out-Null
    } elseif (-not $hasGolden) {
        if ($AllowMissingGolden -or $UpdateGolden) {
            Write-Step ("Missing golden (allowed): {0}" -f $entry.Name)
            $skippedComparisons.Add($entry.Name) | Out-Null
        } else {
            $missingGoldenScenarios.Add($entry.Name) | Out-Null
        }
    } else {
        Write-Header ("Compare: {0}" -f $entry.Name)
        $compareParams = @{
            BaseReport = $goldenPath
            CurrentReport = $entry.Path
        }
        if (-not $UpdateGolden) {
            $compareParams['FailOnRegression'] = $true
        }

        & $compareScript @compareParams
        if ($LASTEXITCODE -ne 0) {
            $comparisonRegressions.Add($entry.Name) | Out-Null
        }
    }

    if ($UpdateGolden) {
        Copy-Item -Path $entry.Path -Destination $goldenPath -Force
        $updatedGoldenScenarios.Add($entry.Name) | Out-Null
        Write-Step ("Golden updated: {0} -> {1}" -f $entry.Name, $goldenPath)
    }
}

Write-Header 'Compaction Soak Baseline Summary'
Write-Step ("Compared scenarios: {0}" -f ($selectedReports.Count - $skippedComparisons.Count))
Write-Step ("Skipped comparisons: {0}" -f $skippedComparisons.Count)
Write-Step ("Golden updates: {0}" -f $updatedGoldenScenarios.Count)

if ($missingGoldenScenarios.Count -gt 0) {
    Write-Fail ("Missing golden baselines: {0}" -f ($missingGoldenScenarios -join ', '))
}
if ($comparisonRegressions.Count -gt 0) {
    Write-Fail ("Regressions detected: {0}" -f ($comparisonRegressions -join ', '))
}

if ($missingGoldenScenarios.Count -gt 0 -or $comparisonRegressions.Count -gt 0) {
    exit 1
}

Write-Step "Compaction soak baseline checks passed."
exit 0
