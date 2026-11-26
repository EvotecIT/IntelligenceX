#Requires -Version 7.5
<#
.SYNOPSIS
Installs, updates, rolls back or reports the Ledger agent service for the signed-in user.
.DESCRIPTION
The agent service runs as its own process: the sign-in task EvotecIT-IX-Ledger-agent on Windows, the LaunchAgent
com.evotec.intelligencex-ledger.agent on macOS. It installs from a kit built by Build/Publish-LedgerAgent.ps1; the kit's
agent-manifest.json names the source commit and every file's SHA-256. Each version lives in
agent-service/versions/<commit>, and agent-service/install.json records the current and previous one, so Rollback
returns to the previous version without a kit. A kit built with -Unmerged installs only with -AllowUnmerged, for an
isolated test instance.

Install and Rollback preview by default; -Apply makes the change. Stop stops only the agent process its own receipt
names (with a matching start time) and the startup task that runs this install, never other Ledger processes such as
the MCP servers sessions run, so stop the agent with it before a runtime update. The Ledger service and reporter installs, their
snapshots and scheduled tasks are left alone. The one exception is the first install over an older reporter that ran
the agent itself: its agent settings move to agent.json and the reporter is restarted before the new process starts,
so the two never run together.
.EXAMPLE
./Install-LedgerAgent.ps1 -Action Install -Kit <kit> -Capability windows-powershell
./Install-LedgerAgent.ps1 -Action Install -Kit <kit> -Capability windows-powershell -Apply
./Install-LedgerAgent.ps1 -Action Rollback -Apply
./Install-LedgerAgent.ps1 -Action Stop
./Install-LedgerAgent.ps1
#>
[CmdletBinding()]
param(
    [ValidateSet('Install', 'Rollback', 'Status', 'Stop')][string]$Action = 'Status',
    [string]$Kit,
    [string]$Root,
    [string]$ReporterConfig,
    [string[]]$Capability = @(),
    [string]$WorkspaceRoot,
    [string[]]$RepositoryRoot = @(),
    [string]$Codex,
    [string]$Claude,
    [switch]$AllowUnmerged,
    [switch]$Apply,
    # A version change waits up to this long for running helper turns to finish, since stopping the agent ends them.
    [int]$WaitMinutes = 60,
    # Switch versions without waiting for running helper turns; their asks are reconciled and answered as stopped.
    [switch]$Now
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$TaskName = 'EvotecIT-IX-Ledger-agent'
$Label = 'com.evotec.intelligencex-ledger.agent'
if (-not $Root) { $Root = if ($IsWindows) { Join-Path $env:LOCALAPPDATA 'IntelligenceX/Ledger' } else { Join-Path $HOME '.local/share/intelligencex-ledger' } }
$Root = [IO.Path]::GetFullPath($Root)
if (-not $ReporterConfig) { $ReporterConfig = Join-Path $Root ($IsWindows ? 'reporter.json' : 'mac-reporter.json') }
$ReporterConfig = [IO.Path]::GetFullPath($ReporterConfig)
$AgentConfig = Join-Path $Root 'agent.json'
$ServiceRoot = Join-Path $Root 'agent-service'
$Versions = Join-Path $ServiceRoot 'versions'
$Receipt = Join-Path $ServiceRoot 'install.json'
$Dotnet = if ($IsWindows) { (Get-Command dotnet -ErrorAction Stop).Source } else {
    # The LaunchAgent gets no shell PATH, so the host is resolved here and written into its plist.
    @($env:DOTNET_ROOT ? (Join-Path $env:DOTNET_ROOT 'dotnet') : $null; (Join-Path $HOME '.dotnet/dotnet'); '/usr/local/share/dotnet/dotnet'
        '/opt/homebrew/bin/dotnet'; (Get-Command dotnet -ErrorAction SilentlyContinue)?.Source) |
        Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -First 1
}
if (-not $Dotnet) { throw 'No dotnet host found; install the .NET runtime or set DOTNET_ROOT.' }
$Log = Join-Path $ServiceRoot 'agent.log'
$Uid = if ($IsWindows) { $null } else { (& /usr/bin/id -u).Trim() }
$Plist = if ($IsWindows) { $null } else { Join-Path $HOME "Library/LaunchAgents/$Label.plist" }

function Read-Json([string]$Path) { if (Test-Path -LiteralPath $Path) { Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json } }

# The Ledger program inside a kit or installed version; the manifest names its folder (service on Windows, app on macOS).
function Get-Assembly([string]$Path) {
    $folder = (Read-Json (Join-Path $Path 'agent-manifest.json')).appFolder
    return Join-Path $Path "$folder/IntelligenceX.Ledger.dll"
}

function Write-Receipt([string]$Current, [string]$Previous) {
    $null = New-Item -ItemType Directory -Force -Path $ServiceRoot
    [ordered]@{ current = $Current; previous = $Previous; changedAtUtc = [DateTimeOffset]::UtcNow.ToString('O') } |
        ConvertTo-Json | Set-Content -LiteralPath $Receipt -Encoding utf8NoBOM
}

# Verifies every file the kit's agent manifest names, and that the kit holds nothing else.
function Assert-Kit([string]$Path) {
    $manifest = Read-Json (Join-Path $Path 'agent-manifest.json')
    if (-not $manifest -or $manifest.sourceCommit -notmatch '^[0-9a-f]{40}$') { throw "No agent-manifest.json with a source commit in $Path." }
    $expected = $IsWindows ? 'win-x64' : 'osx-arm64'
    if ($manifest.runtime -cne $expected) { throw "The kit is for $($manifest.runtime); this machine needs $expected." }
    # Live machines run merged master only; a kit built with -Unmerged is for an isolated test instance.
    # Kits from before the rule have no merged field and stay installable, so rollback to them keeps working.
    $merged = $manifest.PSObject.Properties['merged']
    if ($merged -and $merged.Value -eq $false -and -not $AllowUnmerged) { throw "The kit was built from unmerged commit $($manifest.sourceCommit). Live machines run merged master only." }
    $listed = @{}
    foreach ($file in $manifest.files) {
        $full = [IO.Path]::GetFullPath((Join-Path $Path $file.path))
        if (-not $full.StartsWith($Path + [IO.Path]::DirectorySeparatorChar, [StringComparison]::Ordinal)) { throw "Unsafe path in the manifest: $($file.path)" }
        if ((Get-FileHash -LiteralPath $full -Algorithm SHA256).Hash.ToLowerInvariant() -cne $file.sha256) { throw "Hash mismatch: $($file.path)" }
        $listed[$full] = $true
    }
    foreach ($folder in $manifest.appFolder, 'tools') {
        Get-ChildItem -LiteralPath (Join-Path $Path $folder) -File -Recurse | Where-Object { -not $listed.ContainsKey($_.FullName) } |
            ForEach-Object { throw "File not in the manifest: $($_.FullName)" }
    }
    return $manifest.sourceCommit
}

function Invoke-AgentInstall([string]$Version, [switch]$Change) {
    $arguments = @((Get-Assembly $Version), 'agent-install', '--reporter', $ReporterConfig, '--version', $Version,
        '--agent-config', $AgentConfig)
    foreach ($name in $Capability) { $arguments += @('--capability', $name) }
    if ($WorkspaceRoot) { $arguments += @('--workspace-root', $WorkspaceRoot) }
    foreach ($path in $RepositoryRoot) { $arguments += @('--repository-root', $path) }
    if ($Codex) { $arguments += @('--codex', $Codex) }
    if ($Claude) { $arguments += @('--claude', $Claude) }
    if ($Change) { $arguments += '--apply' }
    $output = & $Dotnet @arguments
    if ($LASTEXITCODE -ne 0) { throw "agent-install failed for $Version." }
    return ($output -join "`n" | ConvertFrom-Json)
}

# The running agent, from the receipt it writes at start, only while that process is still the agent.
function Get-AgentProcess {
    $agent = Read-Json $AgentConfig
    if (-not $agent) { return $null }
    $receipt = Read-Json (Join-Path $agent.stateDirectory 'agent-process.json')
    if (-not $receipt) { return $null }
    $process = Get-Process -Id $receipt.pid -ErrorAction SilentlyContinue
    # A process that exits while it is being looked at (launchctl bootout stops it) has no start time on macOS.
    if (-not $process -or $process.HasExited -or -not $process.StartTime) { return $null }
    if ([Math]::Abs(($process.StartTime.ToUniversalTime() - ([DateTimeOffset]$receipt.startedAtUtc).UtcDateTime).TotalSeconds) -gt 2) { return $null }
    return $process
}

# The startup task or LaunchAgent, only when it launches exactly this install (the launch arguments Start-Agent writes);
# another install's is left alone, including one whose folder merely starts with this one's.
function Test-OwnStartup {
    if ($IsWindows) {
        $task = Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue
        $arguments = "//B //Nologo `"$(Join-Path $ServiceRoot 'Start-LedgerAgent.vbs')`""
        return [bool]($task -and @($task.Actions | Where-Object { [string]::Equals($_.Arguments, $arguments, [StringComparison]::OrdinalIgnoreCase) }))
    }
    $config = '<string>agent</string><string>--config</string><string>' + [Security.SecurityElement]::Escape($AgentConfig) + '</string>'
    return (Test-Path -LiteralPath $Plist) -and (Get-Content -LiteralPath $Plist -Raw).Contains($config)
}

# Stops the startup entry (when it is this install's) and the agent process its receipt names. Returns that process's id.
function Stop-Agent {
    $own = Test-OwnStartup
    if ($IsWindows) {
        if ($own) { Stop-ScheduledTask -TaskName $TaskName }
        if ($process = Get-AgentProcess) {
            Stop-Process -Id $process.Id
            if (-not $process.WaitForExit(15000)) { throw "The agent service (process $($process.Id)) did not stop; nothing was changed." }
            return $process.Id
        }
    } else {
        if ($own) {
            & /bin/launchctl bootout "gui/$Uid/$Label" 2>$null
            # KeepAlive would restart a process stopped while the job is still loaded. bootout returns before the job is
            # gone, so the check waits up to 30 seconds for it to unload.
            # The last check comes after the last pause, so an unload during that pause still counts.
            $deadline = [DateTimeOffset]::UtcNow.AddSeconds(30)
            while ($true) {
                & /bin/launchctl print "gui/$Uid/$Label" *> $null
                $loaded = $LASTEXITCODE -eq 0
                if (-not $loaded -or [DateTimeOffset]::UtcNow -ge $deadline) { break }
                Start-Sleep -Milliseconds 500
            }
            if ($loaded) { throw "launchctl could not unload $Label; nothing was changed." }
        }
        if ($process = Get-AgentProcess) {
            & /bin/kill -TERM $process.Id 2>$null
            if (-not $process.WaitForExit(30000)) { throw "The agent service (process $($process.Id)) did not stop; nothing was changed." }
            return $process.Id
        }
    }
}

function Start-Agent([string]$Version) {
    $assembly = Get-Assembly $Version
    # One previous log is kept; the agent writes a line only when something changes.
    if ((Test-Path -LiteralPath $Log) -and (Get-Item -LiteralPath $Log).Length -gt 10MB) { Move-Item -LiteralPath $Log -Destination "$Log.1" -Force }
    if ($IsWindows) {
        # wscript runs the agent without a console window and returns its exit code, so the task restarts it on failure;
        # cmd appends its output to agent.log.
        $launcher = Join-Path $ServiceRoot 'Start-LedgerAgent.vbs'
        $inner = "`"$Dotnet`" `"$assembly`" agent --config `"$AgentConfig`" >> `"$Log`" 2>&1"
        $command = '"' + ("cmd.exe /d /s /c `"$inner`"").Replace('"', '""') + '"'
        Set-Content -LiteralPath $launcher -Encoding ascii -Value @(
            'Set shell = CreateObject("WScript.Shell")',
            "WScript.Quit shell.Run($command, 0, True)")
        $task = New-ScheduledTaskAction -Execute (Join-Path $env:WINDIR 'System32/wscript.exe') -Argument "//B //Nologo `"$launcher`""
        $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable `
            -ExecutionTimeLimit ([TimeSpan]::Zero) -RestartCount 999 -RestartInterval (New-TimeSpan -Minutes 1) -MultipleInstances IgnoreNew
        $principal = New-ScheduledTaskPrincipal -UserId ([Security.Principal.WindowsIdentity]::GetCurrent().Name) -LogonType Interactive -RunLevel Limited
        $null = Register-ScheduledTask -TaskName $TaskName -Action $task -Trigger (New-ScheduledTaskTrigger -AtLogOn -User $principal.UserId) `
            -Settings $settings -Principal $principal -Description 'IntelligenceX Ledger agent service' -Force
        Start-ScheduledTask -TaskName $TaskName
    } else {
        $escape = { param($v) [Security.SecurityElement]::Escape($v) }
        # A LaunchAgent starts with only the system PATH, so helpers would miss Homebrew tools (gh, claude, node) and the
        # user's own. The agent and the helpers it starts get the usual user locations, then this shell's PATH.
        $path = @((Split-Path $Dotnet), (Join-Path $HOME '.local/bin'), (Join-Path $HOME '.dotnet/tools'), '/opt/homebrew/bin', '/opt/homebrew/sbin',
            '/usr/local/bin') + ($env:PATH -split ':') + @('/usr/bin', '/bin', '/usr/sbin', '/sbin') |
            Where-Object { $_ } | Select-Object -Unique
        $null = New-Item -ItemType Directory -Force -Path (Split-Path $Plist)
        Set-Content -LiteralPath $Plist -Encoding utf8NoBOM -Value @"
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
<key>Label</key><string>$Label</string>
<key>ProgramArguments</key><array><string>$(& $escape $Dotnet)</string><string>$(& $escape $assembly)</string><string>agent</string><string>--config</string><string>$(& $escape $AgentConfig)</string></array>
<key>EnvironmentVariables</key><dict><key>DOTNET_ROOT</key><string>$(& $escape (Split-Path $Dotnet))</string><key>HOME</key><string>$(& $escape $HOME)</string><key>PATH</key><string>$(& $escape ($path -join ':'))</string></dict>
<key>RunAtLoad</key><true/><key>KeepAlive</key><true/><key>ThrottleInterval</key><integer>30</integer><key>ProcessType</key><string>Background</string>
<key>StandardOutPath</key><string>$(& $escape $Log)</string><key>StandardErrorPath</key><string>$(& $escape $Log)</string>
</dict></plist>
"@
        & /bin/launchctl bootstrap "gui/$Uid" $Plist
        if ($LASTEXITCODE -ne 0) { throw 'launchctl bootstrap failed.' }
    }
    # A busy machine can take about a minute to bring the agent up, so it gets three.
    $deadline = [DateTimeOffset]::UtcNow.AddMinutes(3)
    while (-not (Get-AgentProcess) -and [DateTimeOffset]::UtcNow -lt $deadline) { Start-Sleep -Seconds 2 }
    if (-not (Get-AgentProcess)) { throw "The agent service did not start within three minutes; see $Log." }
}

# Older builds ran the agent inside the reporter. Restart it once its agent section is gone, before the new process starts.
function Restart-Reporter {
    if ($IsWindows) {
        $receipt = Read-Json (Join-Path $Root 'reporter-process.json')
        $before = $receipt.process.pid
        # Only the process the receipt names: a reused process id belongs to something else.
        $process = Get-Process -Id $before -ErrorAction SilentlyContinue
        if ($process -and [Math]::Abs(($process.StartTime.ToUniversalTime() - ([DateTimeOffset]$receipt.process.startedAtUtc).UtcDateTime).TotalSeconds) -le 2) {
            Stop-Process -Id $before
        } else { throw 'The reporter in reporter-process.json is not running; start its task, then run this again.' }
        $deadline = [DateTimeOffset]::UtcNow.AddSeconds(150)
        do { Start-Sleep -Seconds 5; $now = (Read-Json (Join-Path $Root 'reporter-process.json')).process.pid }
        while ($now -eq $before -and [DateTimeOffset]::UtcNow -lt $deadline)
        if ($now -eq $before) { throw 'The reporter did not restart; its startup check runs every minute.' }
    } else {
        & /bin/launchctl kickstart -k "gui/$Uid/com.evotec.intelligencex-ledger.reporter"
        if ($LASTEXITCODE -ne 0) { throw 'The reporter LaunchAgent could not be restarted.' }
    }
}

# Helper turns the running agent has in progress: run records with a turn under way and not finished. Stopping the agent
# ends them (a turn runs on the agent's own Codex server or Claude process), so a version change waits for them first.
function Get-RunningTurn {
    $state = (Read-Json $AgentConfig)?.stateDirectory
    if (-not $state -or -not (Test-Path -LiteralPath (Join-Path $state 'runs'))) { return @() }
    @(Get-ChildItem -LiteralPath (Join-Path $state 'runs') -Filter '*.json' -File |
        Where-Object { $_.Name -notlike '*.tokens.json' } |
        ForEach-Object { try { Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json } catch { $null } } |
        Where-Object { $_ -and $_.Phase -eq 'turn' -and $_.TurnId -and -not $_.ResultRef })
}

function Wait-RunningTurn {
    if ($Now -or -not (Get-AgentProcess)) { return }
    $deadline = [DateTimeOffset]::UtcNow.AddMinutes($WaitMinutes)
    while (($running = @(Get-RunningTurn)).Count -gt 0) {
        if ([DateTimeOffset]::UtcNow -ge $deadline) {
            throw "$($running.Count) helper turn(s) still running after $WaitMinutes minutes ($(@($running.RequestId) -join ', ')); nothing was changed. Run again later, or with -Now to stop them."
        }
        Write-Host "Waiting for $($running.Count) helper turn(s) to finish: $(@($running.RequestId) -join ', ')"
        Start-Sleep -Seconds 30
    }
}

# The operator CLI (ix ledger policy, project, coordination) lives in coordination/client, where operators and the
# status scripts look for it. It is refreshed from the installed version, so it never lags behind the service it talks
# to. A copy that can't be replaced (a CLI still running) is kept with a warning; the next install tries again.
function Update-OperatorClient([string]$Version) {
    $source = Join-Path $Version 'tools/cli'
    if (-not (Test-Path -LiteralPath $source)) { return }
    $client = Join-Path $Root 'coordination/client'
    $commit = Split-Path $Version -Leaf
    $marker = Join-Path $client '.ledger-client-commit'
    # Another install may be swapping the folder at this moment; an unreadable marker just means copying again.
    $installed = try { (Get-Content -LiteralPath $marker -Raw -ErrorAction Stop).Trim() } catch { $null }
    if ($installed -eq $commit) { return }
    # Each run stages in its own folder, so two installs at once never share one; the coordination folder may not exist
    # yet when the reporter configuration lives outside the root.
    $staging = "$client.partial-" + [Guid]::NewGuid().ToString('N')
    $previous = "$client.previous"
    try {
        $null = New-Item -ItemType Directory -Force -Path (Split-Path $client -Parent)
        Copy-Item -LiteralPath $source -Destination $staging -Recurse
        Set-Content -LiteralPath (Join-Path $staging '.ledger-client-commit') -Value $commit -NoNewline
        if (Test-Path -LiteralPath $previous) { Remove-Item -LiteralPath $previous -Recurse -Force }
        if (Test-Path -LiteralPath $client) { Move-Item -LiteralPath $client -Destination $previous }
        Move-Item -LiteralPath $staging -Destination $client
    } catch {
        Write-Warning "Kept the existing operator CLI in ${client}: $($_.Exception.Message)"
        if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -Recurse -Force -ErrorAction SilentlyContinue }
        if (-not (Test-Path -LiteralPath $client) -and (Test-Path -LiteralPath $previous)) { Move-Item -LiteralPath $previous -Destination $client }
    }
}

function Switch-Version([string]$Version, [string]$Previous) {
    Wait-RunningTurn
    $result = Invoke-AgentInstall $Version -Change
    $restart = @($result.steps | Where-Object action -eq 'restart-reporter').Count -gt 0
    $null = Stop-Agent
    if ($restart) { Restart-Reporter }
    Start-Agent $Version
    Write-Receipt (Split-Path $Version -Leaf) $Previous
    Update-OperatorClient $Version
    Remove-OldVersions @((Split-Path $Version -Leaf), $Previous)
}

# Keeps the named versions and removes the rest. A folder that a running process uses (an MCP server of a
# session that is still open) is left whole for a later install to remove. Matching on command lines covers
# both an executable started from the folder and a dll hosted by dotnet, as on macOS and Linux.
function Remove-OldVersions([string[]]$Keep) {
    $listErrors = $null
    $running = @(@(if ($IsWindows) {
            Get-CimInstance Win32_Process -ErrorAction SilentlyContinue -ErrorVariable listErrors |
                ForEach-Object { if ($_.CommandLine) { $_.CommandLine } else { $_.ExecutablePath } }
        } else {
            & ps -axww -o command= 2>$null
            if ($LASTEXITCODE -ne 0) { $listErrors = "ps exited with $LASTEXITCODE" }
        }) | Where-Object { $_ })
    # Without a process list nothing can be shown to be unused, so every old folder waits for a later install.
    if ($listErrors -or $running.Count -eq 0) {
        Write-Host "Kept old versions: running processes could not be listed; a later install removes them."
        return
    }
    foreach ($folder in Get-ChildItem -LiteralPath $Versions -Directory | Where-Object { $_.Name -notin $Keep }) {
        $prefix = $folder.FullName.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
        $users = @($running | Where-Object { $_.Contains($prefix, [StringComparison]::OrdinalIgnoreCase) }).Count
        if ($users -gt 0) {
            Write-Host "Kept $($folder.Name): $users running process(es) still use it; a later install removes it."
            continue
        }
        Remove-Item -LiteralPath $folder.FullName -Recurse -Force -ErrorAction SilentlyContinue -ErrorVariable removeErrors
        if ($removeErrors) { Write-Host "Kept $($folder.Name): $($removeErrors[0].Exception.Message)" }
    }
}

$installed = Read-Json $Receipt
$current = if ($installed) { $installed.current } else { $null }
$recordedPrevious = if ($installed) { $installed.previous } else { $null }
switch ($Action) {
    'Stop' {
        $stopped = Stop-Agent
        [pscustomobject]@{ Action = 'Stop'; Stopped = $stopped; Running = [bool](Get-AgentProcess) }
    }
    'Status' {
        [pscustomobject]@{
            Current = $current; Previous = $recordedPrevious
            Running = [bool](Get-AgentProcess); ProcessId = (Get-AgentProcess)?.Id
            Service = $IsWindows ? (Get-ScheduledTask -TaskName $TaskName -ErrorAction SilentlyContinue)?.State : ((& /bin/launchctl print "gui/$Uid/$Label" 2>$null) ? 'loaded' : 'not loaded')
            AgentConfig = $AgentConfig
        }
    }
    'Install' {
        if (-not $Kit) { throw 'Pass -Kit with a kit from Build/Publish-LedgerAgent.ps1.' }
        $Kit = [IO.Path]::GetFullPath($Kit)
        $commit = Assert-Kit $Kit
        $version = Join-Path $Versions $commit
        $previous = if ($current -and $current -ne $commit) { $current } else { $recordedPrevious }
        if (-not $Apply) {
            $plan = Invoke-AgentInstall $Kit
            [pscustomobject]@{ Action = 'Install'; Commit = $commit; Current = $current; Version = $version
                Configuration = @($plan.steps | ForEach-Object action); Apply = 'Run again with -Apply.' }
            return
        }
        if (-not (Test-Path -LiteralPath $version)) {
            $staging = "$version.partial"
            if (Test-Path -LiteralPath $staging) { Remove-Item -LiteralPath $staging -Recurse -Force }
            $null = New-Item -ItemType Directory -Force -Path $staging
            $folder = (Read-Json (Join-Path $Kit 'agent-manifest.json')).appFolder
            Copy-Item -LiteralPath (Join-Path $Kit $folder) -Destination (Join-Path $staging $folder) -Recurse
            Copy-Item -LiteralPath (Join-Path $Kit 'tools') -Destination (Join-Path $staging 'tools') -Recurse
            Copy-Item -LiteralPath (Join-Path $Kit 'agent-manifest.json') -Destination $staging
            Move-Item -LiteralPath $staging -Destination $version
        }
        $null = Assert-Kit $version
        if ($current -eq $commit -and (Get-AgentProcess) -and @((Invoke-AgentInstall $version).steps).Count -eq 0) {
            Update-OperatorClient $version
            [pscustomobject]@{ Installed = $commit; Previous = $recordedPrevious; Running = $true; Changed = $false }
            return
        }
        Switch-Version $version $previous
        [pscustomobject]@{ Installed = $commit; Previous = $previous; Running = [bool](Get-AgentProcess) }
    }
    'Rollback' {
        if (-not $recordedPrevious) { throw 'No previous version is recorded.' }
        $version = Join-Path $Versions $recordedPrevious
        $null = Assert-Kit $version
        if (-not $Apply) { [pscustomobject]@{ Action = 'Rollback'; From = $current; To = $recordedPrevious; Apply = 'Run again with -Apply.' }; return }
        Switch-Version $version $current
        [pscustomobject]@{ Installed = $recordedPrevious; Previous = $current; Running = [bool](Get-AgentProcess) }
    }
}
