Import-Module "$PSScriptRoot\..\IntelligenceX.psd1" -Force

# Optional: configure defaults via .intelligencex/config.json (see Module/Examples/.intelligencex/config.json)

try {
    $client = Connect-IntelligenceX -Diagnostics

    # OpenAI app-server health (uses active client)
    Get-IntelligenceXHealth

    # Copilot CLI health (optional)
    # Get-IntelligenceXHealth -Copilot
} finally {
    if ($client) {
        Disconnect-IntelligenceX -Client $client
    }
}
