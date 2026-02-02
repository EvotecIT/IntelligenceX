# PowerShell Quickstart

## Connect + login + chat

```powershell
Import-Module ./Module/IntelligenceX.psd1 -Force
$client = Connect-IntelligenceX
Initialize-IntelligenceX -Client $client -Name "IntelligenceX" -Title "PowerShell" -Version "0.1.0"
$login = Start-IntelligenceXChatGptLogin -Client $client
Write-Host $login.AuthUrl
Wait-IntelligenceXLogin -Client $client -LoginId $login.LoginId
$thread = Start-IntelligenceXThread -Client $client -Model "gpt-5.2-codex"
$turn = Send-IntelligenceXMessage -Client $client -ThreadId $thread.Id -Text "Hello from PowerShell"
$turn | Get-IntelligenceXTurnOutput
Disconnect-IntelligenceX -Client $client
```

## One-liner chat

```powershell
Invoke-IntelligenceXChat -Text "Summarize this repo." -Stream -WaitSeconds 10
```

## Run a review in a thread

```powershell
$review = Start-IntelligenceXReview -ThreadId $thread.Id -Delivery immediate -TargetType uncommittedChanges
```
