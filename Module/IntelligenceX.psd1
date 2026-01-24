@{
    AliasesToExport      = @()
    Author               = 'Przemyslaw Klys'
    CmdletsToExport      = @(
        'Connect-IntelligenceX',
        'Disconnect-IntelligenceX',
        'Initialize-IntelligenceX',
        'Start-IntelligenceXChatGptLogin',
        'Start-IntelligenceXApiKeyLogin',
        'Wait-IntelligenceXLogin',
        'Get-IntelligenceXAccount',
        'Start-IntelligenceXThread',
        'Send-IntelligenceXMessage',
        'Get-IntelligenceXThread',
        'Get-IntelligenceXLoadedThread',
        'Resume-IntelligenceXThread',
        'New-IntelligenceXThreadFork',
        'Backup-IntelligenceXThread',
        'Restore-IntelligenceXThread',
        'Stop-IntelligenceXTurn',
        'Start-IntelligenceXReview',
        'Invoke-IntelligenceXCommand',
        'Invoke-IntelligenceXRpc',
        'Get-IntelligenceXModel',
        'Get-IntelligenceXCollaborationMode',
        'Get-IntelligenceXSkill',
        'Set-IntelligenceXSkill',
        'Get-IntelligenceXConfig',
        'Set-IntelligenceXConfigValue',
        'Set-IntelligenceXConfigBatch',
        'Get-IntelligenceXConfigRequirements',
        'Start-IntelligenceXMcpOAuthLogin',
        'Get-IntelligenceXMcpServerStatus',
        'Invoke-IntelligenceXMcpServerConfigReload',
        'Request-IntelligenceXUserInput',
        'Send-IntelligenceXFeedback',
        'Watch-IntelligenceXEvent'
    )
    CompanyName          = 'Evotec'
    CompatiblePSEditions = @('Desktop', 'Core')
    Copyright            = '(c) 2011 - 2026 Przemyslaw Klys @ Evotec. All rights reserved.'
    Description          = 'IntelligenceX is a PowerShell module for the Codex app-server client.'
    FunctionsToExport    = @()
    GUID                 = '8fc3e038-c57b-44f3-bc7f-714beb3bd65a'
    HelpInfoURI          = 'https://github.com/EvotecIT/IntelligenceX/blob/master/README.md'
    ModuleVersion        = '0.1.0'
    PowerShellVersion    = '5.1'
    PrivateData          = @{
        PSData = @{
            IconUri    = 'https://raw.githubusercontent.com/EvotecIT/IntelligenceX/master/Assets/Icons/IntelligenceX_128x128.png'
            ProjectUri = 'https://github.com/EvotecIT/IntelligenceX'
            Tags       = @('Windows', 'MacOS', 'Linux')
        }
    }
    RootModule           = 'IntelligenceX.psm1'
}
