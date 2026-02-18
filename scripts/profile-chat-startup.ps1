[CmdletBinding()]
param(
    [string] $ExePath,
    [ValidateRange(1, 50)]
    [int] $Runs = 5,
    [ValidateRange(5, 300)]
    [int] $TimeoutSeconds = 75,
    [ValidateRange(0, 120)]
    [int] $PostStartupGraceSeconds = 0,
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

$runResults = New-Object System.Collections.Generic.List[object]

for ($i = 1; $i -le $Runs; $i++) {
    Stop-ChatProcesses
    if (Test-Path $startupLogPath) {
        Remove-Item $startupLogPath -Force
    }

    $process = Start-Process -FilePath $ExePath -PassThru
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

    $run = [ordered]@{
        run              = $i
        state            = $state
        total_ms         = Get-DurationMs (Get-MarkerTimestamp $lines 'Program.Main enter') (Get-MarkerTimestamp $lines 'MainWindow.StartupFlow done')
        connect_ms       = Get-DurationMs (Get-MarkerTimestamp $lines 'StartupPhase.Connect begin') (Get-MarkerTimestamp $lines 'StartupPhase.Connect done')
        hello_ms         = Get-DurationMs (Get-MarkerTimestamp $lines 'StartupConnect.hello begin') (Get-MarkerTimestamp $lines 'StartupConnect.hello done')
        list_tools_ms    = Get-DurationMs (Get-MarkerTimestamp $lines 'StartupConnect.list_tools begin') (Get-MarkerTimestamp $lines 'StartupConnect.list_tools done')
        auth_refresh_ms  = Get-DurationMs (Get-MarkerTimestamp $lines 'StartupConnect.auth_refresh begin') (Get-MarkerTimestamp $lines 'StartupConnect.auth_refresh done')
        model_sync_ms    = Get-DurationMs (Get-MarkerTimestamp $lines 'StartupConnect.model_profile_sync begin') (Get-MarkerTimestamp $lines 'StartupConnect.model_profile_sync done')
        hello_deferred   = [bool]($lines | Where-Object { $_ -match 'StartupConnect.hello deferred' } | Select-Object -First 1)
        model_deferred   = [bool]($lines | Where-Object { $_ -match 'StartupConnect.model_profile_sync deferred' } | Select-Object -First 1)
    }

    $runResults.Add([pscustomobject]$run)
    Write-Host ("Run {0}/{1}: state={2}, total={3}ms, connect={4}ms, hello={5}ms" -f $i, $Runs, $run.state, $run.total_ms, $run.connect_ms, $run.hello_ms)
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
    avg_connect_ms       = Get-Average ($completedRuns | ForEach-Object { $_.connect_ms })
    avg_hello_ms         = Get-Average ($completedRuns | ForEach-Object { $_.hello_ms })
    avg_list_tools_ms    = Get-Average ($completedRuns | ForEach-Object { $_.list_tools_ms })
    avg_auth_refresh_ms  = Get-Average ($completedRuns | ForEach-Object { $_.auth_refresh_ms })
    avg_model_sync_ms    = Get-Average ($completedRuns | ForEach-Object { $_.model_sync_ms })
    avg_warm_total_ms    = Get-Average ($warmRuns | ForEach-Object { $_.total_ms })
    avg_warm_connect_ms  = Get-Average ($warmRuns | ForEach-Object { $_.connect_ms })
    avg_warm_hello_ms    = Get-Average ($warmRuns | ForEach-Object { $_.hello_ms })
    hello_deferred_runs  = @($completedRuns | Where-Object { $_.hello_deferred }).Count
    model_deferred_runs  = @($completedRuns | Where-Object { $_.model_deferred }).Count
}

$report = [pscustomobject]@{
    generated_utc = [datetime]::UtcNow.ToString('O')
    exe_path      = $ExePath
    startup_log   = $startupLogPath
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
