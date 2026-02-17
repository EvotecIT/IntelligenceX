$moduleManifest = Join-Path (Split-Path -Parent $PSScriptRoot) 'IntelligenceX.PowerShell.psd1'
if (Test-Path -LiteralPath $moduleManifest) {
    Import-Module $moduleManifest -Force -ErrorAction Stop
} else {
    Import-Module IntelligenceX.PowerShell -Force -ErrorAction Stop
}

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
