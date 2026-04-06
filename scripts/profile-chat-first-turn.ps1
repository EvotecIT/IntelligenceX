[CmdletBinding()]
param(
    [string] $ExePath,
    [ValidateRange(1, 20)]
    [int] $Runs = 3,
    [ValidateRange(10, 180)]
    [int] $StartupTimeoutSeconds = 70,
    [ValidateRange(10, 240)]
    [int] $TurnTimeoutSeconds = 90,
    [string] $ProfileName = "default",
    [string] $OutFile
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if (-not $IsWindows) {
    throw "This script requires Windows."
}

$repoRoot = (Get-Item (Join-Path $PSScriptRoot "..")).FullName
if ([string]::IsNullOrWhiteSpace($ExePath)) {
    $ExePath = Join-Path $repoRoot "IntelligenceX.Chat\IntelligenceX.Chat.App\bin\Debug\net10.0-windows10.0.26100.0\win-x64\IntelligenceX.Chat.App.exe"
}

if (-not (Test-Path $ExePath)) {
    throw "Chat app executable not found: $ExePath"
}

$dbPath = Join-Path $env:LOCALAPPDATA "IntelligenceX.Chat\app-state.db"
$startupLogPath = Join-Path $env:TEMP "IntelligenceX.Chat\app-startup.log"
$sqliteCorePath = "C:\Support\GitHub\DbaClientX\DbaClientX.Core\bin\Debug\net8.0\DbaClientX.Core.dll"
$sqlitePath = "C:\Support\GitHub\DbaClientX\DbaClientX.SQLite\bin\Debug\net8.0\DbaClientX.SQLite.dll"

if (-not (Test-Path $sqliteCorePath) -or -not (Test-Path $sqlitePath)) {
    throw "DbaClientX assemblies not found. Build Debug first: $sqliteCorePath ; $sqlitePath"
}

Add-Type -AssemblyName Microsoft.VisualBasic
Add-Type -AssemblyName System.Windows.Forms
Add-Type -Path $sqliteCorePath
Add-Type -Path $sqlitePath
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;

public static class IxChatWin32 {
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);

    public const int SW_RESTORE = 9;
    public const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    public const uint MOUSEEVENTF_LEFTUP = 0x0004;
}
"@

function Stop-ChatProcesses {
    Get-Process -Name "IntelligenceX.Chat.App", "IntelligenceX.Chat.Service" -ErrorAction SilentlyContinue |
        Stop-Process -Force -ErrorAction SilentlyContinue
}

function Wait-StartupDone([int] $timeoutSeconds, [string] $startupLog) {
    $deadline = (Get-Date).AddSeconds($timeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        if (Test-Path $startupLog) {
            $raw = Get-Content $startupLog -Raw -ErrorAction SilentlyContinue
            if ($raw -match "MainWindow.StartupFlow done") {
                return $true
            }
            if ($raw -match "MainWindow.StartupFlow failed") {
                return $false
            }
        }
        Start-Sleep -Milliseconds 150
    }
    return $false
}

function Get-StartupLogMarkerTime([string] $startupLog, [string] $marker) {
    if (-not (Test-Path $startupLog)) {
        return $null
    }

    try {
        $line = Get-Content $startupLog -ErrorAction SilentlyContinue |
            Where-Object { $_ -like "*$marker*" } |
            Select-Object -Last 1
        if ([string]::IsNullOrWhiteSpace($line)) {
            return $null
        }

        if ($line -match '^\[(?<ts>[^\]]+)\]') {
            return [datetime]::Parse($Matches['ts'])
        }
    } catch {
        return $null
    }

    return $null
}

function Get-LatestStartupTurnMetrics([string] $startupLog) {
    if (-not (Test-Path $startupLog)) {
        return $null
    }

    try {
        $line = Get-Content $startupLog -ErrorAction SilentlyContinue |
            Where-Object { $_ -like '*TurnMetrics request_id=*' } |
            Select-Object -Last 1
        if ([string]::IsNullOrWhiteSpace($line)) {
            return $null
        }

        if ($line -match '^\[(?<ts>[^\]]+)\]\s+TurnMetrics request_id=(?<requestId>\S+)\s+duration_ms=(?<duration>\d+)\s+ttft_ms=(?<ttft>null|\d+)\s+outcome=(?<outcome>\S+)\s+tool_calls=(?<toolCalls>\d+)\s+tool_rounds=(?<toolRounds>\d+)') {
            $ttftValue = $null
            if ($Matches['ttft'] -ne 'null') {
                $ttftValue = [long]$Matches['ttft']
            }

            return [pscustomobject]@{
                timestamp = [datetime]::Parse($Matches['ts'])
                requestId = $Matches['requestId']
                durationMs = [long]$Matches['duration']
                ttftMs = $ttftValue
                outcome = $Matches['outcome']
                toolCalls = [int]$Matches['toolCalls']
                toolRounds = [int]$Matches['toolRounds']
            }
        }
    } catch {
        return $null
    }

    return $null
}

