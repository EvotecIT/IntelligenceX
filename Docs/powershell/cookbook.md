---
title: PowerShell Cookbook
description: Copy practical IntelligenceX PowerShell recipes for bootstrapping sessions, chat, config, MCP, and automation workflows.
---

# PowerShell Cookbook

Practical PowerShell workflows you can copy, adapt, and run.

## Workflow: Bootstrap A Session

```powershell
Import-Module ./Module/IntelligenceX.psd1 -Force

$client = Connect-IntelligenceX
Initialize-IntelligenceX -Client $client -Name "Repo.Automation" -Title "Repo Automation" -Version "1.0.0"

$login = Start-IntelligenceXChatGptLogin -Client $client
Write-Output "Open: $($login.AuthUrl)"
Wait-IntelligenceXLogin -Client $client -LoginId $login.LoginId
Get-IntelligenceXAccount -Client $client
```

## Workflow: Start A Thread, Chat, Review

```powershell
$thread = Start-IntelligenceXThread -Client $client -Model "gpt-5.3-codex"
Send-IntelligenceXMessage -Client $client -ThreadId $thread.Id -Text "Summarize current repo risks."

$review = Start-IntelligenceXReview -Client $client -ThreadId $thread.Id -Delivery immediate -TargetType uncommittedChanges
$review
```

## Workflow: Non-Interactive CI (API Key)

```powershell
Import-Module ./Module/IntelligenceX.psd1 -Force

$client = Connect-IntelligenceX -Transport Native
Initialize-IntelligenceX -Client $client -Name "CI.PowerShell" -Title "CI PowerShell" -Version "1.0.0"
Start-IntelligenceXApiKeyLogin -Client $client -ApiKey (Get-Item Env:OPENAI_API_KEY).Value

Invoke-IntelligenceXChat -Client $client -Text "Summarize changed files." -WaitSeconds 20
Disconnect-IntelligenceX -Client $client
```

## Workflow: MCP OAuth Onboarding

```powershell
$status = Get-IntelligenceXMcpServerStatus -Client $client
$status.Servers | Select-Object Name, AuthStatus

$oauthStatus = [IntelligenceX.OpenAI.AppServer.Models.McpAuthStatus]::OAuth
$oauthServer = $status.Servers | Where-Object { $_.AuthStatus -eq $oauthStatus } | Select-Object -First 1

if ($oauthServer) {
    $login = Start-IntelligenceXMcpOAuthLogin -Client $client -ServerName $oauthServer.Name
    Start-Process $login.AuthUrl
}
```

## Workflow: Config Baseline + Validation

```powershell
Set-IntelligenceXConfigBatch -Client $client -Values @{
    model = "gpt-5.3-codex"
    approvalPolicy = "on-failure"
    stream = $true
}

$config = Get-IntelligenceXConfig -Client $client
$config.Config
$config.Origins["model"]
```

## Workflow: Script Cleanup Pattern

```powershell
try {
    $client = Connect-IntelligenceX
    Initialize-IntelligenceX -Client $client -Name "Ops.Script" -Title "Ops Script" -Version "1.0.0"
    Get-IntelligenceXHealth -Client $client
} finally {
    if ($client) {
        Disconnect-IntelligenceX -Client $client
    }
}
```
