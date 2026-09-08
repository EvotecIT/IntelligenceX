$moduleManifest = Join-Path (Split-Path -Parent $PSScriptRoot) 'IntelligenceX.PowerShell.psd1'
if (Test-Path -LiteralPath $moduleManifest) {
    Import-Module $moduleManifest -Force -ErrorAction Stop
} else {
    Import-Module IntelligenceX.PowerShell -Force -ErrorAction Stop
}

$client = Connect-IntelligenceX

try {
    # List MCP servers and current auth status.
    $status = Get-IntelligenceXMcpServerStatus -Client $client
    $status.Servers | Select-Object Name, AuthStatus

    # Start OAuth for the first OAuth-enabled server.
    $oauthStatus = [IntelligenceX.OpenAI.AppServer.Models.McpAuthStatus]::OAuth
    $oauthServer = $status.Servers | Where-Object { $_.AuthStatus -eq $oauthStatus } | Select-Object -First 1
    if ($oauthServer) {
        $login = Start-IntelligenceXMcpOAuthLogin -Client $client -ServerName $oauthServer.Name
        Write-Output "Open MCP OAuth URL: $($login.AuthUrl)"
    }

    # Reload MCP config after local file edits.
    Invoke-IntelligenceXMcpServerConfigReload -Client $client
} finally {
    Disconnect-IntelligenceX -Client $client
}