function Wait-MainWindowHandle([System.Diagnostics.Process] $process, [int] $timeoutSeconds) {
    $deadline = (Get-Date).AddSeconds($timeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        $process.Refresh()
        if ($process.MainWindowHandle -ne 0) {
            return $process.MainWindowHandle
        }
        Start-Sleep -Milliseconds 100
    }
    return [IntPtr]::Zero
}

function Focus-MainWindowPromptArea([System.IntPtr] $windowHandle) {
    if ($windowHandle -eq [System.IntPtr]::Zero) {
        return $false
    }

    [void][IxChatWin32]::ShowWindow($windowHandle, [IxChatWin32]::SW_RESTORE)
    [void][IxChatWin32]::SetForegroundWindow($windowHandle)
    Start-Sleep -Milliseconds 350

    $rect = New-Object IxChatWin32+RECT
    if (-not [IxChatWin32]::GetWindowRect($windowHandle, [ref]$rect)) {
        return $false
    }

    $width = [Math]::Max(1, $rect.Right - $rect.Left)
    $height = [Math]::Max(1, $rect.Bottom - $rect.Top)
    $x = $rect.Left + [Math]::Floor($width / 2)
    $y = $rect.Top + [Math]::Floor($height - 34)
    [void][IxChatWin32]::SetCursorPos($x, $y)
    Start-Sleep -Milliseconds 80
    [IxChatWin32]::mouse_event([IxChatWin32]::MOUSEEVENTF_LEFTDOWN, 0, 0, 0, [UIntPtr]::Zero)
    [IxChatWin32]::mouse_event([IxChatWin32]::MOUSEEVENTF_LEFTUP, 0, 0, 0, [UIntPtr]::Zero)
    Start-Sleep -Milliseconds 120
    return $true
}

function New-SqlParameters {
    return (New-Object 'System.Collections.Generic.Dictionary[string,object]' ([StringComparer]::OrdinalIgnoreCase))
}

function Get-ProfileNames([string] $databasePath) {
    $names = New-Object System.Collections.Generic.List[string]
    if (-not (Test-Path $databasePath)) {
        return $names
    }

    try {
        $db = New-Object DBAClientX.SQLite
        for ($offset = 0; $offset -lt 128; $offset++) {
            $sql = "SELECT profile_name FROM ix_app_profiles ORDER BY profile_name LIMIT 1 OFFSET " + $offset
            $name = $db.ExecuteScalar($databasePath, $sql, (New-SqlParameters))
            $normalized = ([string]$name).Trim()
            if ([string]::IsNullOrWhiteSpace($normalized)) {
                break
            }
            [void]$names.Add($normalized)
        }
    } catch {
        # best-effort discovery
    }

    return $names
}

function Get-ProfileJson([string] $databasePath, [string] $profileName) {
    if (-not (Test-Path $databasePath)) {
        return $null
    }
    try {
        $db = New-Object DBAClientX.SQLite
        $parameters = New-SqlParameters
        $parameters["@name"] = $profileName
        return $db.ExecuteScalar($databasePath, "SELECT json FROM ix_app_profiles WHERE profile_name = @name", $parameters)
    } catch {
        return $null
    }
}

function Find-PromptObservationForState([object] $state, [string] $promptText) {
    if ($null -eq $state -or $null -eq $state.conversations) {
        return $null
    }

    $firstUserObservation = $null
    foreach ($conversation in @($state.conversations)) {
        $messages = @($conversation.messages)
        if ($messages.Count -eq 0) {
            continue
        }
        for ($i = 0; $i -lt $messages.Count; $i++) {
            $entry = $messages[$i]
            if ($entry.role -ne "User" -or $entry.text -ne $promptText) {
                continue
            }
            $userTime = [datetime]$entry.time
            if ($null -eq $firstUserObservation) {
                $firstUserObservation = [pscustomobject]@{
                    foundAssistant = $false
                    conversationId = $conversation.id
                    userAt = $userTime
                    assistantAt = $null
                    assistantPreview = $null
                }
            }
            for ($j = $i + 1; $j -lt $messages.Count; $j++) {
                $assistant = $messages[$j]
                if ($assistant.role -ne "Assistant") {
                    continue
                }
                if ([string]::IsNullOrWhiteSpace([string]$assistant.text)) {
                    continue
                }
                return [pscustomobject]@{
                    foundAssistant = $true
                    conversationId = $conversation.id
                    userAt = $userTime
                    assistantAt = [datetime]$assistant.time
                    assistantPreview = ([string]$assistant.text).Substring(0, [Math]::Min(120, ([string]$assistant.text).Length))
                }
            }
        }
    }

    return $firstUserObservation
}

