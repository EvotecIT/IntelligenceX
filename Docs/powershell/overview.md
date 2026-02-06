# PowerShell Module Overview

The PowerShell module wraps the core library with binary cmdlets (net472/net8).

## Build

```powershell
./Module/Build/Build-Module.ps1
```

## Import and use

```powershell
Import-Module ./Module/IntelligenceX.psd1 -Force
$client = Connect-IntelligenceX
Initialize-IntelligenceX -Client $client -Name 'IntelligenceX' -Title 'Demo' -Version '0.1.0'
$login = Start-IntelligenceXChatGptLogin -Client $client
Write-Host $login.AuthUrl
Wait-IntelligenceXLogin -Client $client -LoginId $login.LoginId
$thread = Start-IntelligenceXThread -Client $client -Model 'gpt-5.3-codex'
Send-IntelligenceXMessage -Client $client -ThreadId $thread.Id -Text 'Hello from PowerShell'
Disconnect-IntelligenceX -Client $client
```

Quickstart: [PowerShell Quickstart](/docs/powershell/quickstart/)

## Diagnostics

```powershell
Connect-IntelligenceX -Diagnostics
Get-IntelligenceXHealth
```

Examples live in `Module/Examples`.
