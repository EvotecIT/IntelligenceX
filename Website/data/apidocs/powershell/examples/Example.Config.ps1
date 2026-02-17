Import-Module "$PSScriptRoot\..\IntelligenceX.psd1" -Force

$client = Connect-IntelligenceX

try {
    # Read effective config and inspect origin metadata.
    $config = Get-IntelligenceXConfig -Client $client
    $config.Config
    $config.Origins["model"]

    # Write one value.
    Set-IntelligenceXConfigValue -Client $client -Key "model" -Value "gpt-5.3-codex"

    # Write multiple values in one call.
    Set-IntelligenceXConfigBatch -Client $client -Values @{
        approvalPolicy = "on-failure"
        stream = $true
    }

    # Verify effective result.
    (Get-IntelligenceXConfig -Client $client).Config
} finally {
    Disconnect-IntelligenceX -Client $client
}
