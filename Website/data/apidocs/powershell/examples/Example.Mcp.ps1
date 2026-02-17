Import-Module "$PSScriptRoot\..\IntelligenceX.psd1" -Force

$client = Connect-IntelligenceX

try {
    # List MCP servers and current auth status.
    $status = Get-IntelligenceXMcpServerStatus -Client $client
    $status.Servers | Select-Object Name, AuthStatus

    # Start OAuth for the first OAuth-enabled server.
    $oauthServer = $status.Servers | Where-Object AuthStatus -eq "OAuth" | Select-Object -First 1
    if ($oauthServer) {
        $login = Start-IntelligenceXMcpOAuthLogin -Client $client -ServerName $oauthServer.Name
        Write-Host "Open MCP OAuth URL: $($login.AuthUrl)"
    }

    # Reload MCP config after local file edits.
    Invoke-IntelligenceXMcpServerConfigReload -Client $client
} finally {
    Disconnect-IntelligenceX -Client $client
}