function Find-PromptObservationAcrossProfiles(
    [string] $databasePath,
    [System.Collections.Generic.List[string]] $profileNames,
    [string] $promptText) {
    $userOnly = $null
    foreach ($profileName in @($profileNames)) {
        $json = Get-ProfileJson -databasePath $databasePath -profileName $profileName
        if ([string]::IsNullOrWhiteSpace($json)) {
            continue
        }

        try {
            $state = $json | ConvertFrom-Json -Depth 100
            $observation = Find-PromptObservationForState -state $state -promptText $promptText
            if ($null -eq $observation) {
                continue
            }

            $withProfile = [pscustomobject]@{
                profileName = $profileName
                foundAssistant = $observation.foundAssistant
                conversationId = $observation.conversationId
                userAt = $observation.userAt
                assistantAt = $observation.assistantAt
                assistantPreview = $observation.assistantPreview
            }

            if ($withProfile.foundAssistant) {
                return $withProfile
            }

            if ($null -eq $userOnly) {
                $userOnly = $withProfile
            }
        } catch {
            # best effort parsing while app is writing
        }
    }

    return $userOnly
}

$runResults = New-Object System.Collections.Generic.List[object]

for ($run = 1; $run -le $Runs; $run++) {
    Stop-ChatProcesses
    if (Test-Path $startupLogPath) {
        Remove-Item $startupLogPath -Force
    }

    $message = "bench-first-turn-" + [Guid]::NewGuid().ToString("N").Substring(0, 8)
    $previousAutoPrompt = $env:IXCHAT_BENCH_AUTOSEND_PROMPT
    $env:IXCHAT_BENCH_AUTOSEND_PROMPT = $message
    $launchAt = Get-Date
    $process = Start-Process -FilePath $ExePath -PassThru
    if ($null -eq $previousAutoPrompt) {
        Remove-Item Env:IXCHAT_BENCH_AUTOSEND_PROMPT -ErrorAction SilentlyContinue
    } else {
        $env:IXCHAT_BENCH_AUTOSEND_PROMPT = $previousAutoPrompt
    }
    $startupDone = Wait-StartupDone -timeoutSeconds $StartupTimeoutSeconds -startupLog $startupLogPath
    $startupDoneAt = Get-Date
    $mainWindowHandle = Wait-MainWindowHandle -process $process -timeoutSeconds 20

    $sendAttempted = $true
    $sendAt = $null
    $focusedPrompt = $false
    if ($mainWindowHandle -ne [IntPtr]::Zero) {
        # Optional focus click helps interactive observation while benchmark auto-send runs via env var.
        $focusedPrompt = Focus-MainWindowPromptArea -windowHandle $mainWindowHandle
    }

    $response = $null
    $benchAutoSendBeginAt = $null
    $benchAutoSendDoneAt = $null
    $turnMetrics = $null
    if ($sendAttempted) {
        $profileNames = New-Object System.Collections.Generic.List[string]
        foreach ($profile in @(Get-ProfileNames -databasePath $dbPath)) {
            $normalizedProfile = ([string]$profile).Trim()
            if (-not [string]::IsNullOrWhiteSpace($normalizedProfile) -and -not $profileNames.Contains($normalizedProfile)) {
                [void]$profileNames.Add($normalizedProfile)
            }
        }

        $preferredProfile = ([string]$ProfileName).Trim()
        if (-not [string]::IsNullOrWhiteSpace($preferredProfile) -and -not $profileNames.Contains($preferredProfile)) {
            [void]$profileNames.Add($preferredProfile)
        }
        $deadline = (Get-Date).AddSeconds($TurnTimeoutSeconds)
        while ((Get-Date) -lt $deadline) {
            $response = Find-PromptObservationAcrossProfiles -databasePath $dbPath -profileNames $profileNames -promptText $message
            if ($null -ne $response -and $response.foundAssistant) {
                break
            }

            if ($null -eq $benchAutoSendBeginAt) {
                $benchAutoSendBeginAt = Get-StartupLogMarkerTime -startupLog $startupLogPath -marker "StartupPhase.BenchAutoSend begin"
            }
            if ($null -eq $benchAutoSendDoneAt) {
                $benchAutoSendDoneAt = Get-StartupLogMarkerTime -startupLog $startupLogPath -marker "StartupPhase.BenchAutoSend done"
            }
            if ($benchAutoSendBeginAt -and $benchAutoSendDoneAt) {
                $turnMetrics = Get-LatestStartupTurnMetrics -startupLog $startupLogPath
                # Benchmark hook completed; do not burn the full timeout waiting on DB parsing.
                break
            }

            Start-Sleep -Milliseconds 250
        }

        if ($null -eq $turnMetrics) {
            $turnMetrics = Get-LatestStartupTurnMetrics -startupLog $startupLogPath
        }
    }

    $runResult = [ordered]@{
        run = $run
        processId = $process.Id
        startupDone = $startupDone
        startupMs = [Math]::Round(($startupDoneAt - $launchAt).TotalMilliseconds, 2)
        sendAttempted = $sendAttempted
        promptFocused = $focusedPrompt
        prompt = $message
        sendAt = if ($sendAt) { $sendAt.ToString("O") } else { $null }
        profileName = if ($response) { $response.profileName } else { $null }
        foundUser = $null -ne $response
        foundAssistant = if ($response) { [bool]$response.foundAssistant } else { $false }
        benchAutoSendBeginAt = if ($benchAutoSendBeginAt) { $benchAutoSendBeginAt.ToString("O") } else { $null }
        benchAutoSendDoneAt = if ($benchAutoSendDoneAt) { $benchAutoSendDoneAt.ToString("O") } else { $null }
        benchAutoSendMs = if ($benchAutoSendBeginAt -and $benchAutoSendDoneAt) { [Math]::Round(($benchAutoSendDoneAt - $benchAutoSendBeginAt).TotalMilliseconds, 2) } else { $null }
        turnMetricsRequestId = if ($turnMetrics) { $turnMetrics.requestId } else { $null }
        turnMetricsAt = if ($turnMetrics) { $turnMetrics.timestamp.ToString("O") } else { $null }
        turnMetricsDurationMs = if ($turnMetrics) { $turnMetrics.durationMs } else { $null }
        turnMetricsTtftMs = if ($turnMetrics) { $turnMetrics.ttftMs } else { $null }
        turnMetricsOutcome = if ($turnMetrics) { $turnMetrics.outcome } else { $null }
        turnMetricsToolCalls = if ($turnMetrics) { $turnMetrics.toolCalls } else { $null }
        turnMetricsToolRounds = if ($turnMetrics) { $turnMetrics.toolRounds } else { $null }
        userAt = if ($response) { $response.userAt.ToString("O") } else { $null }
        assistantAt = if ($response -and $response.assistantAt) { $response.assistantAt.ToString("O") } else { $null }
        firstTurnMs = if ($response -and $response.userAt -and $response.assistantAt) { [Math]::Round(($response.assistantAt - $response.userAt).TotalMilliseconds, 2) } else { $null }
        assistantPreview = if ($response) { $response.assistantPreview } else { $null }
    }
    $runResults.Add([pscustomobject]$runResult)

    Write-Host ("Run {0}/{1}: startupDone={2}, startupMs={3}, sendAttempted={4}, foundUser={5}, foundAssistant={6}, firstTurnMs={7}, benchAutoSendMs={8}, ttftMs={9}, durationMs={10}" -f
        $run, $Runs, $runResult.startupDone, $runResult.startupMs, $runResult.sendAttempted, $runResult.foundUser, $runResult.foundAssistant, $runResult.firstTurnMs, $runResult.benchAutoSendMs, $runResult.turnMetricsTtftMs, $runResult.turnMetricsDurationMs)
}

