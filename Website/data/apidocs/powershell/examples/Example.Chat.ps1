$moduleManifest = Join-Path (Split-Path -Parent $PSScriptRoot) 'IntelligenceX.PowerShell.psd1'
if (Test-Path -LiteralPath $moduleManifest) {
    Import-Module $moduleManifest -Force -ErrorAction Stop
} else {
    Import-Module IntelligenceX.PowerShell -Force -ErrorAction Stop
}

$client = Connect-IntelligenceX
Initialize-IntelligenceX -Client $client -Name 'IntelligenceX.Examples' -Title 'IntelligenceX Examples' -Version '0.1.0'

$login = Start-IntelligenceXChatGptLogin -Client $client
Write-Output "Open this URL to login: $($login.AuthUrl)"
Wait-IntelligenceXLogin -Client $client -LoginId $login.LoginId

$thread = Start-IntelligenceXThread -Client $client -Model 'gpt-5.3-codex'
Send-IntelligenceXMessage -Client $client -ThreadId $thread.Id -Text 'Hello from PowerShell.'

Disconnect-IntelligenceX -Client $client
