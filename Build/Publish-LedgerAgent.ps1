#Requires -Version 7.5
<#
.SYNOPSIS
Publishes the Ledger reporter and service with the agent service and its ask tools, as one kit for one platform.
.DESCRIPTION
The kit has the layout the Ledger service and reporter installers expect, plus what the agent service needs:

- win-x64: service/ and manifest.json for Update-LedgerRuntime.ps1.
- osx-arm64: app/ and manifest.json (with the runtime) for Install-MacLedgerReporter.ps1.
- tools/mcp: the MCP server with the ask tools.
- tools/hook: the Claude inbox hook as a Native AOT executable. Native AOT builds only for the operating system it
  runs on, so build each platform's kit on that platform; -NoHook leaves it out.
- agent-manifest.json and Install-LedgerAgent.ps1, which installs the agent service from this kit.

The manifest names the exact source commit, so the working tree must be clean.
.EXAMPLE
./Build/Publish-LedgerAgent.ps1 -Runtime win-x64 -Output D:\Dev\Scratch\ledger-kit
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateSet('win-x64', 'osx-arm64')][string]$Runtime,
    [Parameter(Mandatory)][string]$Output,
    [switch]$NoHook
)
$ErrorActionPreference = 'Stop'
$repository = Split-Path $PSScriptRoot -Parent
$commit = (git -C $repository rev-parse HEAD).Trim()
if (git -C $repository status --porcelain) { throw 'Commit or stash changes first; the kit records its exact source commit.' }
$Output = [IO.Path]::GetFullPath($Output)
if (Test-Path -LiteralPath $Output) { throw "The output folder already exists: $Output" }
$appFolder = if ($Runtime -eq 'win-x64') { 'service' } else { 'app' }
$app = Join-Path $Output $appFolder

function Publish([string]$Project, [string]$Destination, [string[]]$Extra = @()) {
    $arguments = @('publish', (Join-Path $repository $Project), '-c', 'Release', '-r', $Runtime, '-o', $Destination, '--nologo') + $Extra
    & dotnet @arguments | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed for $Project." }
}

Publish 'IntelligenceX.Ledger/IntelligenceX.Ledger.csproj' $app @('--self-contained', 'false')
Publish 'IntelligenceX.Ledger.Mcp/IntelligenceX.Ledger.Mcp.csproj' (Join-Path $Output 'tools/mcp') @('--self-contained', 'false')
if (-not $NoHook) {
    # The native linker on Windows is found through vswhere, which the Visual Studio installer doesn't put on PATH.
    if ($IsWindows -and -not (Get-Command vswhere -ErrorAction SilentlyContinue)) {
        $installer = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer'
        if (Test-Path (Join-Path $installer 'vswhere.exe')) { $env:PATH = $installer + [IO.Path]::PathSeparator + $env:PATH }
    }
    $hook = Join-Path $Output 'tools/hook'
    Publish 'IntelligenceX.Ledger.Hook/IntelligenceX.Ledger.Hook.csproj' $hook
    Get-ChildItem -LiteralPath $hook -Filter *.pdb | Remove-Item
    Get-ChildItem -LiteralPath $hook -Filter *.dSYM -Directory | Remove-Item -Recurse
}

# Update-LedgerRuntime.ps1 reads service/-relative paths with upper-case hashes; the Mac installer reads app-relative
# paths with lower-case hashes and the runtime.
$files = Get-ChildItem -LiteralPath $app -File -Recurse | Sort-Object FullName | ForEach-Object {
    $relative = [IO.Path]::GetRelativePath($app, $_.FullName).Replace('\', '/')
    $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
    if ($Runtime -eq 'win-x64') { [ordered]@{ path = "service/$relative"; sha256 = $hash.ToUpperInvariant() } }
    else { [ordered]@{ path = $relative; sha256 = $hash.ToLowerInvariant() } }
}
$manifest = [ordered]@{ sourceCommit = $commit }
if ($Runtime -ne 'win-x64') { $manifest.runtime = $Runtime }
$manifest.files = @($files)
$manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $Output 'manifest.json') -Encoding utf8NoBOM

# Install-LedgerAgent.ps1 verifies every file it installs (the app and the tools) against agent-manifest.json.
$agentFiles = foreach ($folder in $appFolder, 'tools') {
    Get-ChildItem -LiteralPath (Join-Path $Output $folder) -File -Recurse | Sort-Object FullName | ForEach-Object {
        [ordered]@{ path = [IO.Path]::GetRelativePath($Output, $_.FullName).Replace('\', '/')
            sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
    }
}
[ordered]@{ sourceCommit = $commit; runtime = $Runtime; appFolder = $appFolder; files = @($agentFiles) } |
    ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $Output 'agent-manifest.json') -Encoding utf8NoBOM
Copy-Item -LiteralPath (Join-Path $repository 'IntelligenceX.Ledger/Install/Install-LedgerAgent.ps1') -Destination $Output
[pscustomobject]@{ Kit = $Output; Runtime = $Runtime; SourceCommit = $commit; Files = $files.Count; Hook = -not $NoHook }
