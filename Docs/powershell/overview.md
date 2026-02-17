# PowerShell Module Overview

The IntelligenceX PowerShell module exposes the app-server client as native cmdlets, so you can automate chat, threads, reviews, config, MCP servers, and diagnostics from scripts and CI pipelines.

## What You Get

- Strongly-typed cmdlets for common IntelligenceX workflows.
- Raw JSON mode (`-Raw`) when you need full protocol payloads.
- MCP management cmdlets for status, OAuth login, and config reload.
- Compatible build targets for Windows PowerShell and PowerShell 7+.

## Build The Module

```powershell
./Module/Build/Build-Module.ps1
```

## Core Workflow

```powershell
Import-Module ./Module/IntelligenceX.psd1 -Force

$client = Connect-IntelligenceX
Initialize-IntelligenceX -Client $client -Name 'IntelligenceX' -Title 'Demo' -Version '0.1.0'

$login = Start-IntelligenceXChatGptLogin -Client $client
Write-Host "Login URL: $($login.AuthUrl)"
Wait-IntelligenceXLogin -Client $client -LoginId $login.LoginId

$thread = Start-IntelligenceXThread -Client $client -Model 'gpt-5.3-codex'
$turn = Send-IntelligenceXMessage -Client $client -ThreadId $thread.Id -Text 'Hello from PowerShell'
$turn | Get-IntelligenceXTurnOutput

Disconnect-IntelligenceX -Client $client
```

## MCP Workflow

```powershell
# Inspect configured MCP servers and auth state.
$status = Get-IntelligenceXMcpServerStatus
$status.Servers | Select-Object Name, AuthStatus

# Start OAuth when needed.
$oauthStatus = [IntelligenceX.OpenAI.AppServer.Models.McpAuthStatus]::OAuth
$oauthServer = $status.Servers | Where-Object { $_.AuthStatus -eq $oauthStatus } | Select-Object -First 1
if ($oauthServer) {
    $login = Start-IntelligenceXMcpOAuthLogin -ServerName $oauthServer.Name
    Start-Process $login.AuthUrl
}

# Reload MCP config after editing configuration files.
Invoke-IntelligenceXMcpServerConfigReload
```

## Config Workflow

```powershell
# Read effective config and origin metadata.
$config = Get-IntelligenceXConfig
$config.Config
$config.Origins['model']

# Apply updates.
Set-IntelligenceXConfigValue -Key 'model' -Value 'gpt-5.3-codex'
Set-IntelligenceXConfigBatch -Values @{
    approvalPolicy = 'on-failure'
    stream = $true
}
```

## Diagnostics

```powershell
Connect-IntelligenceX -Diagnostics
Get-IntelligenceXHealth
Get-IntelligenceXHealth -Copilot
```

## Next Docs

- Quickstart: [PowerShell Quickstart](/docs/powershell/quickstart/)
- Full cmdlet reference: [/api/powershell/](/api/powershell/)
- Script examples: `Module/Examples`
