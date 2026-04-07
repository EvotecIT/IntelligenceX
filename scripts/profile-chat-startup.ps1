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
    # Affinity mask uses a single 64-bit value today, so keep simulated target cores within that representable range.
    [ValidateRange(1, 64)]
    [int] $SimulatedSlowHardwareMaxLogicalCores = 2,
    [ValidateSet('Idle', 'BelowNormal', 'Normal')]
    [string] $SimulatedSlowHardwarePriorityClass = 'BelowNormal',
    [string] $ArchiveLogsDirectory,
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
    throw "This profiler targets the WinUI chat app and currently requires Windows."
}

$repoRoot = (Get-Item (Join-Path $PSScriptRoot '..')).FullName
if ([string]::IsNullOrWhiteSpace($ExePath)) {
    $ExePath = Join-Path $repoRoot 'IntelligenceX.Chat\IntelligenceX.Chat.App\bin\Release\net10.0-windows10.0.26100.0\win-x64\IntelligenceX.Chat.App.exe'
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

function Parse-NullableInt([string] $value) {
    if ([string]::IsNullOrWhiteSpace($value)) {
        return $null
    }

    $parsed = 0
    if ([int]::TryParse($value, [ref]$parsed)) {
        return $parsed
    }

    return $null
}

function Get-OrCreate-ConnectAttemptRecord([hashtable] $attemptMap, [System.Collections.Generic.List[string]] $attemptOrder, [string] $phase, [int] $attempt) {
    $key = $phase + '|' + $attempt
    if (-not $attemptMap.ContainsKey($key)) {
        $attemptMap[$key] = [ordered]@{
            phase               = $phase
            attempt             = $attempt
            status              = $null
            requested_timeout_ms = $null
            timeout_ms          = $null
            hard_timeout_ms     = $null
            budget_remaining_ms = $null
            elapsed_ms          = $null
            error_type          = $null
            error               = $null
            guardrail           = $null
            outlier             = $false
        }
        $attemptOrder.Add($key) | Out-Null
    }

    return $attemptMap[$key]
}

function Parse-StartupConnectAttemptDiagnostics([string[]] $lines) {
    $attemptMap = @{}
    $attemptOrder = New-Object System.Collections.Generic.List[string]
    $attemptOutlierCount = 0
    $totalOutlierCount = 0
    $guardrailAbortCount = 0
    $timeoutFailureCount = 0
    $ensureSidecarElapsedMs = $null

    foreach ($line in $lines) {
        if ([string]::IsNullOrWhiteSpace($line)) {
            continue
        }

        if ($line -match 'StartupConnect\.ensure_sidecar(?:\.recovery)? elapsed_ms=(?<elapsed>\d+)') {
            $ensureSidecarElapsedMs = [int]$Matches['elapsed']
            continue
        }

        if ($line -match 'StartupConnect\.(?<phase>pipe_connect\.(?:initial|retry|recovery)|ensure_sidecar(?:\.recovery)?)\s+(?:attempt=(?<attempt>\d+)\s+)?outlier elapsed_ms=(?<elapsed>\d+)\s+threshold_ms=(?<threshold>\d+)') {
            $totalOutlierCount++
            $phase = $Matches['phase']
            if ($phase.StartsWith('pipe_connect.', [StringComparison]::Ordinal)) {
                $attemptOutlierCount++
                if (-not [string]::IsNullOrWhiteSpace($Matches['attempt'])) {
                    $attempt = [int]$Matches['attempt']
                    $attemptRecord = Get-OrCreate-ConnectAttemptRecord $attemptMap $attemptOrder $phase $attempt
                    $attemptRecord.outlier = $true
                }
            }
            continue
        }

        if ($line -match 'StartupConnect\.pipe_connect\.retry attempt=(?<attempt>\d+) guardrail=(?<guardrail>[a-zA-Z0-9_\-]+)') {
            $attempt = [int]$Matches['attempt']
            $attemptRecord = Get-OrCreate-ConnectAttemptRecord $attemptMap $attemptOrder 'pipe_connect.retry' $attempt
            $attemptRecord.guardrail = $Matches['guardrail']
            if ($attemptRecord.guardrail -eq 'abort_after_timeout') {
                $guardrailAbortCount++
            }
            continue
        }

        if ($line -match 'StartupConnect\.(?<phase>pipe_connect\.(?:initial|retry|recovery)) attempt=(?<attempt>\d+) start requested_timeout_ms=(?<requested>\d+) timeout_ms=(?<timeout>\d+) hard_timeout_ms=(?<hard>\d+) budget_remaining_ms=(?<budget>[a-zA-Z0-9_\-]+)') {
            $phase = $Matches['phase']
            $attempt = [int]$Matches['attempt']
            $attemptRecord = Get-OrCreate-ConnectAttemptRecord $attemptMap $attemptOrder $phase $attempt
            $attemptRecord.requested_timeout_ms = [int]$Matches['requested']
            $attemptRecord.timeout_ms = [int]$Matches['timeout']
            $attemptRecord.hard_timeout_ms = [int]$Matches['hard']
            $attemptRecord.budget_remaining_ms = Parse-NullableInt $Matches['budget']
            continue
        }

        if ($line -match 'StartupConnect\.(?<phase>pipe_connect\.(?:initial|retry|recovery)) attempt=(?<attempt>\d+) (?<status>success|failed) elapsed_ms=(?<elapsed>\d+)(?: error_type=(?<errorType>\S+))?(?: error=(?<error>.*))?') {
            $phase = $Matches['phase']
            $attempt = [int]$Matches['attempt']
            $attemptRecord = Get-OrCreate-ConnectAttemptRecord $attemptMap $attemptOrder $phase $attempt
            $attemptRecord.status = $Matches['status']
            $attemptRecord.elapsed_ms = [int]$Matches['elapsed']
            $attemptRecord.error_type = if ([string]::IsNullOrWhiteSpace($Matches['errorType'])) { $null } else { $Matches['errorType'] }
            $attemptRecord.error = if ([string]::IsNullOrWhiteSpace($Matches['error'])) { $null } else { $Matches['error'] }

            $isTimeoutFailure = $attemptRecord.status -eq 'failed' -and (
                $attemptRecord.error_type -eq 'TimeoutException' -or
                $attemptRecord.error_type -eq 'OperationCanceledException' -or
                ($attemptRecord.error -is [string] -and $attemptRecord.error.Contains('Timed out', [StringComparison]::OrdinalIgnoreCase))
            )
            if ($isTimeoutFailure) {
                $timeoutFailureCount++
            }
            continue
        }
    }

    $attempts = New-Object System.Collections.Generic.List[object]
    foreach ($key in $attemptOrder) {
        $attempts.Add([pscustomobject]$attemptMap[$key])
    }

    return [pscustomobject]@{
        attempts                = $attempts
        attempt_outlier_count   = $attemptOutlierCount
        total_outlier_count     = $totalOutlierCount
        ensure_sidecar_elapsed_ms = $ensureSidecarElapsedMs
        guardrail_abort_count   = $guardrailAbortCount
        timeout_failure_count   = $timeoutFailureCount
    }
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
    $connectAttemptDiagnostics = Parse-StartupConnectAttemptDiagnostics $lines

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
        startup_connect_attempt_count = $connectAttemptDiagnostics.attempts.Count
        startup_connect_attempt_avg_elapsed_ms = Get-Average ($connectAttemptDiagnostics.attempts | ForEach-Object { $_.elapsed_ms })
        startup_connect_attempt_outlier_count = $connectAttemptDiagnostics.attempt_outlier_count
        startup_connect_outlier_count = $connectAttemptDiagnostics.total_outlier_count
        startup_connect_ensure_sidecar_elapsed_ms = $connectAttemptDiagnostics.ensure_sidecar_elapsed_ms
        startup_connect_guardrail_abort_count = $connectAttemptDiagnostics.guardrail_abort_count
        startup_connect_timeout_failure_count = $connectAttemptDiagnostics.timeout_failure_count
        startup_connect_attempts = $connectAttemptDiagnostics.attempts
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
    avg_startup_connect_attempt_count = Get-Average ($completedRuns | ForEach-Object { $_.startup_connect_attempt_count })
    avg_startup_connect_attempt_avg_elapsed_ms = Get-Average ($completedRuns | ForEach-Object { $_.startup_connect_attempt_avg_elapsed_ms })
    avg_startup_connect_ensure_sidecar_elapsed_ms = Get-Average ($completedRuns | ForEach-Object { $_.startup_connect_ensure_sidecar_elapsed_ms })
    startup_connect_attempt_outlier_runs = @($completedRuns | Where-Object { $_.startup_connect_attempt_outlier_count -gt 0 }).Count
    startup_connect_attempt_outlier_total = [int](($completedRuns | Measure-Object -Property startup_connect_attempt_outlier_count -Sum).Sum)
    startup_connect_outlier_runs = @($completedRuns | Where-Object { $_.startup_connect_outlier_count -gt 0 }).Count
    startup_connect_outlier_total = [int](($completedRuns | Measure-Object -Property startup_connect_outlier_count -Sum).Sum)
    startup_connect_guardrail_abort_runs = @($completedRuns | Where-Object { $_.startup_connect_guardrail_abort_count -gt 0 }).Count
    startup_connect_guardrail_abort_total = [int](($completedRuns | Measure-Object -Property startup_connect_guardrail_abort_count -Sum).Sum)
    startup_connect_timeout_failure_runs = @($completedRuns | Where-Object { $_.startup_connect_timeout_failure_count -gt 0 }).Count
    startup_connect_timeout_failure_total = [int](($completedRuns | Measure-Object -Property startup_connect_timeout_failure_count -Sum).Sum)
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
