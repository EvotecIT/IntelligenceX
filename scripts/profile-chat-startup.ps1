[CmdletBinding()]
param(
    [string] $ExePath,
    [ValidateRange(1, 50)]
    [int] $Runs = 5,
    [ValidateRange(5, 300)]
    [int] $TimeoutSeconds = 75,
    [ValidateRange(0, 120)]
    [int] $PostStartupGraceSeconds = 0,
    [switch] $SimulateSlowHardware,
    [ValidateRange(1, 256)]
    [int] $SimulatedSlowHardwareMaxLogicalCores = 2,
    [ValidateSet('Idle', 'BelowNormal', 'Normal')]
    [string] $SimulatedSlowHardwarePriorityClass = 'BelowNormal',
    [string] $ArchiveLogsDirectory,
    [string] $OutFile
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if (-not $IsWindows) {
    throw "This profiler targets the WinUI chat app and currently requires Windows."
}

$repoRoot = (Get-Item (Join-Path $PSScriptRoot '..')).FullName
if ([string]::IsNullOrWhiteSpace($ExePath)) {
    $ExePath = Join-Path $repoRoot 'IntelligenceX.Chat\IntelligenceX.Chat.App\bin\Release\net8.0-windows10.0.26100.0\win-x64\IntelligenceX.Chat.App.exe'
}

if (-not (Test-Path $ExePath)) {
    throw "Chat app executable not found: $ExePath"
}

$startupLogPath = Join-Path $env:TEMP 'IntelligenceX.Chat\app-startup.log'
$resolvedArchiveLogsDirectory = $null
if (-not [string]::IsNullOrWhiteSpace($ArchiveLogsDirectory)) {
    $resolvedArchiveLogsDirectory = [System.IO.Path]::GetFullPath($ArchiveLogsDirectory)
}

function Resolve-SlowHardwareSimulationProfile {
    if (-not $SimulateSlowHardware) {
        return $null
    }

    $logicalCoreCount = [Environment]::ProcessorCount
    $targetCoreCount = [Math]::Min($logicalCoreCount, [Math]::Max(1, $SimulatedSlowHardwareMaxLogicalCores))

    [long]$affinityMaskValue = 0
    for ($core = 0; $core -lt $targetCoreCount; $core++) {
        $affinityMaskValue = $affinityMaskValue -bor ([long]1 -shl $core)
    }

    return [pscustomobject]@{
        enabled             = $true
        host_logical_cores  = $logicalCoreCount
        target_logical_cores = $targetCoreCount
        priority_class      = $SimulatedSlowHardwarePriorityClass
        affinity_mask_value = $affinityMaskValue
        affinity_mask       = ('0x{0:X}' -f $affinityMaskValue)
    }
}

function Stop-ChatProcesses {
    Get-Process -Name 'IntelligenceX.Chat.App', 'IntelligenceX.Chat.Service' -ErrorAction SilentlyContinue |
        Stop-Process -Force -ErrorAction SilentlyContinue
}

function Parse-Timestamp([string] $line) {
    if ([string]::IsNullOrWhiteSpace($line)) {
        return $null
    }

    if ($line -match '^\[(?<ts>[^\]]+)\]') {
        return [datetime]::Parse($Matches['ts'])
    }

    return $null
}

function Get-MarkerTimestamp([string[]] $lines, [string] $marker) {
    $line = $lines | Where-Object { $_ -match [regex]::Escape($marker) } | Select-Object -First 1
    return Parse-Timestamp $line
}

function Get-DurationMs($start, $end) {
    if ($null -eq $start -or $null -eq $end) {
        return $null
    }

    return [math]::Round(($end - $start).TotalMilliseconds, 2)
}

function Get-Average([object[]] $values) {
    $usable = @($values | Where-Object { $null -ne $_ })
    if ($usable.Count -eq 0) {
        return $null
    }

    return [math]::Round((($usable | Measure-Object -Average).Average), 2)
}

function Get-MarkerIntValue([string[]] $lines, [string] $marker) {
    $line = $lines | Where-Object { $_ -match [regex]::Escape($marker) } | Select-Object -First 1
    if ([string]::IsNullOrWhiteSpace($line)) {
        return $null
    }

    $pattern = [regex]::Escape($marker) + '(?<value>\d+)'
    if ($line -match $pattern) {
        return [int]$Matches['value']
    }

    return $null
}

$slowHardwareSimulationProfile = Resolve-SlowHardwareSimulationProfile
if ($null -ne $slowHardwareSimulationProfile) {
    Write-Host ("Slow hardware simulation enabled: target_cores={0}/{1}, priority={2}, affinity={3}" -f
        $slowHardwareSimulationProfile.target_logical_cores,
        $slowHardwareSimulationProfile.host_logical_cores,
        $slowHardwareSimulationProfile.priority_class,
        $slowHardwareSimulationProfile.affinity_mask)
}

$runResults = New-Object System.Collections.Generic.List[object]

for ($i = 1; $i -le $Runs; $i++) {
    Stop-ChatProcesses
    if (Test-Path $startupLogPath) {
        Remove-Item $startupLogPath -Force
    }

    $process = Start-Process -FilePath $ExePath -PassThru
    $slowHardwareSimulationApplied = $false
    $slowHardwareSimulationError = $null
    if ($null -ne $slowHardwareSimulationProfile) {
        try {
            if (-not $process.HasExited) {
                $process.ProcessorAffinity = [IntPtr]$slowHardwareSimulationProfile.affinity_mask_value
                $process.PriorityClass = [System.Diagnostics.ProcessPriorityClass]::$SimulatedSlowHardwarePriorityClass
                $slowHardwareSimulationApplied = $true
            }
        } catch {
            $slowHardwareSimulationError = $_.Exception.Message
            Write-Warning ("Slow hardware simulation was not applied on run {0}: {1}" -f $i, $slowHardwareSimulationError)
        }
    }

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    $state = 'timeout'

    while ((Get-Date) -lt $deadline) {
        if (Test-Path $startupLogPath) {
            $raw = Get-Content $startupLogPath -Raw -ErrorAction SilentlyContinue
            if ($raw -match 'MainWindow.StartupFlow done') {
                $state = 'done'
                break
            }

            if ($raw -match 'MainWindow.StartupFlow failed') {
                $state = 'failed'
                break
            }
        }

        if ($process.HasExited) {
            $state = 'exited'
            break
        }

        Start-Sleep -Milliseconds 200
    }

    if ($state -eq 'done' -and $PostStartupGraceSeconds -gt 0) {
        Start-Sleep -Seconds $PostStartupGraceSeconds
    }

    Stop-ChatProcesses
    $lines = if (Test-Path $startupLogPath) { Get-Content $startupLogPath -ErrorAction SilentlyContinue } else { @() }
    $archivedStartupLogPath = $null
    if (-not [string]::IsNullOrWhiteSpace($resolvedArchiveLogsDirectory) -and (Test-Path $startupLogPath)) {
        New-Item -ItemType Directory -Path $resolvedArchiveLogsDirectory -Force | Out-Null
        $archivedStartupLogPath = Join-Path $resolvedArchiveLogsDirectory ("run-{0:D2}-{1}.log" -f $i, $state)
        Copy-Item -Path $startupLogPath -Destination $archivedStartupLogPath -Force
    }

    $run = [ordered]@{
        run              = $i
        state            = $state
        startup_log_path = $archivedStartupLogPath
        total_ms         = Get-DurationMs (Get-MarkerTimestamp $lines 'Program.Main enter') (Get-MarkerTimestamp $lines 'MainWindow.StartupFlow done')
        startup_webview_budget_ms = Get-MarkerIntValue $lines 'StartupPhase.WebView budget_ms='
        startup_webview_ms = Get-DurationMs (Get-MarkerTimestamp $lines 'StartupPhase.WebView begin') (Get-MarkerTimestamp $lines 'StartupPhase.WebView done')
        ensure_webview_ms  = Get-DurationMs (Get-MarkerTimestamp $lines 'EnsureWebViewInitializedAsync begin') (Get-MarkerTimestamp $lines 'EnsureWebViewInitializedAsync ok')
        webview_env_prewarm_ms = Get-DurationMs (Get-MarkerTimestamp $lines 'EnsureWebViewInitializedAsync.env_prewarm begin') (Get-MarkerTimestamp $lines 'EnsureWebViewInitializedAsync.env_prewarm done')
        connect_ms       = Get-DurationMs (Get-MarkerTimestamp $lines 'StartupPhase.Connect begin') (Get-MarkerTimestamp $lines 'StartupPhase.Connect done')
        hello_ms         = Get-DurationMs (Get-MarkerTimestamp $lines 'StartupConnect.hello begin') (Get-MarkerTimestamp $lines 'StartupConnect.hello done')
        list_tools_ms    = Get-DurationMs (Get-MarkerTimestamp $lines 'StartupConnect.list_tools begin') (Get-MarkerTimestamp $lines 'StartupConnect.list_tools done')
        auth_refresh_ms  = Get-DurationMs (Get-MarkerTimestamp $lines 'StartupConnect.auth_refresh begin') (Get-MarkerTimestamp $lines 'StartupConnect.auth_refresh done')
        model_sync_ms    = Get-DurationMs (Get-MarkerTimestamp $lines 'StartupConnect.model_profile_sync begin') (Get-MarkerTimestamp $lines 'StartupConnect.model_profile_sync done')
        startup_webview_budget_exhausted = [bool]($lines | Where-Object { $_ -match 'StartupPhase.WebView budget_exhausted' } | Select-Object -First 1)
        startup_webview_deferred = [bool]($lines | Where-Object { $_ -match 'StartupPhase.WebView deferred' } | Select-Object -First 1)
        startup_webview_eventual_done = [bool]($lines | Where-Object { $_ -match 'StartupPhase.WebView eventual_done' } | Select-Object -First 1)
        hello_deferred   = [bool]($lines | Where-Object { $_ -match 'StartupConnect.hello deferred' } | Select-Object -First 1)
        model_deferred   = [bool]($lines | Where-Object { $_ -match 'StartupConnect.model_profile_sync deferred' } | Select-Object -First 1)
        slow_hardware_simulation_applied = $slowHardwareSimulationApplied
        slow_hardware_simulation_error = $slowHardwareSimulationError
    }

    $runResults.Add([pscustomobject]$run)
    Write-Host ("Run {0}/{1}: state={2}, total={3}ms, startup_webview={4}ms, connect={5}ms, hello={6}ms" -f $i, $Runs, $run.state, $run.total_ms, $run.startup_webview_ms, $run.connect_ms, $run.hello_ms)
}

$completedRuns = @($runResults | Where-Object { $_.state -eq 'done' })
if ($completedRuns.Count -eq 0) {
    throw "No successful startup runs completed."
}

$warmRuns = @($completedRuns | Where-Object { $_.run -gt 1 })

$summary = [pscustomobject]@{
    runs_requested       = $Runs
    runs_completed       = $completedRuns.Count
    avg_total_ms         = Get-Average ($completedRuns | ForEach-Object { $_.total_ms })
    avg_startup_webview_budget_ms = Get-Average ($completedRuns | ForEach-Object { $_.startup_webview_budget_ms })
    avg_startup_webview_ms = Get-Average ($completedRuns | ForEach-Object { $_.startup_webview_ms })
    avg_ensure_webview_ms  = Get-Average ($completedRuns | ForEach-Object { $_.ensure_webview_ms })
    avg_webview_env_prewarm_ms = Get-Average ($completedRuns | ForEach-Object { $_.webview_env_prewarm_ms })
    avg_connect_ms       = Get-Average ($completedRuns | ForEach-Object { $_.connect_ms })
    avg_hello_ms         = Get-Average ($completedRuns | ForEach-Object { $_.hello_ms })
    avg_list_tools_ms    = Get-Average ($completedRuns | ForEach-Object { $_.list_tools_ms })
    avg_auth_refresh_ms  = Get-Average ($completedRuns | ForEach-Object { $_.auth_refresh_ms })
    avg_model_sync_ms    = Get-Average ($completedRuns | ForEach-Object { $_.model_sync_ms })
    avg_warm_total_ms    = Get-Average ($warmRuns | ForEach-Object { $_.total_ms })
    avg_warm_startup_webview_budget_ms = Get-Average ($warmRuns | ForEach-Object { $_.startup_webview_budget_ms })
    avg_warm_startup_webview_ms = Get-Average ($warmRuns | ForEach-Object { $_.startup_webview_ms })
    avg_warm_ensure_webview_ms  = Get-Average ($warmRuns | ForEach-Object { $_.ensure_webview_ms })
    avg_warm_webview_env_prewarm_ms = Get-Average ($warmRuns | ForEach-Object { $_.webview_env_prewarm_ms })
    avg_warm_connect_ms  = Get-Average ($warmRuns | ForEach-Object { $_.connect_ms })
    avg_warm_hello_ms    = Get-Average ($warmRuns | ForEach-Object { $_.hello_ms })
    startup_webview_budget_exhausted_runs = @($completedRuns | Where-Object { $_.startup_webview_budget_exhausted }).Count
    startup_webview_deferred_runs = @($completedRuns | Where-Object { $_.startup_webview_deferred }).Count
    startup_webview_eventual_done_runs = @($completedRuns | Where-Object { $_.startup_webview_eventual_done }).Count
    hello_deferred_runs  = @($completedRuns | Where-Object { $_.hello_deferred }).Count
    model_deferred_runs  = @($completedRuns | Where-Object { $_.model_deferred }).Count
    slow_hardware_simulation_enabled = $null -ne $slowHardwareSimulationProfile
    slow_hardware_simulation_applied_runs = @($completedRuns | Where-Object { $_.slow_hardware_simulation_applied }).Count
    slow_hardware_simulation_failed_runs = @($completedRuns | Where-Object { -not [string]::IsNullOrWhiteSpace($_.slow_hardware_simulation_error) }).Count
}

$slowHardwareSimulationReport = if ($null -eq $slowHardwareSimulationProfile) {
    [pscustomobject]@{
        enabled = $false
    }
} else {
    [pscustomobject]@{
        enabled = $true
        host_logical_cores = $slowHardwareSimulationProfile.host_logical_cores
        target_logical_cores = $slowHardwareSimulationProfile.target_logical_cores
        priority_class = $slowHardwareSimulationProfile.priority_class
        affinity_mask = $slowHardwareSimulationProfile.affinity_mask
    }
}

$report = [pscustomobject]@{
    schema_version = "chat-startup-profile-v2"
    generated_utc = [datetime]::UtcNow.ToString('O')
    exe_path      = $ExePath
    startup_log   = $startupLogPath
    archive_logs_directory = $resolvedArchiveLogsDirectory
    simulation    = $slowHardwareSimulationReport
    summary       = $summary
    runs          = $runResults
}

$json = $report | ConvertTo-Json -Depth 6
if (-not [string]::IsNullOrWhiteSpace($OutFile)) {
    $outDir = Split-Path -Parent $OutFile
    if (-not [string]::IsNullOrWhiteSpace($outDir)) {
        New-Item -ItemType Directory -Path $outDir -Force | Out-Null
    }

    Set-Content -Path $OutFile -Value $json
    Write-Host "Saved report to $OutFile"
}

$json
