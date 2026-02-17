# PowerShell Troubleshooting

Common issues and direct fixes for IntelligenceX PowerShell usage.

## Connect-IntelligenceX Fails With Executable Not Found

Symptom:
- `Connect-IntelligenceX -Transport AppServer` throws an error about Codex app-server not found.

Fix:
```powershell
Connect-IntelligenceX -Transport AppServer -ExecutablePath "C:\tools\codex.exe"
```

Fallback:
```powershell
Connect-IntelligenceX -Transport Native
```

## Login Never Completes

Symptom:
- `Wait-IntelligenceXLogin` times out.

Fix checklist:
1. Start login again and open the exact URL from `AuthUrl`.
2. Wait using explicit login id.
3. Verify account after completion.

```powershell
$login = Start-IntelligenceXChatGptLogin
Write-Output $login.AuthUrl
Wait-IntelligenceXLogin -LoginId $login.LoginId -TimeoutSeconds 300
Get-IntelligenceXAccount
```

## Raw Output Needed But Typed Result Missing Field

Use `-Raw` to inspect full payload:

```powershell
Get-IntelligenceXMcpServerStatus -Raw
Get-IntelligenceXConfig -Raw
Invoke-IntelligenceXRpc -Method "config/read"
```

## MCP OAuth Never Starts

Symptom:
- No server is found for OAuth workflow.

Fix:
- Filter using enum-safe auth status.
- Reload MCP config if files changed.

```powershell
Invoke-IntelligenceXMcpServerConfigReload

$status = Get-IntelligenceXMcpServerStatus
$oauthStatus = [IntelligenceX.OpenAI.AppServer.Models.McpAuthStatus]::OAuth
$oauthServer = $status.Servers | Where-Object { $_.AuthStatus -eq $oauthStatus } | Select-Object -First 1

if ($oauthServer) {
    Start-IntelligenceXMcpOAuthLogin -ServerName $oauthServer.Name
}
```

## Turn Is Stuck Or Running Too Long

```powershell
Stop-IntelligenceXTurn -ThreadId $thread.Id -TurnId $turn.Id
```

Then continue from the same thread:

```powershell
Resume-IntelligenceXThread -ThreadId $thread.Id
```

## Need To Rewind Conversation State

```powershell
Restore-IntelligenceXThread -ThreadId $thread.Id -Turns 1
```

## Health Checks For Diagnostics

```powershell
Get-IntelligenceXHealth
Get-IntelligenceXHealth -Copilot
```

Enable detailed transport diagnostics:

```powershell
Connect-IntelligenceX -Diagnostics
Watch-IntelligenceXEvent
```

## Script Hygiene

Always disconnect in `finally`:

```powershell
try {
    $client = Connect-IntelligenceX
    Initialize-IntelligenceX -Client $client -Name "Troubleshoot" -Title "Troubleshoot" -Version "1.0.0"
    Get-IntelligenceXHealth -Client $client
} finally {
    if ($client) {
        Disconnect-IntelligenceX -Client $client
    }
}
```

## Related Docs

- [PowerShell Quickstart](/docs/powershell/quickstart/)
- [PowerShell Cookbook](/docs/powershell/cookbook/)
- [PowerShell API Reference](/api/powershell/)

