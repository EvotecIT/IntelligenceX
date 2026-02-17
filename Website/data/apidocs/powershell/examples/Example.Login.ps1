Import-Module "$PSScriptRoot\..\IntelligenceX.psd1" -Force

# Optional: configure defaults via .intelligencex/config.json (see Module/Examples/.intelligencex/config.json)
# or set $env:INTELLIGENCEX_CONFIG_PATH to point to a shared config file.

$client = Connect-IntelligenceX
Initialize-IntelligenceX -Client $client -Name 'IntelligenceX.Examples' -Title 'IntelligenceX Examples' -Version '0.1.0'
$login = Start-IntelligenceXChatGptLogin -Client $client
Write-Host "Open this URL to login: $($login.AuthUrl)"
Wait-IntelligenceXLogin -Client $client -LoginId $login.LoginId
$account = Get-IntelligenceXAccount -Client $client
Write-Host "Logged in as $($account.Email)"
Disconnect-IntelligenceX -Client $client
