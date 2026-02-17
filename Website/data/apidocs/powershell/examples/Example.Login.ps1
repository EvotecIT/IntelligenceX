$moduleManifest = Join-Path (Split-Path -Parent $PSScriptRoot) 'IntelligenceX.PowerShell.psd1'
if (Test-Path -LiteralPath $moduleManifest) {
    Import-Module $moduleManifest -Force -ErrorAction Stop
} else {
    Import-Module IntelligenceX.PowerShell -Force -ErrorAction Stop
}

# Optional: configure defaults via .intelligencex/config.json (see Module/Examples/.intelligencex/config.json)
# or set $env:INTELLIGENCEX_CONFIG_PATH to point to a shared config file.

$client = Connect-IntelligenceX
Initialize-IntelligenceX -Client $client -Name 'IntelligenceX.Examples' -Title 'IntelligenceX Examples' -Version '0.1.0'
$login = Start-IntelligenceXChatGptLogin -Client $client
Write-Output "Open this URL to login: $($login.AuthUrl)"
Wait-IntelligenceXLogin -Client $client -LoginId $login.LoginId
$account = Get-IntelligenceXAccount -Client $client
Write-Output "Logged in as $($account.Email)"
Disconnect-IntelligenceX -Client $client
