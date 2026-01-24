@{
    RootModule        = 'IntelligenceX.psm1'
    ModuleVersion     = '0.1.0'
    GUID              = '8fc3e038-c57b-44f3-bc7f-714beb3bd65a'
    Author            = 'Przemyslaw Klys'
    CompanyName       = 'Evotec'
    Copyright         = "(c) 2011 - $((Get-Date).Year) Przemyslaw Klys @ Evotec. All rights reserved."
    Description       = 'PowerShell module for IntelligenceX Codex app-server client.'
    PowerShellVersion = '5.1'
    CompatiblePSEditions = @('Desktop', 'Core')
    FunctionsToExport = @(
        'Connect-IntelligenceX',
        'Disconnect-IntelligenceX',
        'Initialize-IntelligenceX',
        'Start-IntelligenceXChatGptLogin',
        'Start-IntelligenceXApiKeyLogin',
        'Wait-IntelligenceXLogin',
        'Get-IntelligenceXAccount',
        'Start-IntelligenceXThread',
        'Send-IntelligenceXMessage'
    )
    CmdletsToExport = @()
    AliasesToExport = @()
    PrivateData = @{
        PSData = @{
            Tags       = @('OpenAI', 'Codex', 'ChatGPT', 'AI')
            ProjectUri = 'https://github.com/EvotecIT/IntelligenceX'
        }
    }
}
