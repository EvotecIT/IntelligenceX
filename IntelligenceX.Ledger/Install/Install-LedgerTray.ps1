#Requires -Version 7.5
<#
.SYNOPSIS
Installs, updates, removes or reports the IX Ledger tray (Windows) or menu bar (macOS) app for the signed-in user.
.DESCRIPTION
The tray only reads the Ledger: whether the service answers, each machine's reporter, and GitHub, Codex and Claude
sign-in with the changes to them. It installs from a kit built by Build/Publish-LedgerAgent.ps1, whose
agent-manifest.json names the source commit and every file's SHA-256; tools/tray holds the app. The app goes to
ledger-tray/app in the Ledger folder and starts at login: a Run value on Windows, the LaunchAgent
com.evotec.intelligencex.ledger-tray on macOS, both written by the app itself. A kit built with -Unmerged installs only
with -AllowUnmerged.

The tray reads client.json (endpoint, read token and, over the network, the certificate fingerprint) in the Ledger
folder. Without it the icon shows that it has no read settings.

Install and Remove preview by default; -Apply makes the change. Only a tray started from this install is stopped.
.EXAMPLE
./Install-LedgerTray.ps1 -Action Install -Kit <kit>
./Install-LedgerTray.ps1 -Action Install -Kit <kit> -Apply
./Install-LedgerTray.ps1 -Action Remove -Apply
./Install-LedgerTray.ps1
#>
[CmdletBinding()]
param(
    [ValidateSet('Install', 'Remove', 'Status')][string]$Action = 'Status',
    [string]$Kit,
    [string]$Root,
    [switch]$AllowUnmerged,
    [switch]$Apply
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$Label = 'com.evotec.intelligencex.ledger-tray'
if (-not $Root) { $Root = if ($IsWindows) { Join-Path $env:LOCALAPPDATA 'IntelligenceX/Ledger' } else { Join-Path $HOME '.local/share/intelligencex-ledger' } }
$Root = [IO.Path]::GetFullPath($Root)
$Home_ = Join-Path $Root 'ledger-tray'
$App = Join-Path $Home_ 'app'
$Executable = Join-Path $App ($IsWindows ? 'IntelligenceX.Ledger.Tray.exe' : 'IntelligenceX.Ledger.Tray')
$Uid = if ($IsWindows) { $null } else { (& /usr/bin/id -u).Trim() }
$Plist = if ($IsWindows) { $null } else { Join-Path $HOME "Library/LaunchAgents/$Label.plist" }
if (-not $IsWindows -and -not $env:DOTNET_ROOT) {
    # The app host needs to find .NET; the tray writes the same folder into its LaunchAgent.
    $env:DOTNET_ROOT = @((Join-Path $HOME '.dotnet'), '/usr/local/share/dotnet', '/opt/homebrew/opt/dotnet/libexec') |
        Where-Object { Test-Path -LiteralPath (Join-Path $_ 'dotnet') } | Select-Object -First 1
}

# The Ledger folder as one quoted argument for Start-Process on Windows, where a path can't contain a quote; a trailing
# backslash is doubled so it doesn't escape the closing quote.
$RootArgument = '"' + ($Root.EndsWith('\') ? $Root + '\' : $Root) + '"'

# Runs the tray for one command and returns its exit code. On Windows the tray is a GUI app, so calling it directly
# neither waits nor sets $LASTEXITCODE. Each argument goes in as it is, so a folder name with quotes or spaces arrives
# whole on a Mac too.
function Invoke-Tray([string[]]$Arguments) {
    $start = [Diagnostics.ProcessStartInfo]::new($Executable)
    $start.UseShellExecute = $false
    foreach ($argument in $Arguments + @('--ledger-dir', $Root)) { $start.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::Start($start)
    try { $process.WaitForExit(); return $process.ExitCode } finally { $process.Dispose() }
}

function Get-Tray {
    @(Get-Process -Name 'IntelligenceX.Ledger.Tray' -ErrorAction SilentlyContinue | Where-Object {
        $_.Path -and [IO.Path]::GetFullPath($_.Path).StartsWith($App + [IO.Path]::DirectorySeparatorChar, [StringComparison]::Ordinal)
    })
}

# The login entry is shared by every root, so it counts as this install's only when it starts this executable.
function Test-OwnLogin {
    if ($IsWindows) {
        $value = (Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'IX Ledger tray' -ErrorAction SilentlyContinue)?.'IX Ledger tray'
        return [bool]$value -and $value.Contains('"' + $Executable + '"', [StringComparison]::OrdinalIgnoreCase)
    }
    return (Test-Path -LiteralPath $Plist) -and (Get-Content -LiteralPath $Plist -Raw).Contains('<string>' + [Security.SecurityElement]::Escape($Executable) + '</string>')
}

# The loaded job can still be another root's even after this install rewrote the shared plist.
function Test-OwnLoadedJob {
    $loaded = & /bin/launchctl print "gui/$Uid/$Label" 2>$null
    if ($LASTEXITCODE -ne 0) { return $false }
    # launchctl prints the loaded program as "program = <path>"; it has to be this executable exactly.
    $program = @($loaded | Where-Object { $_ -match '^\s*program = (.+)$' } | ForEach-Object { $Matches[1].Trim() }) | Select-Object -First 1
    return [string]::Equals($program, $Executable, [StringComparison]::Ordinal)
}

function Stop-Tray {
    if (-not $IsWindows -and (Test-OwnLoadedJob)) {
        & /bin/launchctl bootout "gui/$Uid/$Label" 2>$null
        # bootout returns before the job is gone; bootstrapping again too early fails.
        $deadline = [DateTime]::UtcNow.AddSeconds(15)
        do {
            & /bin/launchctl print "gui/$Uid/$Label" *> $null
            $loaded = $LASTEXITCODE -eq 0
            if ($loaded) { Start-Sleep -Milliseconds 250 }
        } while ($loaded -and [DateTime]::UtcNow -lt $deadline)
        if ($loaded) { throw "launchctl could not unload $Label; nothing was changed." }
    }
    foreach ($process in @(Get-Tray)) { $process.Kill(); $null = $process.WaitForExit(10000) }
}

# Verifies the tray's files against the kit's agent manifest, so only what the merged build produced is installed.
function Assert-Kit([string]$Path) {
    $Path = [IO.Path]::GetFullPath($Path)
    $manifest = Get-Content -LiteralPath (Join-Path $Path 'agent-manifest.json') -Raw | ConvertFrom-Json
    if ($manifest.sourceCommit -notmatch '^[0-9a-f]{40}$') { throw "No agent-manifest.json with a source commit in $Path." }
    $expected = $IsWindows ? 'win-x64' : 'osx-arm64'
    if ($manifest.runtime -cne $expected) { throw "The kit is for $($manifest.runtime); this machine needs $expected." }
    $merged = $manifest.PSObject.Properties['merged']
    if ($merged -and $merged.Value -eq $false -and -not $AllowUnmerged) { throw "The kit was built from unmerged commit $($manifest.sourceCommit). Live machines run merged master only." }
    $tray = @($manifest.files | Where-Object { $_.path.StartsWith('tools/tray/', [StringComparison]::Ordinal) })
    if ($tray.Count -eq 0) { throw 'The kit has no tray (tools/tray).' }
    $listed = @{}
    foreach ($file in $tray) {
        $full = [IO.Path]::GetFullPath((Join-Path $Path $file.path))
        if ((Get-FileHash -LiteralPath $full -Algorithm SHA256).Hash.ToLowerInvariant() -cne $file.sha256) { throw "Hash mismatch: $($file.path)" }
        $listed[$full] = $true
    }
    Get-ChildItem -LiteralPath (Join-Path $Path 'tools/tray') -File -Recurse | Where-Object { -not $listed.ContainsKey($_.FullName) } |
        ForEach-Object { throw "File not in the manifest: $($_.FullName)" }
    return $manifest.sourceCommit
}

switch ($Action) {
    'Status' {
        [pscustomobject]@{
            Installed = Test-Path -LiteralPath $Executable
            Version = (Test-Path -LiteralPath (Join-Path $Home_ 'install.json')) ? (Get-Content -LiteralPath (Join-Path $Home_ 'install.json') -Raw | ConvertFrom-Json).sourceCommit : $null
            Running = @(Get-Tray).Count -gt 0
            ReadSettings = Test-Path -LiteralPath (Join-Path $Root 'client.json')
            StartAtLogin = Test-OwnLogin
        }
    }
    'Install' {
        if (-not $Kit) { throw 'Install needs -Kit.' }
        $commit = Assert-Kit $Kit
        if (-not $Apply) { "Would install the tray from $commit into $App and start it at login. Add -Apply."; return }
        if (-not $IsWindows) {
            # The LaunchAgent label is shared by every root; another root's loaded tray would make bootstrap fail
            # after this install had already changed the login entry.
            & /bin/launchctl print "gui/$Uid/$Label" *> $null
            if ($LASTEXITCODE -eq 0 -and -not (Test-OwnLoadedJob)) {
                throw "Another tray is loaded as $Label; remove it with its own -Root first. Nothing was changed."
            }
        }
        $null = New-Item -ItemType Directory -Force -Path $Home_
        $staging = Join-Path $Home_ ('app.new-' + [guid]::NewGuid().ToString('N'))
        Copy-Item -LiteralPath (Join-Path ([IO.Path]::GetFullPath($Kit)) 'tools/tray') -Destination $staging -Recurse
        if (-not $IsWindows) { & /bin/chmod +x (Join-Path $staging 'IntelligenceX.Ledger.Tray') }
        Stop-Tray
        if (Test-Path -LiteralPath $App) { Remove-Item -LiteralPath $App -Recurse -Force }
        Move-Item -LiteralPath $staging -Destination $App
        [ordered]@{ sourceCommit = $commit; installedAtUtc = [DateTimeOffset]::UtcNow.ToString('O') } | ConvertTo-Json |
            Set-Content -LiteralPath (Join-Path $Home_ 'install.json') -Encoding utf8NoBOM
        # The tray records its Ledger folder in the login entry, so the next login starts the same tray.
        if ((Invoke-Tray @('login', 'on')) -ne 0) { throw 'The tray could not set itself to start at login.' }
        # A trailing backslash is doubled so it doesn't escape the closing quote (D:\ would otherwise arrive as D:").
        if ($IsWindows) { Start-Process -FilePath $Executable -ArgumentList @('--ledger-dir', $RootArgument) -WorkingDirectory $App }
        else {
            & /bin/launchctl bootstrap "gui/$Uid" $Plist
            if ($LASTEXITCODE -ne 0) { throw 'launchctl bootstrap failed.' }
        }
        "Installed the tray from $commit and started it."
    }
    'Remove' {
        if (-not $Apply) { "Would stop the tray, turn off start at login and remove $App. Add -Apply."; return }
        Stop-Tray
        if ((Test-Path -LiteralPath $Executable) -and (Test-OwnLogin)) {
            # Keep the app while its login entry remains, so the entry never points at a missing program.
            if ((Invoke-Tray @('login', 'off')) -ne 0) { throw 'The tray could not turn off start at login; the app was left in place.' }
        }
        if (Test-Path -LiteralPath $App) { Remove-Item -LiteralPath $App -Recurse -Force }
        'Removed the tray. Its settings and log stay in ' + $Home_ + '.'
    }
}
