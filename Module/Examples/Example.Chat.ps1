Import-Module "$PSScriptRoot\..\IntelligenceX.psd1" -Force

$client = Connect-IntelligenceX
Initialize-IntelligenceX -Client $client -Name 'IntelligenceX.Examples' -Title 'IntelligenceX Examples' -Version '0.1.0'

$login = Start-IntelligenceXChatGptLogin -Client $client
Write-Host "Open this URL to login: $($login.AuthUrl)"
Wait-IntelligenceXLogin -Client $client -LoginId $login.LoginId

$thread = Start-IntelligenceXThread -Client $client -Model 'gpt-5.4'
Send-IntelligenceXMessage -Client $client -ThreadId $thread.Id -Text 'Hello from PowerShell.'

Disconnect-IntelligenceX -Client $client
