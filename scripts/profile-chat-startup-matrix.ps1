[CmdletBinding()]
param(
    [string] $ExePath,
    [ValidateRange(1, 50)]
    [int] $Runs = 4,
    [ValidateRange(5, 300)]
    [int] $TimeoutSeconds = 75,
    [ValidateRange(0, 120)]
    [int] $PostStartupGraceSeconds = 0,
    [string] $MatrixOutputDirectory,
    [string] $OutFile
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$runningOnWindows = if (Get-Variable -Name IsWindows -ErrorAction SilentlyContinue) {
    [bool]$IsWindows
} else {
    [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows)
}

if (-not $runningOnWindows) {
    throw "This startup matrix profiler targets the WinUI chat app and currently requires Windows."
}

$repoRoot = (Get-Item (Join-Path $PSScriptRoot '..')).FullName
$singleRunProfilerScript = Join-Path $PSScriptRoot 'profile-chat-startup.ps1'
if (-not (Test-Path $singleRunProfilerScript)) {
    throw "Required script not found: $singleRunProfilerScript"
}

if ([string]::IsNullOrWhiteSpace($ExePath)) {
    $ExePath = Join-Path $repoRoot 'IntelligenceX.Chat\IntelligenceX.Chat.App\bin\Release\net10.0-windows10.0.26100.0\win-x64\IntelligenceX.Chat.App.exe'
}

if (-not (Test-Path $ExePath)) {
    throw "Chat app executable not found: $ExePath"
}

if ([string]::IsNullOrWhiteSpace($MatrixOutputDirectory)) {
    $MatrixOutputDirectory = Join-Path $repoRoot 'artifacts\chat-startup-profile-matrix'
}
$resolvedMatrixOutputDirectory = [System.IO.Path]::GetFullPath($MatrixOutputDirectory)
New-Item -ItemType Directory -Path $resolvedMatrixOutputDirectory -Force | Out-Null

if ([string]::IsNullOrWhiteSpace($OutFile)) {
    $OutFile = Join-Path $resolvedMatrixOutputDirectory 'matrix-report.json'
}
$resolvedOutFile = [System.IO.Path]::GetFullPath($OutFile)

function Get-DeltaPercent($baseline, $value) {
    if ($null -eq $baseline -or $baseline -eq 0 -or $null -eq $value) {
        return $null
    }

    return [math]::Round((($value - $baseline) / $baseline) * 100, 2)
}

function Get-DeltaMs($baseline, $value) {
    if ($null -eq $baseline -or $null -eq $value) {
        return $null
    }

    return [math]::Round(($value - $baseline), 2)
}

$tierDefinitions = @(
    [ordered]@{
        tier_name = 'native'
        display_name = 'Native'
        simulate = $false
        max_cores = $null
        priority_class = $null
    },
    [ordered]@{
        tier_name = 'sim-8c'
        display_name = 'Simulated 8-core'
        simulate = $true
        max_cores = 8
        priority_class = 'Normal'
    },
    [ordered]@{
        tier_name = 'sim-4c'
        display_name = 'Simulated 4-core'
        simulate = $true
        max_cores = 4
        priority_class = 'BelowNormal'
    },
    [ordered]@{
        tier_name = 'sim-2c'
        display_name = 'Simulated 2-core'
        simulate = $true
        max_cores = 2
        priority_class = 'BelowNormal'
    }
)

$tierReports = New-Object System.Collections.Generic.List[object]
$logsRoot = Join-Path $resolvedMatrixOutputDirectory 'logs'
New-Item -ItemType Directory -Path $logsRoot -Force | Out-Null

foreach ($tier in $tierDefinitions) {
    $tierName = [string]$tier.tier_name
    $tierOutFile = Join-Path $resolvedMatrixOutputDirectory ($tierName + '.json')
    $tierLogDirectory = Join-Path $logsRoot $tierName

    $singleRunArgs = @{
        ExePath = $ExePath
        Runs = $Runs
        TimeoutSeconds = $TimeoutSeconds
        PostStartupGraceSeconds = $PostStartupGraceSeconds
        ArchiveLogsDirectory = $tierLogDirectory
        OutFile = $tierOutFile
    }

    if ([bool]$tier.simulate) {
        $singleRunArgs.SimulateSlowHardware = $true
        $singleRunArgs.SimulatedSlowHardwareMaxLogicalCores = [int]$tier.max_cores
        $singleRunArgs.SimulatedSlowHardwarePriorityClass = [string]$tier.priority_class
    }

    Write-Host ("Running startup profile tier '{0}' ({1}/{2})..." -f $tierName, ($tierReports.Count + 1), $tierDefinitions.Count)
    & $singleRunProfilerScript @singleRunArgs | Out-Null

    $tierRawReport = Get-Content -Path $tierOutFile -Raw | ConvertFrom-Json -Depth 100
    $tierSummary = $tierRawReport.summary

    $tierReports.Add([pscustomobject]@{
        tier_name = $tierName
        display_name = [string]$tier.display_name
        report_file = $tierOutFile
        simulation = $tierRawReport.simulation
        runs_completed = $tierSummary.runs_completed
        avg_total_ms = $tierSummary.avg_total_ms
        avg_startup_webview_ms = $tierSummary.avg_startup_webview_ms
        avg_connect_ms = $tierSummary.avg_connect_ms
        avg_startup_connect_ensure_sidecar_elapsed_ms = $tierSummary.avg_startup_connect_ensure_sidecar_elapsed_ms
        avg_startup_connect_attempt_avg_elapsed_ms = $tierSummary.avg_startup_connect_attempt_avg_elapsed_ms
        startup_connect_timeout_failure_total = $tierSummary.startup_connect_timeout_failure_total
        startup_connect_outlier_total = $tierSummary.startup_connect_outlier_total
    }) | Out-Null
}

$nativeTier = @($tierReports | Where-Object { $_.tier_name -eq 'native' } | Select-Object -First 1)
if ($nativeTier.Count -eq 0) {
    throw "Native tier report missing."
}
$native = $nativeTier[0]

foreach ($tierReport in $tierReports) {
    $tierReport | Add-Member -NotePropertyName avg_total_vs_native_ms -NotePropertyValue (Get-DeltaMs $native.avg_total_ms $tierReport.avg_total_ms)
    $tierReport | Add-Member -NotePropertyName avg_total_vs_native_percent -NotePropertyValue (Get-DeltaPercent $native.avg_total_ms $tierReport.avg_total_ms)
    $tierReport | Add-Member -NotePropertyName avg_startup_webview_vs_native_ms -NotePropertyValue (Get-DeltaMs $native.avg_startup_webview_ms $tierReport.avg_startup_webview_ms)
    $tierReport | Add-Member -NotePropertyName avg_connect_vs_native_ms -NotePropertyValue (Get-DeltaMs $native.avg_connect_ms $tierReport.avg_connect_ms)
}

$sortedByTotal = @($tierReports | Sort-Object -Property avg_total_ms)
$matrixSummary = [pscustomobject]@{
    tiers_profiled = $tierReports.Count
    runs_per_tier = $Runs
    fastest_tier = if ($sortedByTotal.Count -gt 0) { $sortedByTotal[0].tier_name } else { $null }
    slowest_tier = if ($sortedByTotal.Count -gt 0) { $sortedByTotal[$sortedByTotal.Count - 1].tier_name } else { $null }
    native_avg_total_ms = $native.avg_total_ms
    slowest_vs_native_ms = if ($sortedByTotal.Count -gt 0) { Get-DeltaMs $native.avg_total_ms $sortedByTotal[$sortedByTotal.Count - 1].avg_total_ms } else { $null }
    slowest_vs_native_percent = if ($sortedByTotal.Count -gt 0) { Get-DeltaPercent $native.avg_total_ms $sortedByTotal[$sortedByTotal.Count - 1].avg_total_ms } else { $null }
}

$matrixReport = [pscustomobject]@{
    schema_version = 'chat-startup-profile-matrix-v1'
    generated_utc = [datetime]::UtcNow.ToString('O')
    exe_path = $ExePath
    output_directory = $resolvedMatrixOutputDirectory
    source_single_run_schema_version = 'chat-startup-profile-v2'
    summary = $matrixSummary
    tiers = $tierReports
}

$matrixOutDirectory = Split-Path -Parent $resolvedOutFile
if (-not [string]::IsNullOrWhiteSpace($matrixOutDirectory)) {
    New-Item -ItemType Directory -Path $matrixOutDirectory -Force | Out-Null
}

$matrixJson = $matrixReport | ConvertTo-Json -Depth 8
Set-Content -Path $resolvedOutFile -Value $matrixJson

Write-Host "Saved matrix report to $resolvedOutFile"
Write-Host ""
Write-Host "Startup matrix summary (avg ms):"
$tierReports |
    Select-Object tier_name, avg_total_ms, avg_total_vs_native_ms, avg_total_vs_native_percent, avg_startup_webview_ms, avg_connect_ms, avg_startup_connect_ensure_sidecar_elapsed_ms, startup_connect_outlier_total |
    Sort-Object -Property avg_total_ms |
    Format-Table -AutoSize

$matrixJson
