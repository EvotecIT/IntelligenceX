# PowerShell Quickstart

This quickstart walks through the common IntelligenceX PowerShell flow: connect, authenticate, chat, inspect MCP status, and update runtime configuration.

## 1. Import + Connect

```powershell
Import-Module ./Module/IntelligenceX.psd1 -Force

# Native transport is the default and works with ChatGPT OAuth.
$client = Connect-IntelligenceX
Initialize-IntelligenceX -Client $client -Name "IntelligenceX" -Title "PowerShell" -Version "0.1.0"
```

Use app-server transport explicitly when needed:

```powershell
$client = Connect-IntelligenceX -Transport AppServer -ExecutablePath "codex"
```

## 2. Login (ChatGPT OAuth)

```powershell
$login = Start-IntelligenceXChatGptLogin -Client $client
Write-Host "Open this URL in your browser: $($login.AuthUrl)"
Wait-IntelligenceXLogin -Client $client -LoginId $login.LoginId
```

## 3. Start A Thread + Send A Message

```powershell
$thread = Start-IntelligenceXThread -Client $client -Model "gpt-5.3-codex"
$turn = Send-IntelligenceXMessage -Client $client -ThreadId $thread.Id -Text "Hello from PowerShell"
$turn | Get-IntelligenceXTurnOutput
```

## 4. One-Liner Chat

```powershell
Invoke-IntelligenceXChat -Text "Summarize this repo." -Stream -WaitSeconds 10
```

## 5. Run A Review In A Thread

```powershell
$review = Start-IntelligenceXReview -ThreadId $thread.Id -Delivery immediate -TargetType uncommittedChanges
```

## 6. MCP Status + OAuth Login

```powershell
$status = Get-IntelligenceXMcpServerStatus
$status.Servers | Select-Object Name, AuthStatus

$oauthStatus = [IntelligenceX.OpenAI.AppServer.Models.McpAuthStatus]::OAuth
$oauthServer = $status.Servers | Where-Object { $_.AuthStatus -eq $oauthStatus } | Select-Object -First 1
if ($oauthServer) {
    $oauth = Start-IntelligenceXMcpOAuthLogin -ServerName $oauthServer.Name
    Write-Output "Complete MCP login at: $($oauth.AuthUrl)"
}
```

If you edited MCP configuration files, reload them:

```powershell
Invoke-IntelligenceXMcpServerConfigReload
```

## 7. Read + Update Configuration

```powershell
$config = Get-IntelligenceXConfig
$config.Config
$config.Origins["model"]

Set-IntelligenceXConfigValue -Key "model" -Value "gpt-5.3-codex"
Set-IntelligenceXConfigBatch -Values @{
    approvalPolicy = "on-failure"
    stream = $true
}
```

## 8. Disconnect

```powershell
Disconnect-IntelligenceX -Client $client
```