$completed = @($runResults | Where-Object { $_.foundAssistant -eq $true -and $null -ne $_.firstTurnMs })
$avgFirstTurn = if ($completed.Count -gt 0) {
    [Math]::Round((($completed | Measure-Object -Property firstTurnMs -Average).Average), 2)
} else {
    $null
}

$report = [pscustomobject]@{
    schema_version = "chat-first-turn-profile-v1"
    generated_utc = [datetime]::UtcNow.ToString("O")
    exe_path = $ExePath
    db_path = $dbPath
    startup_log_path = $startupLogPath
    runs_requested = $Runs
    runs_with_assistant = $completed.Count
    avg_first_turn_ms = $avgFirstTurn
    runs = $runResults
}

$jsonReport = $report | ConvertTo-Json -Depth 8
if (-not [string]::IsNullOrWhiteSpace($OutFile)) {
    $resolvedOut = [System.IO.Path]::GetFullPath($OutFile)
    $outDir = [System.IO.Path]::GetDirectoryName($resolvedOut)
    if (-not [string]::IsNullOrWhiteSpace($outDir) -and -not (Test-Path $outDir)) {
        New-Item -ItemType Directory -Path $outDir -Force | Out-Null
    }
    $jsonReport | Set-Content -Path $resolvedOut -Encoding UTF8
    Write-Host "Saved report: $resolvedOut"
}

$jsonReport
