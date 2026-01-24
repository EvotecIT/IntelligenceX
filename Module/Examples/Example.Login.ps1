Import-Module "$PSScriptRoot\..\IntelligenceX.psd1" -Force

$client = Connect-IntelligenceX
Initialize-IntelligenceX -Client $client -Name 'IntelligenceX.Examples' -Title 'IntelligenceX Examples' -Version '0.1.0'
$login = Start-IntelligenceXChatGptLogin -Client $client
Write-Host "Open this URL to login: $($login.AuthUrl)"
Wait-IntelligenceXLogin -Client $client -LoginId $login.LoginId
$account = Get-IntelligenceXAccount -Client $client
Write-Host "Logged in as $($account.Email)"
Disconnect-IntelligenceX -Client $client
